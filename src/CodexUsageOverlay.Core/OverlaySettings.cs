using System.Text.Json;

namespace CodexUsageOverlay.Core;

public sealed class OverlaySettings
{
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool IsCollapsed { get; set; }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodexUsageOverlay",
            "settings.json");
    }

    public OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new OverlaySettings();
            }

            var text = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<OverlaySettings>(text, JsonOptions) ?? new OverlaySettings();
        }
        catch
        {
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _path + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));

                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(tempPath, _path, null);
                    }
                    catch
                    {
                        File.Move(tempPath, _path, overwrite: true);
                    }
                }
                else
                {
                    File.Move(tempPath, _path, overwrite: true);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
            // Saving overlay position/collapse state is non-critical. Disk, lock, or permission
            // failures should not crash the UI; the next successful save will catch up.
        }
    }
}
