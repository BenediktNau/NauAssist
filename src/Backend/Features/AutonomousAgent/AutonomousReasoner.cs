using Mediator;
using Microsoft.Extensions.Logging;
using NauAssist.Backend.Features.AutonomousAgent.Classification;
using NauAssist.Backend.Features.AutonomousAgent.Push;
using NauAssist.Backend.Features.Calendar.LookupFreeSlots;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Features.Rules;
using NauAssist.Backend.Features.Settings;

namespace NauAssist.Backend.Features.AutonomousAgent;

/// <summary>
/// Verarbeitet pro Tick die Rohsignale: Cheap-Filter → Classifier → Slot-Suche → Suggestion.
/// Thread-Awareness: existiert für die gleiche Quelle+SourceRef eine offene Suggestion
/// (< 24 h), wird sie aktualisiert statt eine neue angelegt.
/// </summary>
public sealed class AutonomousReasoner
{
    private const int DefaultDurationMinutes = 60;
    private const double ConfidenceThreshold = 0.6;
    private static readonly TimeSpan ThreadWindow = TimeSpan.FromHours(24);
    private const int MaxSlotsPerSuggestion = 3;

    private readonly IntentClassifier _classifier;
    private readonly IMediator _mediator;
    private readonly SuggestionRepository _suggestions;
    private readonly IAppSettingsRepository _settings;
    private readonly WebPushSender _pushSender;
    private readonly ClockContext _clockContext;
    private readonly TimeZoneInfo _zone;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<AutonomousReasoner> _logger;

    public AutonomousReasoner(
        IntentClassifier classifier,
        IMediator mediator,
        SuggestionRepository suggestions,
        IAppSettingsRepository settings,
        WebPushSender pushSender,
        ClockContext clockContext,
        TimeZoneInfo zone,
        Func<DateTimeOffset> clock,
        ILogger<AutonomousReasoner> logger)
    {
        _classifier = classifier;
        _mediator = mediator;
        _suggestions = suggestions;
        _settings = settings;
        _pushSender = pushSender;
        _clockContext = clockContext;
        _zone = zone;
        _clock = clock;
        _logger = logger;
    }

    public sealed record Outcome(int Classified, int Created, int Updated, int PersonaUpdates);

