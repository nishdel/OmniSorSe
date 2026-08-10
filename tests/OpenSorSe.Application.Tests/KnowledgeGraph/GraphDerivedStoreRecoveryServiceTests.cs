using OpenSorSe.Application.KnowledgeGraph;

namespace OpenSorSe.Application.Tests.KnowledgeGraph;

/// <summary>Verifies the reviewed provider-neutral derived-store recovery boundary.</summary>
public sealed class GraphDerivedStoreRecoveryServiceTests
{
    /// <summary>Verifies exact confirmation and repair-required state fence provider mutation.</summary>
    [Fact]
    public async Task RecoveryRequiresExactConfirmationAndRepairState()
    {
        var lifecycle = new FakeGraphStorageLifecycle
        {
            State = GraphStorageProvisioningState.RepairRequired,
        };
        var service = new GraphDerivedStoreRecoveryService(lifecycle, lifecycle);

        var rejected = await service.RecoverAsync("rebuild");
        var recovered = await service.RecoverAsync(GraphDerivedStoreRecoveryService.RecoveryConfirmation);

        Assert.False(rejected.Succeeded);
        Assert.True(recovered.Succeeded);
        Assert.Equal(1, lifecycle.DerivedRecoveryCount);
        Assert.Equal(GraphDerivedStoreRecoveryService.RecoveryConfirmation, lifecycle.DerivedRecoveryConfirmation);
    }

    /// <summary>Verifies a healthy or unprovisioned lifecycle never delegates destructive recovery.</summary>
    [Theory]
    [InlineData(GraphStorageProvisioningState.Unprovisioned)]
    [InlineData(GraphStorageProvisioningState.Provisioned)]
    public async Task RecoveryDoesNotReplaceHealthyOrUnprovisionedStore(GraphStorageProvisioningState state)
    {
        var lifecycle = new FakeGraphStorageLifecycle { State = state };
        var service = new GraphDerivedStoreRecoveryService(lifecycle, lifecycle);

        var result = await service.RecoverAsync(GraphDerivedStoreRecoveryService.RecoveryConfirmation);

        Assert.False(result.Succeeded);
        Assert.Equal(0, lifecycle.DerivedRecoveryCount);
    }
}
