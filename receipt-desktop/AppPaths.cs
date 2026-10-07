using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ReceiptTray;

static class AppPaths
{
    public static string DataDirectory { get; private set; } = "";
    public static string AssetsDirectory { get; private set; } = "";
    public static string CodexHome => Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } configured
        ? Path.GetFullPath(configured) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public static string? SelectedCodex { get; private set; }
    public static void Initialize(string[] args)
    {
        int option = Array.IndexOf(args, "--data-dir");
        DataDirectory = option >= 0 && option + 1 < args.Length ? Path.GetFullPath(args[option + 1])
            : args.Contains("--self-test") ? Path.Combine(Environment.CurrentDirectory, ".verification", "self-test")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexReceipt");
        Directory.CreateDirectory(DataDirectory);
        try { SelectedCodex = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DataDirectory, "preferences.json")))?.CodexExecutable; }
        catch (Exception ex) when (ex is IOException or JsonException) { }
    }
    public static void SaveCodex(string path)
    {
        SelectedCodex = Path.GetFullPath(path);
        File.WriteAllText(Path.Combine(DataDirectory, "preferences.json"), JsonSerializer.Serialize(new Preferences(SelectedCodex)));
    }
    public static void ExtractAssets()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("ReceiptTray.ui.zip")
            ?? throw new InvalidOperationException("程序缺少内置界面资源。");
        var hash = Convert.ToHexString(SHA256.HashData(resource))[..16];
        resource.Position = 0;
        AssetsDirectory = Path.Combine(DataDirectory, "ui", hash);
        Directory.CreateDirectory(AssetsDirectory);
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(AssetsDirectory, entry.FullName));
            if (!destination.StartsWith(AssetsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("界面资源路径无效。");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }
    sealed record Preferences(string? CodexExecutable);
}
