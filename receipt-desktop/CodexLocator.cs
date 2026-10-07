namespace ReceiptTray;

static class CodexLocator
{
    public static string? Find()
    {
        if (AppPaths.SelectedCodex is { } selected && File.Exists(selected)) return selected;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var roots = new[] {
            Path.Combine(local, "OpenAI", "Codex", "bin"),
            Path.Combine(local, "Programs", "Codex", "resources"),
            Path.Combine(roaming, "npm", "node_modules", "@openai"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions")
        };
        foreach (var root in roots)
        {
            var found = FindUnder(root);
            if (found != null) return found;
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            try { var candidate = Path.Combine(directory.Trim('"'), "codex.exe"); if (File.Exists(candidate)) return candidate; }
            catch (ArgumentException) { }
        }
        return null;
    }
    internal static string? FindUnder(string root)
    {
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateFiles(root, "codex.exe", new EnumerationOptions {
                RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 9,
                AttributesToSkip = FileAttributes.ReparsePoint
            }).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        catch (IOException) { return null; }
    }
}
