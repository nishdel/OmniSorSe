using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>Verifies the provider-neutral graph-decision recovery boundary.</summary>
public sealed class GraphDecisionRecoveryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 16, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies repair-required lifecycle state can enumerate and restore a verified point.</summary>
    [Fact]
    public async Task RepairRequiredStateAllowsVerifiedRecoveryWithoutProviderPaths()
    {
        var store = new FakeGraphDecisionStore();
        store.RecoveryPoints.Add(new GraphDecisionRecoveryPoint(
            "decision-backup-abc123",
            4,
            2,
            3,
            Now,
            true,
            true,
            "verified"));
        var lifecycle = new FakeGraphStorageLifecycle
        {
            State = GraphStorageProvisioningState.RepairRequired,
        };
        var service = new GraphDecisionRecoveryService(store, lifecycle, new FixedGraphTimeProvider(Now));

        var point = Assert.Single(await service.GetRecoveryPointsAsync());
        var restored = await service.RestoreAsync(
            point.RecoveryPointId,
            GraphDecisionRecoveryService.RestoreConfirmation);

        Assert.True(restored.Succeeded);
        Assert.Equal(point.RecoveryPointId, store.RestoredRecoveryPointId);
    }

    /// <summary>Verifies wrong confirmation and unprovisioned state cannot reach the provider.</summary>
    [Fact]
    public async Task ConfirmationAndLifecycleAreFailClosed()
    {
        var store = new FakeGraphDecisionStore();
        var lifecycle = new FakeGraphStorageLifecycle
        {
            State = GraphStorageProvisioningState.RepairRequired,
        };
        var service = new GraphDecisionRecoveryService(store, lifecycle, new FixedGraphTimeProvider(Now));

        var rejected = await service.RestoreAsync("decision-backup-abc123", "restore");
        Assert.False(rejected.Succeeded);
        Assert.Null(store.RestoredRecoveryPointId);

        lifecycle.State = GraphStorageProvisioningState.Unprovisioned;
        await Assert.ThrowsAsync<GraphAccessUnavailableException>(() => service.GetRecoveryPointsAsync());
    }
}
