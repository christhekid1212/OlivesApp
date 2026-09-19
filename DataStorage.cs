using System.IO;
using System.Security.Cryptography;

namespace OlivesApp;

public static class DataStorage
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OlivesApp", "Αρχείο");

    public static void Initialize()
    {
        Directory.CreateDirectory(Folder);
        var legacy = Path.Combine(AppContext.BaseDirectory, "Αρχείο");
        if (Directory.Exists(legacy)) Import(legacy, Folder);
    }

    public static int Import(string source, string destination)
    {
        if (Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return 0;
        Directory.CreateDirectory(destination);
        using var sourceLock = new FileStream(Path.Combine(source, ".save.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var destinationLock = new FileStream(Path.Combine(destination, ".save.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // Validate everything before copying. Never overwrite a different workbook.
        var maximum = Math.Max(new Archive(source).NextNumber(), new Archive(destination).NextNumber()) - 1;
        var pending = new List<(string Source, string Destination)>();
        foreach (var file in Directory.EnumerateFiles(source, "*.xlsx"))
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            if (File.Exists(target))
            {
                using var a = File.OpenRead(file); using var b = File.OpenRead(target);
                if (!SHA256.HashData(a).SequenceEqual(SHA256.HashData(b)))
                    throw new IOException("Υπάρχει διαφορετικό αρχείο με το ίδιο όνομα: " + Path.GetFileName(file));
            }
            else pending.Add((file, target));
        }
        foreach (var file in pending)
        {
            var temp = Path.Combine(destination, Guid.NewGuid().ToString("N") + ".tmp");
            try { File.Copy(file.Source, temp); File.Move(temp, file.Destination, false); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        var counterTemp = Path.Combine(destination, Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(counterTemp, maximum.ToString(System.Globalization.CultureInfo.InvariantCulture)); File.Move(counterTemp, Path.Combine(destination, ".sequence"), true); }
        finally { if (File.Exists(counterTemp)) File.Delete(counterTemp); }
        return pending.Count;
    }
}
