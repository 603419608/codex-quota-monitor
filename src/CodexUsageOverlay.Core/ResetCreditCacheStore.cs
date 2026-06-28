using System.Text.Json;

namespace CodexUsageOverlay.Core;

public sealed class ResetCreditCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    public ResetCreditCacheStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodexUsageOverlay",
            "reset-credits-cache.json");
    }

    public ResetCreditSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var text = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<ResetCreditSnapshot>(text, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(ResetCreditSnapshot snapshot)
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
                File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, JsonOptions));

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
            // Reset-credit cache is optional. Network data will be retried on the next refresh window.
        }
    }

    public static bool IsFreshForToday(ResetCreditSnapshot snapshot, DateTimeOffset now)
    {
        return snapshot.IsAvailable &&
            snapshot.FetchedAt.ToLocalTime().Date >= now.ToLocalTime().Date;
    }
}