    public async Task<Outcome> ProcessAsync(IReadOnlyList<RawSignal> signals, CancellationToken ct)
    {
        var classified = 0;
        var created = 0;
        var updated = 0;
        var personaUpdates = 0;

        if (signals.Count == 0) return new Outcome(0, 0, 0, 0);

        var persona = await _settings.GetUserPersonaAsync(ct);

        foreach (var signal in signals)
        {
            if (!CheapPreFilter.LooksLikeSchedulingIntent(signal.Text))
            {
                continue;
            }

            ClassificationResult? result;
            try
            {
                result = await _classifier.ClassifyAsync(signal, persona, ct);
                classified++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Classifier-Fehler für Signal aus {Source}/{Ref}.", signal.Source, signal.SourceRef);
                continue;
            }

            if (result is null) continue;

            // Persona-Update darf auch bei intent=none stattfinden (Stil-/Interessen-Lernen).
            if (!string.IsNullOrWhiteSpace(result.PersonaUpdate))
            {
                try
                {
                    await _settings.SetUserPersonaAsync(result.PersonaUpdate, ct);
                    persona = await _settings.GetUserPersonaAsync(ct);
                    personaUpdates++;
                    _logger.LogInformation("Persona-Memory aktualisiert ({Length} Zeichen).", persona.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Persona-Update konnte nicht gespeichert werden.");
                }
            }

            if (result.Intent != "schedule_request" || result.Confidence < ConfidenceThreshold)
            {
                continue;
            }

            try
            {
                var didCreate = await CreateOrUpdateSuggestionAsync(signal, result, ct);
                if (didCreate) created++; else updated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Suggestion-Erstellung fehlgeschlagen für {Source}/{Ref}.", signal.Source, signal.SourceRef);
            }
        }

        return new Outcome(classified, created, updated, personaUpdates);
    }

    /// <summary>Liefert true wenn eine neue Suggestion angelegt wurde, false wenn eine bestehende aktualisiert wurde.</summary>
    private async Task<bool> CreateOrUpdateSuggestionAsync(
        RawSignal signal,
        ClassificationResult cls,
        CancellationToken ct)
    {
        var snap = _clockContext.Build();
        var (from, to) = MapDateHint(cls.DateHint, snap);

        // Slot-Suche darf nicht in der Vergangenheit starten.
        var now = _clock();
        if (from < now) from = now;
        if (to <= from)
        {
            _logger.LogDebug("Date-Hint '{Hint}' ergibt leeren Zeitraum — übersprungen.", cls.DateHint);
            return false;
        }

        var duration = cls.DurationMinutes is > 0 ? cls.DurationMinutes.Value : DefaultDurationMinutes;
        var response = await _mediator.Send(new LookupFreeSlotsRequest(from, to, duration), ct);
        var slots = PickSpreadSlots(response.Annotations, MaxSlotsPerSuggestion);
        if (slots.Count == 0)
        {
            _logger.LogInformation(
                "Keine freien Slots im Bereich {From:o}…{To:o} ({Duration} min) — keine Suggestion erstellt.",
                from, to, duration);
            return false;
        }

        var draft = cls.DraftReply ?? "Hey, [SLOT] passt mir gut.";
        var now2 = _clock();

        var existing = await _suggestions.FindOpenByThreadAsync(
            signal.Source, signal.SourceRef, now2 - ThreadWindow, ct);

        if (existing is not null)
        {
            // Thread-Update: ersetze Slots + Draft, aktualisiere updated_at.
            await _suggestions.UpdateContentAsync(
                existing.Id,
                topic: cls.Topic ?? existing.Topic,
                requester: cls.Requester ?? existing.Requester,
                quotedText: TruncateQuote(signal.Text),
                slots: slots,
                draftReply: draft,
                now: now2,
                ct: ct);
            _logger.LogInformation("Suggestion {Id} im Thread {Source}/{Ref} aktualisiert.",
                existing.Id, signal.Source, signal.SourceRef);
            return false;
        }

        var created = await _suggestions.InsertAsync(
            source: signal.Source,
            sourceRef: signal.SourceRef,
            intent: cls.Intent,
            topic: cls.Topic,
            requester: cls.Requester ?? signal.Sender,
            quotedText: TruncateQuote(signal.Text),
            slots: slots,
            draftReply: draft,
            now: now2,
            ct: ct);
        _logger.LogInformation("Neue Suggestion {Id} aus {Source}/{Ref}: \"{Topic}\".",
            created.Id, signal.Source, signal.SourceRef, cls.Topic);

        // Push nur bei *neuer* Suggestion — Thread-Updates pingen nicht erneut.
        await TryPushAsync(created, ct);
        return true;
    }

    private async Task TryPushAsync(Suggestion s, CancellationToken ct)
    {
        try
        {
            var title = string.IsNullOrEmpty(s.Requester)
                ? "Neue Termin-Anfrage"
                : $"Termin-Anfrage von {s.Requester}";
            var body = s.Topic ?? TruncateQuote(s.QuotedText ?? "");
            await _pushSender.BroadcastAsync(
                new PushNotificationPayload(
                    Title: title,
                    Body: body,
                    Url: $"/?suggestion={s.Id}",
                    Tag: $"suggestion-{s.Id}"),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push-Broadcast für Suggestion {Id} fehlgeschlagen.", s.Id);
        }
    }

    /// <summary>Wählt bis zu N Slots, jeweils einen pro Tag, zuerst die "Passes", dann "SoftViolation" falls nicht genug.</summary>
    private static IReadOnlyList<SuggestionSlot> PickSpreadSlots(IReadOnlyList<SlotAnnotation> annotations, int max)
    {
        var byDay = new Dictionary<DateOnly, SlotAnnotation>();
        // Erst Passes — wenn ein Tag noch leer ist, eintragen.
        foreach (var a in annotations.Where(a => a.Status == AnnotationStatus.Passes))
        {
            var day = DateOnly.FromDateTime(a.Slot.Start.LocalDateTime);
            byDay.TryAdd(day, a);
        }
        // Falls noch Plätze frei — Soft-Violations als Fallback.
        if (byDay.Count < max)
        {
            foreach (var a in annotations.Where(a => a.Status == AnnotationStatus.SoftViolation))
            {
                var day = DateOnly.FromDateTime(a.Slot.Start.LocalDateTime);
                if (byDay.ContainsKey(day)) continue;
                byDay[day] = a;
                if (byDay.Count >= max) break;
            }
        }

        return byDay
            .OrderBy(kv => kv.Key)
            .Take(max)
            .Select(kv => new SuggestionSlot(
                Start: kv.Value.Slot.Start,
                End: kv.Value.Slot.End,
                Note: kv.Value.Status == AnnotationStatus.SoftViolation && kv.Value.ViolatedBy is not null
                    ? $"Achtung: {kv.Value.ViolatedBy.Text}"
                    : null))
            .ToList();
    }

    private (DateTimeOffset From, DateTimeOffset To) MapDateHint(string? hint, TimeSnapshot snap)
    {
        DateTimeOffset Day(DateOnly d)
        {
            var dt = d.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(dt, _zone.GetUtcOffset(dt));
        }

        var defaultFrom = Day(snap.Today);
        var defaultTo = defaultFrom.AddDays(7);

        if (string.IsNullOrWhiteSpace(hint)) return (defaultFrom, defaultTo);

        switch (hint.ToLowerInvariant())
        {
            case "today":
                return (Day(snap.Today), Day(snap.Today).AddDays(1));
            case "tomorrow":
                return (Day(snap.Tomorrow), Day(snap.Tomorrow).AddDays(1));
            case "this_week":
                return (Day(snap.ThisWeek.Start), Day(snap.ThisWeek.End).AddDays(1));
            case "next_week":
                return (Day(snap.NextWeek.Start), Day(snap.NextWeek.End).AddDays(1));
            case "this_weekend":
                return (Day(snap.ThisWeekend.Start), Day(snap.ThisWeekend.End).AddDays(1));
            case "next_weekend":
                return (Day(snap.NextWeekend.Start), Day(snap.NextWeekend.End).AddDays(1));
        }

        var rangeParts = hint.Split('/');
        if (rangeParts.Length == 2
            && DateOnly.TryParse(rangeParts[0], out var rangeStart)
            && DateOnly.TryParse(rangeParts[1], out var rangeEnd)
            && rangeEnd >= rangeStart)
        {
            return (Day(rangeStart), Day(rangeEnd).AddDays(1));
        }

        if (DateOnly.TryParse(hint, out var single))
        {
            return (Day(single), Day(single).AddDays(1));
        }

        return (defaultFrom, defaultTo);
    }

    private static string TruncateQuote(string text)
    {
        const int Max = 280;
        return text.Length <= Max ? text : text[..Max] + "…";
    }
}
