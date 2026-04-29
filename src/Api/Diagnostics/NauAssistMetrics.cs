using Prometheus;

namespace NauAssist.Api.Diagnostics;

/// <summary>
/// Zentral registrierte Prometheus-Metriken. Phase 4 legt sie an;
/// AICore (Etappe 5/Phase 6) und Reflexions-Loop (Etappe 5) befüllen sie.
/// </summary>
public static class NauAssistMetrics
{
    public static readonly Counter LlmTokensTotal = Metrics.CreateCounter(
        "nauassist_llm_tokens_total",
        "Anzahl LLM-Token (Prompt + Antwort).",
        new CounterConfiguration
        {
            LabelNames = new[] { "direction", "model" },
        });

    public static readonly Histogram LlmLatencySeconds = Metrics.CreateHistogram(
        "nauassist_llm_latency_seconds",
        "Latenz pro LLM-Aufruf in Sekunden.",
        new HistogramConfiguration
        {
            LabelNames = new[] { "model" },
            Buckets = Histogram.ExponentialBuckets(start: 0.05, factor: 2, count: 10),
        });

    public static readonly Counter ReflectionCyclesTotal = Metrics.CreateCounter(
        "nauassist_reflection_cycles_total",
        "Reflexions-Iterationen, gruppiert nach Outcome (notify/silent/skip).",
        new CounterConfiguration
        {
            LabelNames = new[] { "outcome" },
        });
}
