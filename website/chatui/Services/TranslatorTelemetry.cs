using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace chatui.Services;

/// <summary>
/// Central OpenTelemetry <see cref="ActivitySource"/> and <see cref="Meter"/> for
/// application-specific traces + metrics. Follows the recommended pattern for
/// OTel-first .NET apps (Azure Monitor OpenTelemetry Distro).
///
/// <para>
/// All spans opened via <see cref="StartActivity"/> automatically show up as
/// dependencies in Application Insights. Counters and histograms show up under
/// Metrics blade (Azure Monitor) and in the <c>customMetrics</c> table.
/// </para>
///
/// <para>Metric naming follows OTel semantic conventions where possible:
///  * <c>&lt;domain&gt;.duration</c> (unit ms) — request latency histograms
///  * <c>&lt;domain&gt;.chars</c>              — billable character counters
///  * <c>&lt;domain&gt;.calls</c>              — count of operations
/// </para>
/// </summary>
public sealed class TranslatorTelemetry : IDisposable
{
    public const string SourceName = "AiTranslator";

    public ActivitySource ActivitySource { get; } = new(SourceName);
    public Meter Meter { get; }

    // Counters (dimensionless — number of calls)
    public Counter<long> TranslateCalls { get; }
    public Counter<long> TtsCalls { get; }
    public Counter<long> FoundryCalls { get; }

    // Character counters — approximate proxy for billable units
    // (Translator: chars translated; TTS: chars synthesised; Foundry: token proxy).
    public Counter<long> TranslateInputChars { get; }
    public Counter<long> TranslateOutputChars { get; }
    public Counter<long> TtsChars { get; }
    public Counter<long> FoundryPromptChars { get; }
    public Counter<long> FoundryResponseChars { get; }

    // Latency histograms (ms). Recorded values get percentile bucketing by OTel.
    public Histogram<double> TranslateDuration { get; }
    public Histogram<double> TtsDuration { get; }
    public Histogram<double> FoundryDuration { get; }

    public TranslatorTelemetry()
    {
        Meter = new Meter(SourceName, "1.0.0");

        TranslateCalls        = Meter.CreateCounter<long>("aitr.translate.calls",         unit: "{call}",  description: "Number of translate requests.");
        TtsCalls              = Meter.CreateCounter<long>("aitr.tts.calls",               unit: "{call}",  description: "Number of TTS synth requests.");
        FoundryCalls          = Meter.CreateCounter<long>("aitr.foundry.calls",           unit: "{call}",  description: "Number of Foundry agent runs.");

        TranslateInputChars   = Meter.CreateCounter<long>("aitr.translate.input.chars",   unit: "{char}",  description: "Total chars submitted for translation.");
        TranslateOutputChars  = Meter.CreateCounter<long>("aitr.translate.output.chars",  unit: "{char}",  description: "Total chars produced by translation.");
        TtsChars              = Meter.CreateCounter<long>("aitr.tts.chars",               unit: "{char}",  description: "Total chars sent to Neural TTS.");
        FoundryPromptChars    = Meter.CreateCounter<long>("aitr.foundry.prompt.chars",    unit: "{char}",  description: "Total chars sent to Foundry agent (input).");
        FoundryResponseChars  = Meter.CreateCounter<long>("aitr.foundry.response.chars",  unit: "{char}",  description: "Total chars returned by Foundry agent (output).");

        TranslateDuration     = Meter.CreateHistogram<double>("aitr.translate.duration",  unit: "ms", description: "Translator API latency.");
        TtsDuration           = Meter.CreateHistogram<double>("aitr.tts.duration",        unit: "ms", description: "TTS synth latency (server-side).");
        FoundryDuration       = Meter.CreateHistogram<double>("aitr.foundry.duration",    unit: "ms", description: "Foundry agent run latency.");
    }

    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        => ActivitySource.StartActivity(name, kind);

    public void Dispose()
    {
        Meter.Dispose();
        ActivitySource.Dispose();
    }
}
