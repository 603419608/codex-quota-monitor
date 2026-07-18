using System.Text;
using System.Text.Json.Nodes;

namespace CodexUsageOverlay.Core;

public sealed class OAuthProfileReader
{
    private readonly string _authPath;

    public OAuthProfileReader(string? authPath = null)
    {
        _authPath = authPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "auth.json");
    }

    public async Task<string?> ReadDisplayNameAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_authPath))
            {
                return null;
            }

            var text = await File.ReadAllTextAsync(_authPath, cancellationToken);
            var node = JsonNode.Parse(text);
            var idToken =
                node?["tokens"]?["id_token"]?.GetValue<string>() ??
                node?["id_token"]?.GetValue<string>() ??
                node?["idToken"]?.GetValue<string>();

            return ParseDisplayNameFromIdToken(idToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static string? ParseDisplayNameFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var segments = idToken.Split('.');
        if (segments.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = DecodeBase64Url(segments[1]);
            var claims = JsonNode.Parse(Encoding.UTF8.GetString(payload));
            var displayName =
                claims?["name"]?.GetValue<string>() ??
                claims?["display_name"]?.GetValue<string>() ??
                claims?["displayName"]?.GetValue<string>();

            return string.IsNullOrWhiteSpace(displayName)
                ? null
                : displayName.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => throw new FormatException("Invalid Base64Url payload.")
        };

        return Convert.FromBase64String(normalized);
    }
}
