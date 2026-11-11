namespace Game.Api.Services;

public static class LiveApiOperation
{
    public const string Dialogue = "dialogue";
    public const string CareerEnding = "career_ending";
}

public sealed record LiveApiMetricSnapshot(
    int ProviderRequestCount,
    int NetworkCallCount,
    int SuccessfulCallCount,
    int FailedCallCount,
    double? SuccessRatePercent,
    double? AverageLatencyMs,
    double? MaximumLatencyMs,
    int FallbackCount);

public sealed record LiveApiMetricsSnapshot(
    LiveApiMetricSnapshot Overall,
    LiveApiMetricSnapshot Dialogue,
    LiveApiMetricSnapshot CareerEnding);

public sealed class LiveApiMetrics
{
    private readonly object sync = new();
    private readonly MetricAccumulator dialogue = new();
    private readonly MetricAccumulator careerEnding = new();

    public void RecordProviderRequest(string operation)
    {
        lock (sync) Select(operation).ProviderRequestCount++;
    }

    public void RecordNetworkCall(string operation, TimeSpan latency, bool successful)
    {
        lock (sync)
        {
            var metric = Select(operation);
            metric.NetworkCallCount++;
            if (successful) metric.SuccessfulCallCount++;
            else metric.FailedCallCount++;
            var milliseconds = Math.Max(0, latency.TotalMilliseconds);
            metric.TotalLatencyMs += milliseconds;
            metric.MaximumLatencyMs = Math.Max(metric.MaximumLatencyMs, milliseconds);
        }
    }

    public void RecordFallback(string operation)
    {
        lock (sync) Select(operation).FallbackCount++;
    }

    public LiveApiMetricsSnapshot Snapshot()
    {
        lock (sync)
        {
            var dialogueSnapshot = Build(dialogue);
            var careerSnapshot = Build(careerEnding);
            var overall = new MetricAccumulator
            {
                ProviderRequestCount = dialogue.ProviderRequestCount + careerEnding.ProviderRequestCount,
                NetworkCallCount = dialogue.NetworkCallCount + careerEnding.NetworkCallCount,
                SuccessfulCallCount = dialogue.SuccessfulCallCount + careerEnding.SuccessfulCallCount,
                FailedCallCount = dialogue.FailedCallCount + careerEnding.FailedCallCount,
                TotalLatencyMs = dialogue.TotalLatencyMs + careerEnding.TotalLatencyMs,
                MaximumLatencyMs = Math.Max(dialogue.MaximumLatencyMs, careerEnding.MaximumLatencyMs),
                FallbackCount = dialogue.FallbackCount + careerEnding.FallbackCount
            };
            return new LiveApiMetricsSnapshot(Build(overall), dialogueSnapshot, careerSnapshot);
        }
    }

    private MetricAccumulator Select(string operation) => operation switch
    {
        LiveApiOperation.Dialogue => dialogue,
        LiveApiOperation.CareerEnding => careerEnding,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown Live API operation.")
    };

    private static LiveApiMetricSnapshot Build(MetricAccumulator metric)
    {
        var calls = metric.NetworkCallCount;
        return new LiveApiMetricSnapshot(
            metric.ProviderRequestCount,
            calls,
            metric.SuccessfulCallCount,
            metric.FailedCallCount,
            calls == 0 ? null : Math.Round(metric.SuccessfulCallCount * 100.0 / calls, 2),
            calls == 0 ? null : Math.Round(metric.TotalLatencyMs / calls, 2),
            calls == 0 ? null : Math.Round(metric.MaximumLatencyMs, 2),
            metric.FallbackCount);
    }

    private sealed class MetricAccumulator
    {
        public int ProviderRequestCount { get; set; }
        public int NetworkCallCount { get; set; }
        public int SuccessfulCallCount { get; set; }
        public int FailedCallCount { get; set; }
        public double TotalLatencyMs { get; set; }
        public double MaximumLatencyMs { get; set; }
        public int FallbackCount { get; set; }
    }
}
