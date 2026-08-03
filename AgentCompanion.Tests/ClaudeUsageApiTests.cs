using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentCompanion.Services;
using Xunit;

namespace AgentCompanion.Tests;

public sealed class ClaudeUsageApiTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "AgentCompanion.ClaudeUsageApi.Tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public void Parse_ReadsFiveHourAndSevenDayPercentages()
    {
        const string json = """
            {
              "five_hour": {
                "utilization": 73.0,
                "resets_at": "2026-07-24T03:40:00.426978+00:00"
              },
              "seven_day": {
                "utilization": 57.0,
                "resets_at": "2026-07-27T15:00:00.426997+00:00"
              }
            }
            """;

        var result = ClaudeUsageApiResponse.Parse(json);

        Assert.Equal(73, result.FiveHourPercent);
        Assert.Equal(57, result.SevenDayPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-07-24T03:40:00.426978+00:00", CultureInfo.InvariantCulture), result.FiveHourResetsAt);
        Assert.Equal(DateTimeOffset.Parse("2026-07-27T15:00:00.426997+00:00", CultureInfo.InvariantCulture), result.SevenDayResetsAt);
    }

    [Fact]
    public void Parse_UsesLimitsArrayWhenLegacyWindowsAreMissing()
    {
        const string json = """
            {
              "limits": [
                {
                  "kind": "session",
                  "group": "session",
                  "percent": 0.0,
                  "resets_at": null,
                  "is_active": false
                },
                {
                  "kind": "weekly_all",
                  "group": "weekly",
                  "percent": 72.0,
                  "resets_at": "2026-07-27T15:00:00.201561+00:00",
                  "is_active": true
                }
              ]
            }
            """;

        var result = ClaudeUsageApiResponse.Parse(json);

        Assert.Equal(0, result.FiveHourPercent);
        Assert.Null(result.FiveHourResetsAt);
        Assert.Equal(72, result.SevenDayPercent);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-27T15:00:00.201561+00:00", CultureInfo.InvariantCulture),
            result.SevenDayResetsAt);
    }

    [Fact]
    public void Parse_ClampsPercentagesAndAllowsMissingWindows()
    {
        const string json = """
            {
              "five_hour": { "utilization": 120.0, "resets_at": null },
              "seven_day": null
            }
            """;

        var result = ClaudeUsageApiResponse.Parse(json);

        Assert.Equal(100, result.FiveHourPercent);
        Assert.Null(result.SevenDayPercent);
        Assert.Null(result.SevenDayResetsAt);
    }

    [Fact]
    public async Task FetchAsync_RefreshesExpiredCredentialBeforeRequestingUsage()
    {
        var now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        WriteCredentials("expired-token", "refresh-token", now.AddMinutes(-1));
        using var handler = new ScriptedHandler(
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/v1/oauth/token", request.RequestUri!.AbsolutePath);
                return JsonResponse("""{"access_token":"updated-token","refresh_token":"updated-refresh","expires_in":3600}""");
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("updated-token", request.Headers.Authorization?.Parameter);
                return JsonResponse("""{"five_hour":{"utilization":23},"seven_day":{"utilization":67}}""");
            });
        using var client = new HttpClient(handler);
        var api = new ClaudeUsageApiClient(client, () => now);

        var result = await api.FetchAsync(_root, CancellationToken.None);

        Assert.Equal(23, result.Usage?.FiveHourPercent);
        Assert.Equal(67, result.Usage?.SevenDayPercent);
        Assert.Equal(2, handler.RequestCount);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, ".credentials.json")));
        var oauth = document.RootElement.GetProperty("claudeAiOauth");
        Assert.Equal("updated-token", oauth.GetProperty("accessToken").GetString());
        Assert.Equal("updated-refresh", oauth.GetProperty("refreshToken").GetString());
        Assert.True(oauth.GetProperty("expiresAt").GetInt64() > now.ToUnixTimeMilliseconds());
        Assert.False(File.Exists(Path.Combine(_root, ".credentials.json.bak")));
    }

    [Fact]
    public async Task FetchAsync_OnUnauthorized_RefreshesAndRetriesUsageOnce()
    {
        var now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        WriteCredentials("current-token", "refresh-token", now.AddHours(1));
        using var handler = new ScriptedHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => JsonResponse("""{"access_token":"updated-token","expires_in":3600}"""),
            request =>
            {
                Assert.Equal("updated-token", request.Headers.Authorization?.Parameter);
                return JsonResponse("""{"five_hour":{"utilization":11},"seven_day":{"utilization":22}}""");
            });
        using var client = new HttpClient(handler);
        var api = new ClaudeUsageApiClient(client, () => now);

        var result = await api.FetchAsync(_root, CancellationToken.None);

        Assert.Equal(11, result.Usage?.FiveHourPercent);
        Assert.Equal(22, result.Usage?.SevenDayPercent);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task FetchAsync_OnUnauthorizedWithFailedRefresh_DoesNotRetryUsage()
    {
        var now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        WriteCredentials("current-token", "refresh-token", now.AddHours(1));
        using var handler = new ScriptedHandler(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var client = new HttpClient(handler);
        var api = new ClaudeUsageApiClient(client, () => now);

        var result = await api.FetchAsync(_root, CancellationToken.None);

        Assert.Null(result.Usage);
        Assert.Equal(2, handler.RequestCount);
    }
    [Fact]
    public async Task FetchAsync_OnRateLimit_ReturnsServerRetryAfter()
    {
        var now = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        WriteCredentials("current-token", "refresh-token", now.AddHours(1));
        using var handler = new ScriptedHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(17));
            return response;
        });
        using var client = new HttpClient(handler);
        var api = new ClaudeUsageApiClient(client, () => now);

        var result = await api.FetchAsync(_root, CancellationToken.None);

        Assert.Null(result.Usage);
        Assert.Equal(TimeSpan.FromMinutes(17), result.RetryAfter);
    }

    private void WriteCredentials(string accessToken, string refreshToken, DateTimeOffset expiresAt)
    {
        Directory.CreateDirectory(_root);
        var json = JsonSerializer.Serialize(new
        {
            claudeAiOauth = new
            {
                accessToken,
                refreshToken,
                expiresAt = expiresAt.ToUnixTimeMilliseconds()
            }
        });
        File.WriteAllText(Path.Combine(_root, ".credentials.json"), json, Encoding.UTF8);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new(responses);

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.NotEmpty(_responses);
            return Task.FromResult(_responses.Dequeue()(request));
        }
    }
}
