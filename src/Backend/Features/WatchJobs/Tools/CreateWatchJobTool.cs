using System.Text.Json;
using Microsoft.Extensions.Options;
using NauAssist.Backend.Features.Agent;
using NauAssist.Backend.Features.Infrastructure.Time;
using NauAssist.Backend.Features.WatchJobs.Web;

namespace NauAssist.Backend.Features.WatchJobs.Tools;

/// <summary>
/// Lässt den Chat-Agenten aus dem Gespräch einen persistenten Hintergrund-Watcher anlegen
/// (das „selbstgeschriebene Tool"). Erzwingt die Kadenz-Untergrenze und die Obergrenze
/// aktiver Jobs pro User. Vorbild: <c>LookupFreeSlotsTool</c>.
/// </summary>
public sealed class CreateWatchJobTool : ITool
{
    public string Name => "create_watch_job";

    public string Description =>
        "Legt einen dauerhaft laufenden Hintergrund-Beobachter (Watch-Job) an, der regelmäßig im Web prüft, " +
        "ob ein Ziel erreicht ist (z.B. ein Produkt wieder verfügbar/bestellbar), und bei einem Treffer per Push meldet. " +
        "Der Job läuft entkoppelt vom Chat — nach dem Anlegen kurz bestätigen; der User kann normal weiterreden.";

    public JsonElement ParameterSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "Kurzer, sprechender Titel des Beobachters" },
            "goal": { "type": "string", "description": "Was beobachtet wird, in einem Satz" },
            "kind": { "type": "string", "enum": ["web_availability"], "description": "Art des Watchers (aktuell nur web_availability)" },
            "spec": {
              "type": "object",
              "properties": {
                "searchQueries": { "type": "array", "items": { "type": "string" }, "description": "Web-Suchanfragen, mehrquellig" },
                "targetUrls": { "type": "array", "items": { "type": "string" }, "description": "Optionale konkrete Produkt-/Shop-URLs" },
                "judgeQuestion": { "type": "string", "description": "Präzise Ja/Nein-Frage, die der Prüf-Schritt beantwortet" },
                "successCriteria": { "type": "string", "description": "Wann gilt das Ziel als erfüllt" }
              },
              "required": ["judgeQuestion", "successCriteria"]
            },
            "schedule": {
              "type": "object",
              "properties": {
                "intervalSeconds": { "type": "integer", "description": "Basis-Prüfintervall in Sekunden (wird auf das erlaubte Minimum angehoben)" },
                "maxIntervalSeconds": { "type": "integer", "description": "Obergrenze fürs Backoff in Sekunden" }
              }
            },
            "notify": {
              "type": "object",
              "properties": {
                "channels": { "type": "array", "items": { "type": "string" }, "description": "Benachrichtigungskanäle: webpush, pushover" },
                "fireOnce": { "type": "boolean", "description": "Nach dem ersten Treffer beenden (true) oder weiterbeobachten (false)" }
              }
            },
            "budget": {
              "type": "object",
              "properties": {
                "maxChecks": { "type": "integer", "description": "Optional: maximale Anzahl Prüfungen" },
                "expiresAt": { "type": "string", "format": "date-time", "description": "Optional: ISO-8601-Ablaufzeitpunkt" }
              }
            }
          },
          "required": ["title", "goal", "spec"]
        }
        """).RootElement;

    private readonly WatchJobRepository _repo;
    private readonly ClockContext _clock;
    private readonly WatchJobOptions _options;

    public CreateWatchJobTool(WatchJobRepository repo, ClockContext clock, IOptions<WatchJobOptions> options)
    {
        _repo = repo;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var title = GetString(args, "title");
        var goal = GetString(args, "goal");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(goal))
        {
            return Error("title und goal sind erforderlich.");
        }

        var kindRaw = GetString(args, "kind") ?? "web_availability";
        if (!WatchJobKindExtensions.TryParseWire(kindRaw, out var kind))
        {
            return Error($"Unbekannte kind '{kindRaw}'. Erlaubt: web_availability.");
        }

        var specEl = GetObject(args, "spec");
        var searchQueries = GetStringArray(specEl, "searchQueries");
        var targetUrls = GetStringArray(specEl, "targetUrls");
        var judgeQuestion = GetString(specEl, "judgeQuestion");
        var successCriteria = GetString(specEl, "successCriteria");

        if (searchQueries.Count == 0 && targetUrls.Count == 0)
        {
            return Error("Mindestens eine searchQuery oder targetUrl ist erforderlich.");
        }

        foreach (var targetUrl in targetUrls)
        {
            if (!SsrfGuard.IsAllowedUrl(targetUrl, out _))
            {
                return Error($"Ungültige targetUrl '{targetUrl}'. Nur absolute http:// oder https:// URLs sind erlaubt.");
            }
        }

        if (string.IsNullOrWhiteSpace(judgeQuestion) || string.IsNullOrWhiteSpace(successCriteria))
        {
            return Error("spec.judgeQuestion und spec.successCriteria sind erforderlich.");
        }

        var active = await _repo.ListActiveByUserAsync(ct);
        if (active.Count >= _options.MaxActivePerUser)
        {
            return Error($"Limit aktiver Watch-Jobs erreicht ({_options.MaxActivePerUser}). Bitte zuerst einen pausieren/stoppen.");
        }

        var scheduleEl = GetObject(args, "schedule");
        var requestedInterval = GetInt(scheduleEl, "intervalSeconds") ?? _options.MinIntervalSeconds;
        var interval = Math.Max(requestedInterval, _options.MinIntervalSeconds);
        var maxInterval = Math.Max(GetInt(scheduleEl, "maxIntervalSeconds") ?? interval * 10, interval);

        var notifyEl = GetObject(args, "notify");
        var channels = GetStringArray(notifyEl, "channels");
        if (channels.Count == 0) channels = new[] { "webpush" };
        var fireOnce = GetBool(notifyEl, "fireOnce") ?? true;

        var budgetEl = GetObject(args, "budget");
        var maxChecks = GetInt(budgetEl, "maxChecks");
        var expiresAt = GetDateTime(budgetEl, "expiresAt");

        var now = _clock.Build().NowUtc;
        var job = await _repo.InsertAsync(
            title: title,
            goal: goal,
            kind: kind,
            spec: new WatchJobSpec(searchQueries, targetUrls, judgeQuestion!, successCriteria!),
            schedule: new WatchJobSchedule(interval, maxInterval),
            notify: new WatchJobNotify(channels, fireOnce),
            budget: new WatchJobBudget(maxChecks, expiresAt),
            nextDueAt: now,
            now: now,
            ct);

        return JsonSerializer.SerializeToElement(new
        {
            ok = true,
            id = job.Id,
            title = job.Title,
            interval_seconds = interval,
            next_check = job.NextDueAt.ToString("O"),
        });
    }

    private static JsonElement Error(string message) =>
        JsonSerializer.SerializeToElement(new { ok = false, error = message });

    private static JsonElement GetObject(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var el)
        && el.ValueKind == JsonValueKind.Object
            ? el
            : default;

    private static string? GetString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var el)
            || el.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(name, out var el)
            || el.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return el.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static int? GetInt(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(el.GetString(), out var i) => i,
            _ => null,
        };
    }

    private static bool? GetBool(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static DateTimeOffset? GetDateTime(JsonElement parent, string name)
    {
        var s = GetString(parent, name);
        return s is not null && DateTimeOffset.TryParse(s, out var dt) ? dt : null;
    }
}
