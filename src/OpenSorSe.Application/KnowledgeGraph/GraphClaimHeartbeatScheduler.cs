namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Uses an injectable time provider to schedule the approved claim heartbeat.</summary>
public sealed class GraphClaimHeartbeatScheduler : IGraphClaimHeartbeatScheduler
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the scheduler.</summary>
    public GraphClaimHeartbeatScheduler(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public Task WaitForHeartbeatAsync(CancellationToken cancellationToken = default) =>
        Task.Delay(GraphLimits.HeartbeatInterval, _timeProvider, cancellationToken);
}

/// <summary>Uses an injectable clock for low-frequency missed-notification reconciliation.</summary>
public sealed class GraphPeriodicReconciliationScheduler : IGraphPeriodicReconciliationScheduler
{
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the scheduler.</summary>
    public GraphPeriodicReconciliationScheduler(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public Task WaitForNextReconciliationAsync(CancellationToken cancellationToken = default) =>
        Task.Delay(GraphLimits.PeriodicReconciliationInterval, _timeProvider, cancellationToken);
}
