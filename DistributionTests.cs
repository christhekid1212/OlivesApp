using System.IO;
namespace OlivesApp;
public static class DistributionTests
{
    public static void Run(string root)
    {
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
        var source = Path.Combine(root, "legacy");
        var target = Path.Combine(root, "migrated");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "000005_test.xlsx"), "sample workbook");
        File.WriteAllText(Path.Combine(source, ".sequence"), "9");
        Check(DataStorage.Import(source, target) == 1, "Migration copies workbook");
        Check(File.Exists(Path.Combine(source, "000005_test.xlsx")), "Migration preserves original");
        Check(new Archive(target).NextNumber() == 10, "Migration preserves sequence beyond files");
        Check(DataStorage.Import(source, target) == 0, "Migration is repeatable");
        File.WriteAllText(Path.Combine(source, "000005_test.xlsx"), "conflicting content");
        bool conflict = false;
        try { DataStorage.Import(source, target); } catch (IOException) { conflict = true; }
        Check(conflict && File.ReadAllText(Path.Combine(target, "000005_test.xlsx")) == "sample workbook", "Conflict cannot overwrite data");
        Check(Updates.IsRepositoryUrl("https://github.com/example/OlivesApp"), "GitHub URL accepted");
        Check(!Updates.IsRepositoryUrl("https://github.com.evil.example/a/b") && !Updates.IsRepositoryUrl("http://github.com/a/b") && !Updates.IsRepositoryUrl("https://github.com/a/b?token=secret"), "Invalid update sources rejected");
    }
}
