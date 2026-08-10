namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Compatibility lifecycle for providers whose stores are always already provisioned.</summary>
public sealed class AlwaysProvisionedGraphStorageLifecycle : IGraphStorageLifecycle
{
    /// <inheritdoc />
    public Task<GraphStorageProvisioningState> GetProvisioningStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GraphStorageProvisioningState.Provisioned);
    }

    /// <inheritdoc />
    public Task ProvisionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<GraphStorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GraphStorageBreakdown.Empty);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class GraphStorageAccessGate
{
    internal static async Task EnsureProvisionedAsync(
        IGraphStorageLifecycle lifecycle,
        CancellationToken cancellationToken)
    {
        GraphStorageProvisioningState state;
        try
        {
            state = await lifecycle.GetProvisioningStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new GraphAccessUnavailableException("graph-storage-lifecycle-unavailable");
        }

        if (state != GraphStorageProvisioningState.Provisioned)
        {
            throw new GraphAccessUnavailableException(state == GraphStorageProvisioningState.RepairRequired
                ? "graph-storage-repair-required"
                : "graph-storage-unprovisioned");
        }
    }
}
