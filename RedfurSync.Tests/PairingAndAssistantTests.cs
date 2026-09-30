using System.Net;
using System.Text.Json;
using RedfurSync;
using Xunit;

namespace RedfurSync.Tests;

public sealed class PairingAndAssistantTests
{
    [Fact]
    public async Task PairAsync_WithEmptyPairingCodeAndApiKey_ReturnsApiKeyModeActive()
    {
        var config = new AppConfig
        {
            ServerUrl = "https://relay.invalid/upload",
            ApiKey = "my-secret-key",
            PairingCode = string.Empty,
        };

        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var uploader = new UploadService(config, handler, FakeHttpMessageHandler.Returning(HttpStatusCode.OK));

        var (ok, message) = await uploader.PairAsync();

        Assert.True(ok);
        Assert.Equal("API key mode active", message);
        Assert.Empty(handler.Requests); // Did not make a dummy HTTP request
        Assert.Empty(config.DeviceToken);
    }

    [Fact]
    public async Task PairAsync_WithPairingCode_ExchangesForDeviceToken()
    {
        var config = new AppConfig
        {
            ServerUrl = "https://relay.invalid/upload",
            PairingCode = "744424",
            ApiKey = "fixture-key",
        };

        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://relay.invalid/api/relay/v1/pair", req.RequestUri?.ToString());
            var json = JsonSerializer.Serialize(new { token = "rfr_mock_token_abc123" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        });

        using var uploader = new UploadService(config, handler, FakeHttpMessageHandler.Returning(HttpStatusCode.OK));

        var (ok, message) = await uploader.PairAsync();

        Assert.True(ok);
        Assert.Contains("paired successfully", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("rfr_mock_token_abc123", config.DeviceToken);
        Assert.Empty(config.PairingCode);
    }

    [Fact]
    public async Task AskFissalAsync_WithApiKeyAndNoDeviceToken_SendsApiKeyHeaderAndReturnsModel()
    {
        var config = new AppConfig
        {
            ServerUrl = "https://relay.invalid/upload",
            ApiKey = "master-key-xyz",
            DeviceToken = string.Empty,
        };

        var handler = new FakeHttpMessageHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://relay.invalid/api/relay/v1/assistant", req.RequestUri?.ToString());
            Assert.True(req.Headers.Contains("X-Api-Key"));
            Assert.Equal("master-key-xyz", req.Headers.GetValues("X-Api-Key").First());

            var json = JsonSerializer.Serialize(new { text = "Purrs warmly, all gears mesh!", model = "gemini-3.8-flash" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        });

        using var uploader = new UploadService(config, handler, FakeHttpMessageHandler.Returning(HttpStatusCode.OK));

        var (ok, reply, model) = await uploader.AskFissalAsync("Status report?");

        Assert.True(ok);
        Assert.Equal("Purrs warmly, all gears mesh!", reply);
        Assert.Equal("gemini-3.8-flash", model);
    }

    [Fact]
    public async Task AskFissalAsync_WithNeitherKeyNorToken_FailsPreflightWithoutNetwork()
    {
        var config = new AppConfig
        {
            ServerUrl = "https://relay.invalid/upload",
            ApiKey = string.Empty,
            DeviceToken = string.Empty,
        };

        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var uploader = new UploadService(config, handler, FakeHttpMessageHandler.Returning(HttpStatusCode.OK));

        var (ok, reply, _) = await uploader.AskFissalAsync("Hello?");

        Assert.False(ok);
        Assert.Contains("Pair Fissal Relay", reply);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void AppConfigSave_WhenStandaloneInstanceWithoutStoragePath_DoesNotTouchProductionConfig()
    {
        var standalone = new AppConfig
        {
            ServerUrl = "https://isolated.test/upload",
            ApiKey = "isolated-key",
        };
        standalone.Save();
        Assert.NotEqual("isolated-key", AppConfig.Instance.ApiKey);
    }
}
