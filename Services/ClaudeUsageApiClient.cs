using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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

internal sealed class ClaudeUsageApiClient
{
    private static readonly Uri UsageEndpoint = new("https://api.anthropic.com/api/oauth/usage");
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(5);
    private static readonly string UserAgent = $"AgentCompanion/{typeof(ClaudeUsageApiClient).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";

    public async Task<ClaudeUsageApiFetchResult> FetchAsync(string claudeHome, CancellationToken cancellationToken)
    {
        var accessToken = ReadAccessToken(claudeHome);
        if (string.IsNullOrWhiteSpace(accessToken))
            return new ClaudeUsageApiFetchResult(null, DefaultRetryAfter);

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd(UserAgent);

        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var retryAfter = GetRetryAfter(response.Headers.RetryAfter);
            AppLogger.Warning($"Claude usage API returned HTTP {(int)response.StatusCode}; retrying after {retryAfter.TotalMinutes:0} minutes.");
            return new ClaudeUsageApiFetchResult(null, retryAfter);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new ClaudeUsageApiFetchResult(ClaudeUsageApiResponse.Parse(json), DefaultRetryAfter);
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

    private static string? ReadAccessToken(string claudeHome)
    {
        var credentialsPath = Path.Combine(claudeHome, ".credentials.json");
        if (!File.Exists(credentialsPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(credentialsPath, Encoding.UTF8));
            var root = document.RootElement;
            if (!root.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object
                || !oauth.TryGetProperty("accessToken", out var token)
                || token.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return token.GetString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLogger.Warning($"Claude usage credentials could not be read: {ex.GetType().Name}");
            return null;
        }
    }
}
