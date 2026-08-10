using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>Validates deterministic resource modes and fail-closed portable policy degradation.</summary>
public sealed class GraphResourceAdmissionPolicyTests
{
    /// <summary>Verifies each stable mode maps to its documented bounded worker ceiling.</summary>
    [Theory]
    [InlineData(IndexingResourceMode.Eco, 1)]
    [InlineData(IndexingResourceMode.Balanced, 2)]
    [InlineData(IndexingResourceMode.Fast, 4)]
    public async Task StableMode_MapsToExpectedConcurrency(IndexingResourceMode mode, int expectedConcurrency)
    {
        var monitor = new CapturingResourceMonitor();
        var policy = new GraphResourceAdmissionPolicy(monitor);

        var eligibility = await policy.GetEligibilityAsync(new GraphControlSettings
        {
            IsEnabled = true,
            ConsentConfirmed = true,
            ResourceMode = mode,
            MaximumConcurrency = expectedConcurrency,
        });

        Assert.True(eligibility.MayProcess);
        Assert.Equal(expectedConcurrency, monitor.Settings!.MaximumConcurrency);
    }

    /// <summary>Verifies unsupported host signals wait with an actionable reason instead of silently processing.</summary>
    [Theory]
    [InlineData(true, false, null, "idle")]
    [InlineData(false, true, null, "power")]
    [InlineData(false, false, 25, "battery")]
    public async Task PortableMonitor_UnsupportedRestriction_WaitsActionably(
        bool idle,
        bool power,
        int? battery,
        string expectedReason)
    {
        var monitor = new PortableBackgroundResourceMonitor(new UtcGraphTimeProvider(TestGraphData.Now));

        var eligibility = await monitor.GetEligibilityAsync(new DeepIndexingSettings
        {
            ProcessOnlyWhileIdle = idle,
            ProcessOnlyWhileConnectedToPower = power,
            PauseBelowBatteryPercentage = battery,
        });

        Assert.False(eligibility.MayProcess);
        Assert.Contains(expectedReason, eligibility.WaitingReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Disable", eligibility.WaitingReason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies portable time windows still use an injectable local clock when no host signal is required.</summary>
    [Fact]
    public async Task PortableMonitor_TimeWindow_IsDeterministic()
    {
        var monitor = new PortableBackgroundResourceMonitor(new UtcGraphTimeProvider(TestGraphData.Now));

        var within = await monitor.GetEligibilityAsync(new DeepIndexingSettings
        {
            ProcessingWindowStartHour = 11,
            ProcessingWindowEndHour = 13,
        });
        var outside = await monitor.GetEligibilityAsync(new DeepIndexingSettings
        {
            ProcessingWindowStartHour = 13,
            ProcessingWindowEndHour = 14,
        });

        Assert.True(within.MayProcess);
        Assert.False(outside.MayProcess);
    }

    private sealed class CapturingResourceMonitor : IBackgroundResourceMonitor
    {
        internal DeepIndexingSettings? Settings { get; private set; }

        public Task<BackgroundResourceEligibility> GetEligibilityAsync(
            DeepIndexingSettings settings,
            CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.FromResult(new BackgroundResourceEligibility(true, null));
        }
    }

    private sealed class UtcGraphTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
