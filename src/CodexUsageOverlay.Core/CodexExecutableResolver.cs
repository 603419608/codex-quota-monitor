namespace CodexUsageOverlay.Core;

public static class CodexExecutableResolver
{
    public const string OverrideEnvironmentVariable = "CODEX_USAGE_OVERLAY_CODEX_EXE";

    public static string Resolve()
    {
        var overridePath = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        if (IsExecutableFile(overridePath))
        {
            return overridePath!;
        }

        var bundledCli = FindBundledCodexCli();
        if (bundledCli is not null)
        {
            return bundledCli;
        }

        var pathMatch = FindOnPath("codex.exe") ?? FindOnPath("codex.cmd") ?? FindOnPath("codex.bat");
        if (pathMatch is not null)
        {
            return pathMatch;
        }

        var localAlias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            "codex.exe");
        if (IsExecutableFile(localAlias))
        {
            return localAlias;
        }

        var packagedApp = FindPackagedCodex();
        if (packagedApp is not null)
        {
            return packagedApp;
        }

        return "codex";
    }

    private static string? FindBundledCodexCli()
    {
        var binRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI",
            "Codex",
            "bin");
        if (!Directory.Exists(binRoot))
        {
            return null;
        }

        var matches = new List<(string Path, DateTime LastWriteTimeUtc)>();
        foreach (var directory in SafeEnumerateDirectories(binRoot))
        {
            var candidate = Path.Combine(directory, "codex.exe");
            if (!IsExecutableFile(candidate))
            {
                continue;
            }

            try
            {
                matches.Add((candidate, File.GetLastWriteTimeUtc(candidate)));
            }
            catch
            {
                matches.Add((candidate, DateTime.MinValue));
            }
        }

        return matches
            .OrderByDescending(match => match.LastWriteTimeUtc)
            .Select(match => match.Path)
            .FirstOrDefault();
    }

    private static string? FindOnPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (IsExecutableFile(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root);
        }
        catch
        {
            return [];
        }
    }

    private static string? FindPackagedCodex()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WindowsApps")
        };

        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> packageDirs;
            try
            {
                packageDirs = Directory.EnumerateDirectories(root, "OpenAI.Codex_*");
            }
            catch
            {
                continue;
            }

            var matches = new List<(string Path, DateTime LastWriteTimeUtc)>();
            foreach (var packageDir in packageDirs)
            {
                var candidate = Path.Combine(packageDir, "app", "resources", "codex.exe");
                if (!IsExecutableFile(candidate))
                {
                    continue;
                }

                try
                {
                    matches.Add((candidate, File.GetLastWriteTimeUtc(candidate)));
                }
                catch
                {
                    matches.Add((candidate, DateTime.MinValue));
                }
            }

            var newest = matches
                .OrderByDescending(match => match.LastWriteTimeUtc)
                .Select(match => match.Path)
                .FirstOrDefault();
            if (newest is not null)
            {
                return newest;
            }
        }

        return null;
    }

    private static bool IsExecutableFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }
}
