using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NauAssist.Backend.Features.AutonomousAgent.Push;
using NauAssist.Backend.Features.Chat;
using NauAssist.Backend.Features.Events;
using NauAssist.Backend.Features.Infrastructure.Auth;
using NauAssist.Backend.Features.WatchJobs;
using NauAssist.Backend.Features.WatchJobs.Notify;
using NauAssist.Backend.Tests.Helpers;

namespace NauAssist.Backend.Tests.Features.WatchJobs;

public sealed class WatchJobNotifierTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-25T10:00:00Z");

    [Fact]
    public async Task NotifyAsync_PersistsProactiveAssistantMessageWithEvidence()
    {
        using var temp = new TempSqliteDb();
        var holder = new UserContextHolder();
        var messages = new MessageRepository(temp.AppDb, holder);
        var notifier = BuildNotifier(temp, holder, messages);

        var job = SampleJob(channels: new[] { "webpush" });
        var result = new WatchJudgeResult(
            Met: true,
            Confidence: 0.9,
            Evidence: new[] { new JudgeEvidence("ShopX", "599€", "https://shop.example/x", "auf Lager") },
            Summary: "Bei ShopX lieferbar");

        await notifier.NotifyAsync(job, result, CancellationToken.None);

        var recent = await messages.GetRecentAsync("default", 10, CancellationToken.None);
        recent.Should().HaveCount(1);
        recent[0].Role.Should().Be(MessageRole.Assistant);
        recent[0].Content.Should().Contain(job.Title)
            .And.Contain("Bei ShopX lieferbar")
            .And.Contain("ShopX")
            .And.Contain("599€")
            .And.Contain("https://shop.example/x");
    }

    [Fact]
    public async Task NotifyAsync_PublishesProactiveEvents()
    {
        using var temp = new TempSqliteDb();
        var holder = new UserContextHolder();
        var messages = new MessageRepository(temp.AppDb, holder);
        var broker = new ProactiveEventBroker();
        using var sub = broker.Subscribe(holder.UserId);
        var notifier = BuildNotifier(temp, holder, messages, broker);

        await notifier.NotifyAsync(
            SampleJob(channels: new[] { "webpush" }),
            new WatchJudgeResult(true, 0.9, Array.Empty<JudgeEvidence>(), "Treffer"),
            CancellationToken.None);

        var first = await sub.Reader.ReadAsync(CancellationToken.None);
        first.EventName.Should().Be("chat_message");
        var second = await sub.Reader.ReadAsync(CancellationToken.None);
        second.EventName.Should().Be("watch_job_fired");
        second.DataJson.Should().Contain("\"jobId\":7");
    }

    [Fact]
    public async Task NotifyAsync_WithUnknownChannel_StillPersistsMessage()
    {
        using var temp = new TempSqliteDb();
        var holder = new UserContextHolder();
        var messages = new MessageRepository(temp.AppDb, holder);
        var notifier = BuildNotifier(temp, holder, messages);

        var job = SampleJob(channels: new[] { "pushover" }); // Phase-2-Kanal ⇒ ignoriert, kein Crash
        var result = new WatchJudgeResult(true, 0.8, Array.Empty<JudgeEvidence>(), "Treffer");

        await notifier.NotifyAsync(job, result, CancellationToken.None);

        (await messages.GetRecentAsync("default", 10, CancellationToken.None)).Should().HaveCount(1);
    }

    [Fact]
    public async Task NotifyAsync_FailingChannelDoesNotPreventOtherChannels()
    {
        using var temp = new TempSqliteDb();
        var holder = new UserContextHolder();
        var messages = new MessageRepository(temp.AppDb, holder);
        var ok = new RecordingChannel("ok");
        var boom = new ThrowingChannel("boom");
        var notifier = new WatchJobNotifier(
            new INotificationChannel[] { boom, ok },
            messages, new ProactiveEventBroker(), holder, () => Now, NullLogger<WatchJobNotifier>.Instance);

        var job = SampleJob(channels: new[] { "boom", "ok" });
        await notifier.NotifyAsync(job, new WatchJudgeResult(true, 0.9, Array.Empty<JudgeEvidence>(), "Treffer"), CancellationToken.None);

        ok.Sent.Should().HaveCount(1);
        (await messages.GetRecentAsync("default", 10, CancellationToken.None)).Should().HaveCount(1);
    }

    private sealed class RecordingChannel : INotificationChannel
    {
        public RecordingChannel(string name) => Name = name;
        public string Name { get; }
        public List<WatchNotification> Sent { get; } = new();
        public Task<bool> SendAsync(WatchNotification notification, CancellationToken ct)
        {
            Sent.Add(notification);
            return Task.FromResult(true);
        }
    }

    private sealed class ThrowingChannel : INotificationChannel
    {
        public ThrowingChannel(string name) => Name = name;
        public string Name { get; }
        public Task<bool> SendAsync(WatchNotification notification, CancellationToken ct)
            => throw new InvalidOperationException("Kanal kaputt");
    }

    private static WatchJobNotifier BuildNotifier(
        TempSqliteDb temp, UserContextHolder holder, MessageRepository messages, ProactiveEventBroker? broker = null)
    {
        var push = new WebPushSender(
            new PushSubscriptionRepository(temp.AppDb, holder),
            new FakeSettingsRepo(),
            () => Now,
            NullLogger<WebPushSender>.Instance);
        return new WatchJobNotifier(
            new INotificationChannel[] { new WebPushChannel(push) },
            messages,
            broker ?? new ProactiveEventBroker(),
            holder,
            () => Now,
            NullLogger<WatchJobNotifier>.Instance);
    }

    private static WatchJob SampleJob(IReadOnlyList<string> channels) => new(
        Id: 7,
        Title: "Midea PortaSplit Verfügbarkeit",
        Goal: "wieder bestellbar",
        Kind: WatchJobKind.WebAvailability,
        Spec: new WatchJobSpec(new[] { "q" }, Array.Empty<string>(), "frage?", "kriterium"),
        Schedule: new WatchJobSchedule(60, 1800),
        Notify: new WatchJobNotify(channels, FireOnce: true),
        Budget: new WatchJobBudget(null, null),
        Status: WatchJobStatus.Active,
        LastCheckedAt: null,
        NextDueAt: Now,
        CheckCount: 0,
        ConsecutiveErrors: 0,
        LastResultJson: null,
        FiredHash: null,
        CreatedAt: Now);
}
