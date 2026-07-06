using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NauAssist.Backend.Features.Settings;
using NauAssist.Backend.Features.WatchJobs.Notify;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.WatchJobs;

public sealed class PushoverChannelTests
{
    [Fact]
    public async Task SendAsync_PostsFormFieldsToPushoverApi()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"status":1}""");
        var channel = BuildChannel(handler, new PushoverSettings("tok", "usr"));

        var ok = await channel.SendAsync(
            new WatchNotification("Midea verfügbar", "Bei ShopX lieferbar", "/chat", "watch-1"),
            CancellationToken.None);

        ok.Should().BeTrue();
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.pushover.net/1/messages.json");
        handler.LastBody.Should().Contain("token=tok").And.Contain("user=usr");
        handler.LastBody.Should().Contain("title=Midea+verf").And.Contain("message=Bei+ShopX");
        // Relative URLs (PWA-interne Deep-Links) werden nicht mitgesendet.
        handler.LastBody.Should().NotContain("url=");
    }

    [Fact]
    public async Task SendAsync_NotConfigured_SkipsWithoutRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"status":1}""");
        var channel = BuildChannel(handler, new PushoverSettings("", ""));

        var ok = await channel.SendAsync(new WatchNotification("T", "B", null, null), CancellationToken.None);

        ok.Should().BeFalse();
        handler.LastRequest.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_HttpError_ReturnsFalse()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"status":0,"errors":["user key invalid"]}""");
        var channel = BuildChannel(handler, new PushoverSettings("tok", "usr"));

        (await channel.SendAsync(new WatchNotification("T", "B", null, null), CancellationToken.None))
            .Should().BeFalse();
    }

    private static PushoverChannel BuildChannel(RecordingHandler handler, PushoverSettings settings)
        => new(
            new SingleClientFactory(new HttpClient(handler)),
            new FakeSettingsRepo { Pushover = settings },
            NullLogger<PushoverChannel>.Instance);

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public RecordingHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }
}
