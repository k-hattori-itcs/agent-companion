using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentCompanion.Services;

internal sealed record ClaudeUsageApiFetchResult(ClaudeUsageApiResponse? Usage, TimeSpan RetryAfter);

public sealed record ClaudeUsageApiResponse(
    double? FiveHourPercent,
    DateTimeOffset? FiveHourResetsAt,
    double? SevenDayPercent,
    DateTimeOffset? SevenDayResetsAt)
{
    public static ClaudeUsageApiResponse Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var fiveHour = ResolveWindow(
            ParseWindow(root, "five_hour"),
            ParseLimit(root, "session", "session"));
        var sevenDay = ResolveWindow(
            ParseWindow(root, "seven_day"),
            ParseLimit(root, "weekly_all", "weekly"));
        return new ClaudeUsageApiResponse(
            fiveHour.Percent,
            fiveHour.ResetsAt,
            sevenDay.Percent,
            sevenDay.ResetsAt);
    }

    private static (double? Percent, DateTimeOffset? ResetsAt) ResolveWindow(
        (double? Percent, DateTimeOffset? ResetsAt) primary,
        (double? Percent, DateTimeOffset? ResetsAt) fallback)
    {
        return (primary.Percent ?? fallback.Percent, primary.ResetsAt ?? fallback.ResetsAt);
    }

    private static (double? Percent, DateTimeOffset? ResetsAt) ParseLimit(
        JsonElement root,
        string expectedKind,
        string expectedGroup)
    {
        if (!root.TryGetProperty("limits", out var limits) || limits.ValueKind != JsonValueKind.Array)
            return (null, null);

        foreach (var limit in limits.EnumerateArray())
        {
            if (limit.ValueKind != JsonValueKind.Object)
                continue;

            var kind = ReadString(limit, "kind");
            var group = ReadString(limit, "group");
            if (!string.Equals(kind, expectedKind, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(group, expectedGroup, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return ParseWindowValues(limit, "percent");
        }

        return (null, null);
    }

    private static string? ReadString(JsonElement value, string propertyName)
    {
        return value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
    private static (double? Percent, DateTimeOffset? ResetsAt) ParseWindow(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
            return (null, null);

        return ParseWindowValues(window, "utilization");
    }

    private static (double? Percent, DateTimeOffset? ResetsAt) ParseWindowValues(
        JsonElement window,
        string percentPropertyName)
    {
        double? percent = null;
        if (window.TryGetProperty(percentPropertyName, out var utilization)
            && utilization.ValueKind == JsonValueKind.Number
            && utilization.TryGetDouble(out var value))
        {
            percent = Math.Clamp(value, 0, 100);
        }

        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resets_at", out var reset)
            && reset.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                reset.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedReset))
        {
            resetsAt = parsedReset;
        }

        return (percent, resetsAt);
    }
}

internal sealed record ClaudeOAuthCredentials(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt)
{
    private static readonly TimeSpan RefreshLeeway = TimeSpan.FromMinutes(2);

    public bool NeedsRefresh(DateTimeOffset now)
    {
        return ExpiresAt is { } expiresAt && expiresAt <= now + RefreshLeeway;
    }
}

internal sealed class ClaudeUsageApiClient
{
    internal static readonly Uri UsageEndpoint = new("https://api.anthropic.com/api/oauth/usage");
    internal static readonly Uri TokenEndpoint = new("https://platform.claude.com/v1/oauth/token");
    internal const string ClaudeCodeOAuthClientId = "22422756-60c9-4084-8eb7-27705fd5cf9a";

    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(5);
    private static readonly string UserAgent = $"AgentCompanion/{typeof(ClaudeUsageApiClient).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";

    private readonly HttpClient _client;
    private readonly Func<DateTimeOffset> _utcNow;

    public ClaudeUsageApiClient()
        : this(SharedClient, () => DateTimeOffset.UtcNow)
    {
    }

    internal ClaudeUsageApiClient(HttpClient client, Func<DateTimeOffset> utcNow)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public async Task<ClaudeUsageApiFetchResult> FetchAsync(string claudeHome, CancellationToken cancellationToken)
    {
        var credentialsPath = Path.Combine(claudeHome, ".credentials.json");
        var credentials = ReadCredentials(credentialsPath);
        if (credentials == null)
            return new ClaudeUsageApiFetchResult(null, DefaultRetryAfter);

        if (credentials.NeedsRefresh(_utcNow()))
        {
            credentials = await RefreshCredentialsAsync(credentialsPath, credentials, cancellationToken).ConfigureAwait(false);
            if (credentials == null)
                return new ClaudeUsageApiFetchResult(null, DefaultRetryAfter);
        }

        using var firstResponse = await SendUsageRequestAsync(credentials.AccessToken, cancellationToken).ConfigureAwait(false);
        if (firstResponse.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return await CreateFetchResultAsync(firstResponse, cancellationToken).ConfigureAwait(false);

        // A 401 may be caused by server-side revocation before expiresAt. Refresh once only.
        credentials = await RefreshCredentialsAsync(credentialsPath, credentials, cancellationToken).ConfigureAwait(false);
        if (credentials == null)
            return new ClaudeUsageApiFetchResult(null, DefaultRetryAfter);

        using var retryResponse = await SendUsageRequestAsync(credentials.AccessToken, cancellationToken).ConfigureAwait(false);
        return await CreateFetchResultAsync(retryResponse, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendUsageRequestAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClaudeUsageApiFetchResult> CreateFetchResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var retryAfter = GetRetryAfter(response.Headers.RetryAfter);
            AppLogger.Warning($"Claude usage API returned HTTP {(int)response.StatusCode}; retrying after {retryAfter.TotalMinutes:0} minutes.");
            return new ClaudeUsageApiFetchResult(null, retryAfter);
        }

        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new ClaudeUsageApiFetchResult(ClaudeUsageApiResponse.Parse(json), DefaultRetryAfter);
        }
        catch (JsonException ex)
        {
            AppLogger.Warning($"Claude usage API response could not be parsed: {ex.GetType().Name}");
            return new ClaudeUsageApiFetchResult(null, DefaultRetryAfter);
        }
    }

    private async Task<ClaudeOAuthCredentials?> RefreshCredentialsAsync(
        string credentialsPath,
        ClaudeOAuthCredentials previous,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(previous.RefreshToken))
        {
            AppLogger.Warning("Claude usage credentials cannot be refreshed because no refresh token is available.");
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    grant_type = "refresh_token",
                    refresh_token = previous.RefreshToken,
                    client_id = ClaudeCodeOAuthClientId
                }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            AppLogger.Warning($"Claude OAuth token refresh returned HTTP {(int)response.StatusCode}.");
            return null;
        }

        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var refreshed = ParseRefreshedCredentials(json, previous, _utcNow());
            return PersistRefreshedCredentials(credentialsPath, previous, refreshed);
        }
        catch (JsonException ex)
        {
            AppLogger.Warning($"Claude OAuth token refresh response could not be parsed: {ex.GetType().Name}");
            return null;
        }
    }

    private static ClaudeOAuthCredentials ParseRefreshedCredentials(
        string json,
        ClaudeOAuthCredentials previous,
        DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!TryReadString(root, "access_token", out var accessToken))
            throw new JsonException("The OAuth refresh response did not contain an access token.");

        var refreshToken = TryReadString(root, "refresh_token", out var updatedRefreshToken)
            ? updatedRefreshToken
            : previous.RefreshToken;
        var expiresAt = ReadRefreshedExpiry(root, now)
            ?? throw new JsonException("The OAuth refresh response did not contain a valid expiry.");
        return new ClaudeOAuthCredentials(accessToken, refreshToken, expiresAt);
    }

    private static DateTimeOffset? ReadRefreshedExpiry(JsonElement root, DateTimeOffset now)
    {
        if (root.TryGetProperty("expires_in", out var expiresIn)
            && expiresIn.ValueKind == JsonValueKind.Number
            && expiresIn.TryGetDouble(out var seconds)
            && double.IsFinite(seconds)
            && seconds is > 0 and <= 31_536_000)
        {
            return now.AddSeconds(seconds);
        }

        if (!root.TryGetProperty("expires_at", out var expiresAt))
            return null;
        if (expiresAt.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(expiresAt.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }
        if (expiresAt.ValueKind == JsonValueKind.Number && expiresAt.TryGetInt64(out var value))
            return FromUnixTimestamp(value);

        return null;
    }

    private static ClaudeOAuthCredentials? PersistRefreshedCredentials(
        string credentialsPath,
        ClaudeOAuthCredentials previous,
        ClaudeOAuthCredentials refreshed)
    {
        try
        {
            var latest = ReadCredentials(credentialsPath);
            if (latest == null)
                return null;

            // Claude Code may refresh this file independently. Never overwrite a newer credential.
            if (!string.Equals(latest.AccessToken, previous.AccessToken, StringComparison.Ordinal))
                return latest;

            var root = JsonNode.Parse(File.ReadAllText(credentialsPath, Encoding.UTF8)) as JsonObject;
            var oauth = root?["claudeAiOauth"] as JsonObject;
            if (root == null || oauth == null)
                return null;

            oauth["accessToken"] = refreshed.AccessToken;
            if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                oauth["refreshToken"] = refreshed.RefreshToken;
            oauth["expiresAt"] = refreshed.ExpiresAt?.ToUnixTimeMilliseconds();
            AtomicFile.WriteAllTextWithoutBackup(credentialsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return refreshed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLogger.Warning($"Claude usage credentials could not be updated: {ex.GetType().Name}");
            return null;
        }
    }

    private static ClaudeOAuthCredentials? ReadCredentials(string credentialsPath)
    {
        if (!File.Exists(credentialsPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(credentialsPath, Encoding.UTF8));
            var root = document.RootElement;
            if (!root.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object
                || !TryReadString(oauth, "accessToken", out var accessToken))
            {
                return null;
            }

            var refreshToken = TryReadString(oauth, "refreshToken", out var parsedRefreshToken)
                ? parsedRefreshToken
                : null;
            var expiresAt = oauth.TryGetProperty("expiresAt", out var expiresAtValue)
                ? ReadCredentialExpiry(expiresAtValue)
                : null;
            return new ClaudeOAuthCredentials(accessToken, refreshToken, expiresAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLogger.Warning($"Claude usage credentials could not be read: {ex.GetType().Name}");
            return null;
        }
    }

    private static DateTimeOffset? ReadCredentialExpiry(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return FromUnixTimestamp(number);
        if (value.ValueKind == JsonValueKind.String)
        {
            if (long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericString))
                return FromUnixTimestamp(numericString);
            if (DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed;
        }

        return null;
    }

    private static DateTimeOffset? FromUnixTimestamp(long value)
    {
        try
        {
            return Math.Abs(value) >= 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = "";
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static TimeSpan GetRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delay && delay > TimeSpan.Zero)
            return delay;
        if (retryAfter?.Date is { } retryAt)
        {
            var remaining = retryAt - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
                return remaining;
        }

        return DefaultRetryAfter;
    }
}
