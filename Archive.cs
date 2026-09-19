using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace OlivesApp;

public record EntryRow(int Category, decimal Weight, int Bins);

public sealed class Archive(string folder)
{
    public static readonly int[] Categories = [110, 120, 130, 140, 150, 160, 180, 200];
    public string Folder { get; } = folder;
    private string Counter => Path.Combine(Folder, ".sequence");

    public long NextNumber()
    {
        Directory.CreateDirectory(Folder);
        long maximum = 0;
        if (File.Exists(Counter))
        {
            if (!long.TryParse(File.ReadAllText(Counter).Trim(), out maximum) || maximum < 0)
                throw new IOException("Το αρχείο αρίθμησης δεν είναι έγκυρο. Απαιτείται έλεγχος του αρχείου .sequence.");
        }
        foreach (var path in Directory.EnumerateFiles(Folder, "*.xlsx"))
            if (long.TryParse(Path.GetFileName(path).Split('_')[0], out var number)) maximum = Math.Max(maximum, number);
        return checked(maximum + 1);
    }

    public string Save(IReadOnlyList<EntryRow> rows)
    {
        if (rows.Count != Categories.Length || rows.Where((r, i) => r.Category != Categories[i] || r.Weight < 0 || r.Bins < 0).Any())
            throw new ArgumentException("Μη έγκυρα στοιχεία καταχώρησης.");
        Directory.CreateDirectory(Folder);
        // Serialize saves across processes. The OS releases this handle after a crash.
        using var fileLock = new FileStream(Path.Combine(Folder, ".save.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var number = NextNumber();
        var now = DateTime.Now;
        var destination = Path.Combine(Folder, $"{number:D6}_{now:yyyy-MM-dd_HH-mm-ss}.xlsx");
        var temporary = Path.Combine(Folder, $".{Guid.NewGuid():N}.tmp");
        try
        {
            Workbook.Write(temporary, number, now, rows);
            // Reserve the number before publishing. Failed saves may leave a gap, never a duplicate.
            var counterTemp = temporary + ".sequence";
            try { File.WriteAllText(counterTemp, number.ToString(CultureInfo.InvariantCulture)); File.Move(counterTemp, Counter, true); }
            finally { if (File.Exists(counterTemp)) File.Delete(counterTemp); }
            File.Move(temporary, destination, false);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class Workbook
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static void Write(string path, long number, DateTime time, IReadOnlyList<EntryRow> rows)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Part(string name, string xml) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(xml); }
        Part("[Content_Types].xml", """
        <?xml version="1.0" encoding="utf-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>
        """);
        Part("_rels/.rels", """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
        """);
        Part("xl/workbook.xml", """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Καταχώρηση" sheetId="1" r:id="rId1"/></sheets></workbook>
        """);
        Part("xl/_rels/workbook.xml.rels", """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
        """);
        Part("xl/styles.xml", """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="12"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="12"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF465B31"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="3"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFill="1" applyFont="1"/><xf numFmtId="4" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
        """);
        XElement Text(string cell, string value, int style = 0) => new(S + "c", new XAttribute("r", cell), new XAttribute("t", "inlineStr"), new XAttribute("s", style), new XElement(S + "is", new XElement(S + "t", value)));
        XElement Numeric(string cell, decimal value) => new(S + "c", new XAttribute("r", cell), new XElement(S + "v", value.ToString(CultureInfo.InvariantCulture)));
        XElement Row(int index, params XElement[] cells) => new(S + "row", new XAttribute("r", index), new XAttribute("ht", 24), new XAttribute("customHeight", 1), cells);
        var data = new XElement(S + "sheetData",
            Row(1, Text("A1", "OlivesApp", 1)),
            Row(2, Text("A2", "Αύξων αριθμός"), Numeric("B2", number)),
            Row(3, Text("A3", "Ημερομηνία / ώρα"), Text("B3", time.ToString("dd/MM/yyyy HH:mm:ss"))),
            Row(5, Text("A5", "Κατηγορία", 1), Text("B5", "Βάρος (kg)", 1), Text("C5", "Bins Τεμάχια", 1)));
        for (var i = 0; i < rows.Count; i++)
        {
            int n = i + 6; var r = rows[i];
            data.Add(Row(n, Numeric($"A{n}", r.Category), Numeric($"B{n}", r.Weight), Numeric($"C{n}", r.Bins)));
        }
        int totalRow = rows.Count + 6;
        XElement Total(string column, decimal value) => new(S + "c", new XAttribute("r", $"{column}{totalRow}"), new XAttribute("s", 1),
            new XElement(S + "f", $"SUM({column}6:{column}{totalRow - 1})"), new XElement(S + "v", value.ToString(CultureInfo.InvariantCulture)));
        data.Add(Row(totalRow, Text($"A{totalRow}", "Σύνολα", 1), Total("B", rows.Sum(r => r.Weight)), Total("C", rows.Sum(r => (long)r.Bins))));
        var sheet = new XElement(S + "worksheet",
            new XElement(S + "cols", new XElement(S + "col", new XAttribute("min", 1), new XAttribute("max", 3), new XAttribute("width", 26), new XAttribute("customWidth", 1))), data);
        Part("xl/worksheets/sheet1.xml", new XDocument(sheet).ToString());
    }
}

public static class SelfTest
{
    public static int Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "OlivesApp-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            void Check(bool ok, string test) { if (!ok) throw new Exception(test); }
            Check(Values.Weight("12,345", out var w) && w == 12.345m, "Greek decimal");
            Check(Values.Weight("12.5", out w) && w == 12.5m, "Decimal point");
            Check(!Values.Weight("-1", out _) && !Values.Weight("1,234.5", out _) && !Values.Weight("0.0001", out _), "Invalid weights");
            Check(Values.Weight("", out w) && w == 0, "Empty weight");
            Check(!Values.Bins("1.5", out _) && !Values.Bins("-1", out _) && Values.Bins("12", out _), "Integer bins");
            foreach (var invalid in new[] { "abc", "12α", "1 2", "-1", "1e3", "12\n", "1,2.3" })
                Check(!Values.CanEdit(invalid, true) && !Values.CanEdit(invalid, false), "Reject non-numeric input: " + invalid);
            Check(Values.CanEdit("12,345", true) && Values.CanEdit(".5", true) && Values.CanEdit("12.", true) && Values.CanEdit("", false), "Decimal editing and clearing");
            Check(!Values.CanEdit("1,5", false) && !Values.CanEdit("1.2345", true), "Integer and precision restrictions");
            var input = MainWindow.Cell("Test", true);
            input.Text = "12,5";
            input.Text = "letters";
            Check(input.Text == "12,5", "TextBox rejects invalid replacement");
            input.SelectAll();
            input.SelectedText = "3.25";
            Check(input.Text == "3.25", "TextBox selection replacement");
            input.Clear();
            Check(input.Text == "", "TextBox clearing");
            var archive = new Archive(folder);
            Check(archive.NextNumber() == 1, "Initial sequence");
            var rows = Archive.Categories.Select(c => new EntryRow(c, 12.345m, 3)).ToList();
            var first = archive.Save(rows);
            using (var zip = ZipFile.OpenRead(first))
            {
                foreach (var entry in zip.Entries) { using var stream = entry.Open(); XDocument.Load(stream); }
                using var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
                var xml = XDocument.Load(sheet); XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                Check(xml.Descendants(ns + "row").Count() == 13, "All worksheet rows including totals");
                Check(xml.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == "B6").Element(ns + "v")!.Value == "12.345", "Numeric weight precision");
                var weightTotal = xml.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == "B14");
                var binsTotal = xml.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == "C14");
                Check(weightTotal.Element(ns + "f")!.Value == "SUM(B6:B13)" && weightTotal.Element(ns + "v")!.Value == "98.760", "Weight formula and cached total");
                Check(binsTotal.Element(ns + "f")!.Value == "SUM(C6:C13)" && binsTotal.Element(ns + "v")!.Value == "24", "Bins formula and cached total");
            }
            Check(new Archive(folder).NextNumber() == 2, "Sequence survives restart");
            File.Delete(first);
            Check(archive.NextNumber() == 2, "Sequence survives deletion");
            var second = archive.Save(rows);
            Check(Path.GetFileName(second).StartsWith("000002_"), "Filename sequence");
            File.Delete(Path.Combine(folder, ".sequence"));
            Check(archive.NextNumber() == 3, "Recovery from archive");
            File.WriteAllText(Path.Combine(folder, ".sequence"), "invalid");
            bool rejected = false; try { archive.NextNumber(); } catch (IOException) { rejected = true; }
            Check(rejected, "Corrupt sequence protected");
            DistributionTests.Run(folder);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "PASS: validation, XLSX XML, precision, filenames, durable sequence and recovery.");
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "FAIL: " + ex); return 1; }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
