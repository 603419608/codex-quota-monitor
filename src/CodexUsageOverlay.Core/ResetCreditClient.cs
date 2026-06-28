using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace CodexUsageOverlay.Core;

public sealed class ResetCreditClient
{
    private static readonly Uri Endpoint = new("https://chatgpt.com/backend-api/wham/rate-limit-reset-credits");
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly string _authPath;

    public ResetCreditClient(string? authPath = null)
    {
        _authPath = authPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "auth.json");
    }

    public async Task<ResetCreditSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var token = await ReadAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var node = JsonNode.Parse(text);
        var snapshot = UsageParsers.ParseResetCredits(node, DateTimeOffset.UtcNow);
        if (!snapshot.IsAvailable)
        {
            throw new InvalidOperationException("Reset-credit response did not contain usable data.");
        }

        return snapshot;
    }

    private async Task<string> ReadAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_authPath))
        {
            throw new FileNotFoundException("Codex auth file was not found.", _authPath);
        }

        var text = await File.ReadAllTextAsync(_authPath, cancellationToken);
        var node = JsonNode.Parse(text);
        var token =
            node?["tokens"]?["access_token"]?.GetValue<string>() ??
            node?["access_token"]?.GetValue<string>() ??
            node?["accessToken"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Codex access token was not found.");
        }

        return token;
    }
}
