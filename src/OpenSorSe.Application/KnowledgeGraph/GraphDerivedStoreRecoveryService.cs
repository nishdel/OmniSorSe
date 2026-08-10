namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Coordinates explicit provider-neutral recovery of the rebuildable graph sidecar.</summary>
public sealed class GraphDerivedStoreRecoveryService : IGraphDerivedStoreRecoveryService
{
    /// <summary>Gets the exact confirmation required before a corrupt derived store is replaced.</summary>
    public const string RecoveryConfirmation = "REBUILD DERIVED GRAPH STORE";

    private readonly IGraphStorageLifecycle _lifecycle;
    private readonly IGraphDerivedStoreRecoveryProvider _provider;

    /// <summary>Creates the reviewed derived-store recovery boundary.</summary>
    public GraphDerivedStoreRecoveryService(
        IGraphStorageLifecycle lifecycle,
        IGraphDerivedStoreRecoveryProvider provider)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> RecoverAsync(
        string confirmationText,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(confirmationText, RecoveryConfirmation, StringComparison.Ordinal))
        {
            return new GraphOperationResult(
                false,
                $"Type {RecoveryConfirmation} to quarantine and rebuild only the derived graph store.",
                0);
        }

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

        if (state != GraphStorageProvisioningState.RepairRequired)
        {
            return new GraphOperationResult(
                false,
                state == GraphStorageProvisioningState.Unprovisioned
                    ? "Graph storage has not been provisioned; no derived store can be recovered."
                    : "The derived graph store is healthy and does not require replacement.",
                0);
        }

        return await _provider.RecoverDerivedStoreAsync(confirmationText, cancellationToken)
            .ConfigureAwait(false);
    }
}
