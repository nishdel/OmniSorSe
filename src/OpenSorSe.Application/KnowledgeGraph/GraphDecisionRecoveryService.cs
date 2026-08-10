namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Provides a lifecycle-aware, provider-neutral graph-decision recovery boundary.</summary>
public sealed class GraphDecisionRecoveryService : IGraphDecisionRecoveryService
{
    /// <summary>Gets the exact confirmation required before a managed recovery point is restored.</summary>
    public const string RestoreConfirmation = "RESTORE GRAPH DECISIONS";

    private readonly IGraphDecisionStore _store;
    private readonly IGraphStorageLifecycle _lifecycle;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the recovery boundary.</summary>
    public GraphDecisionRecoveryService(
        IGraphDecisionStore store,
        IGraphStorageLifecycle lifecycle,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphDecisionRecoveryPoint>> GetRecoveryPointsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureRecoveryStateAsync(cancellationToken).ConfigureAwait(false);
        var points = await _store.GetRecoveryPointsAsync(cancellationToken).ConfigureAwait(false);
        if (points is null || points.Count > 64 ||
            points.Select(item => item.RecoveryPointId).Distinct(StringComparer.Ordinal).Count() != points.Count ||
            points.Any(item => !IsValid(item)))
        {
            throw new GraphAccessUnavailableException("decision-recovery-points-invalid");
        }

        return points;
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> RestoreAsync(
        string recoveryPointId,
        string confirmationText,
        CancellationToken cancellationToken = default)
    {
        ValidateRecoveryPointId(recoveryPointId);
        if (!string.Equals(confirmationText, RestoreConfirmation, StringComparison.Ordinal))
        {
            return new GraphOperationResult(false, $"Type {RestoreConfirmation} to restore graph-native decisions.", 0);
        }

        await EnsureRecoveryStateAsync(cancellationToken).ConfigureAwait(false);
        return await _store.RestoreAsync(
                recoveryPointId,
                confirmationText,
                _timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task EnsureRecoveryStateAsync(CancellationToken cancellationToken)
    {
        GraphStorageProvisioningState state;
        try
        {
            state = await _lifecycle.GetProvisioningStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new GraphAccessUnavailableException("graph-storage-lifecycle-unavailable");
        }

        if (state is not (GraphStorageProvisioningState.Provisioned or GraphStorageProvisioningState.RepairRequired))
        {
            throw new GraphAccessUnavailableException("graph-storage-unprovisioned");
        }
    }

    private static bool IsValid(GraphDecisionRecoveryPoint point) =>
        point is not null &&
        IsRecoveryPointId(point.RecoveryPointId) &&
        point.DecisionSequence >= 0 && point.PrivacySequence >= 0 && point.StoreGeneration > 0 &&
        point.CommittedAtUtc != default &&
        point.StatusCode is "verified" or "privacy-floor-stale" or "corrupt" or "unsupported-schema" &&
        (!point.IsRestorable || string.Equals(point.StatusCode, "verified", StringComparison.Ordinal));

    private static void ValidateRecoveryPointId(string value)
    {
        if (!IsRecoveryPointId(value))
        {
            throw new ArgumentException("A graph-decision recovery point ID must be a bounded opaque managed ID.", nameof(value));
        }
    }

    private static bool IsRecoveryPointId(string? value) =>
        value is { Length: > 0 and <= GraphLimits.MaximumStableIdCharacters } &&
        value.StartsWith("decision-backup-", StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-');
}
