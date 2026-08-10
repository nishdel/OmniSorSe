using OpenSorSe.Application.Indexing;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Adapts the existing cross-platform indexing resource monitor for graph projection.</summary>
public sealed class GraphResourceAdmissionPolicy : IGraphResourceAdmissionPolicy
{
    private readonly IBackgroundResourceMonitor _resourceMonitor;

    /// <summary>Initializes the shared resource-monitor adapter.</summary>
    public GraphResourceAdmissionPolicy(IBackgroundResourceMonitor resourceMonitor) =>
        _resourceMonitor = resourceMonitor ?? throw new ArgumentNullException(nameof(resourceMonitor));

    /// <inheritdoc />
    public async Task<GraphResourceEligibility> GetEligibilityAsync(
        GraphControlSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        var indexingSettings = new DeepIndexingSettings
        {
            Enabled = settings.IsEnabled,
            ResourceMode = settings.ResourceMode,
            MaximumConcurrency = settings.MaximumConcurrency,
            ProcessOnlyWhileIdle = settings.ProcessOnlyWhileIdle,
            ProcessOnlyWhileConnectedToPower = settings.ProcessOnlyWhileConnectedToPower,
            PauseBelowBatteryPercentage = settings.PauseBelowBatteryPercentage,
            ProcessingWindowStartHour = settings.ProcessingWindowStartHour,
            ProcessingWindowEndHour = settings.ProcessingWindowEndHour,
        };
        var eligibility = await _resourceMonitor
            .GetEligibilityAsync(indexingSettings, cancellationToken)
            .ConfigureAwait(false);
        return new GraphResourceEligibility(eligibility.MayProcess, eligibility.WaitingReason);
    }

    internal static void Validate(GraphControlSettings settings)
    {
        if (!Enum.IsDefined(settings.ResourceMode) ||
            settings.MaximumConcurrency != ConcurrencyFor(settings.ResourceMode) ||
            settings.MaximumConcurrency is < 1 or > GraphLimits.MaximumWorkerConcurrency || settings.Revision < 0 ||
            settings.PauseBelowBatteryPercentage is < 1 or > 100 ||
            settings.ProcessingWindowStartHour is < 0 or > 23 ||
            settings.ProcessingWindowEndHour is < 0 or > 23 ||
            settings.ProcessingWindowStartHour.HasValue != settings.ProcessingWindowEndHour.HasValue ||
            (settings.ProcessingWindowStartHour.HasValue &&
             settings.ProcessingWindowStartHour == settings.ProcessingWindowEndHour) ||
            (settings.IsEnabled && !settings.ConsentConfirmed))
        {
            throw new InvalidDataException("Graph control settings are invalid or exceed the stable resource policy.");
        }
    }

    internal static int ConcurrencyFor(IndexingResourceMode mode) => mode switch
    {
        IndexingResourceMode.Eco => 1,
        IndexingResourceMode.Balanced => 2,
        IndexingResourceMode.Fast => 4,
        _ => throw new InvalidDataException("The graph resource mode is not supported."),
    };
}

/// <summary>Schedules resource probes through an injectable clock.</summary>
public sealed class GraphResourceProbeScheduler : IGraphResourceProbeScheduler
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the scheduler with an injectable clock.</summary>
    public GraphResourceProbeScheduler(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public Task WaitForNextProbeAsync(CancellationToken cancellationToken = default) =>
        Task.Delay(GraphLimits.ResourceProbeInterval, _timeProvider, cancellationToken);
}
