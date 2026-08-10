using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.Tests.KnowledgeGraph;

/// <summary>Verifies consent-safe composite graph-sidecar provisioning.</summary>
public sealed class SqliteGraphStorageLifecycleTests
{
    /// <summary>Verifies inspection is non-mutating and provisioning promotes two valid stores.</summary>
    [Fact]
    public async Task InspectionDoesNotProvisionAndProvisionPromotesBothStores()
    {
        using var fixture = new LifecycleFixture();
        await using var lifecycle = new SqliteGraphStorageLifecycle(fixture.IndexDirectory);

        Assert.Equal(GraphStorageProvisioningState.Unprovisioned, await lifecycle.GetProvisioningStateAsync());
        Assert.False(Directory.Exists(fixture.IndexDirectory));

        await lifecycle.ProvisionAsync();

        Assert.Equal(GraphStorageProvisioningState.Provisioned, await lifecycle.GetProvisioningStateAsync());
        Assert.True(System.IO.File.Exists(lifecycle.GraphDatabasePath));
        Assert.True(System.IO.File.Exists(lifecycle.DecisionDatabasePath));
        Assert.False(System.IO.File.Exists(Path.Combine(fixture.IndexDirectory, ".knowledge-data.bootstrap.json")));
        Assert.False(System.IO.File.Exists(Path.Combine(fixture.IndexDirectory, ".knowledge-graph.bootstrap.db")));
        Assert.False(System.IO.File.Exists(Path.Combine(fixture.IndexDirectory, ".knowledge-decisions.bootstrap.db")));
        var storage = await lifecycle.GetStorageBreakdownAsync();
        Assert.True(storage.IsInventoryVerified);
        Assert.True(storage.DerivedStoreBytes > 0);
        Assert.True(storage.DecisionLedgerBytes > 0);
        Assert.Equal(storage.DerivedStoreBytes + storage.DecisionLedgerBytes, storage.TotalBytes);
    }

    /// <summary>Verifies direct provider operations cannot create graph sidecars before lifecycle initialization.</summary>
    [Fact]
    public async Task DirectStoresFailClosedBeforeInitializationWithoutCreatingSidecars()
    {
        using var fixture = new LifecycleFixture();
        var graphPath = Path.Combine(fixture.IndexDirectory, "knowledge-graph.db");
        var decisionPath = Path.Combine(fixture.IndexDirectory, "knowledge-decisions.db");
        await using var graph = new SqliteGraphStore(graphPath);
        await using var decisions = new SqliteGraphDecisionStore(decisionPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => graph.GetStatusAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => decisions.GetSnapshotAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => decisions.ClearAsync("CLEAR GRAPH DECISIONS", DateTimeOffset.UnixEpoch));

        Assert.False(Directory.Exists(fixture.IndexDirectory));
        Assert.False(System.IO.File.Exists(graphPath));
        Assert.False(System.IO.File.Exists(decisionPath));
    }

    /// <summary>Verifies DI ownership and coordinator ownership may safely converge on disposal.</summary>
    [Fact]
    public async Task ConcreteStoreDisposalIsIdempotent()
    {
        using var fixture = new LifecycleFixture();
        var graph = new SqliteGraphStore(Path.Combine(fixture.IndexDirectory, "knowledge-graph.db"));
        var decisions = new SqliteGraphDecisionStore(Path.Combine(fixture.IndexDirectory, "knowledge-decisions.db"));

        await graph.DisposeAsync();
        await graph.DisposeAsync();
        await decisions.DisposeAsync();
        await decisions.DisposeAsync();
    }

    /// <summary>Verifies concurrent first-consent provisioning is serialized and idempotent.</summary>
    [Fact]
    public async Task ConcurrentProvisionIsSerializedAndIdempotent()
    {
        using var fixture = new LifecycleFixture();
        await using var first = new SqliteGraphStorageLifecycle(fixture.IndexDirectory);
        await using var second = new SqliteGraphStorageLifecycle(fixture.IndexDirectory);

        await Task.WhenAll(first.ProvisionAsync(), second.ProvisionAsync());

        Assert.Equal(GraphStorageProvisioningState.Provisioned, await first.GetProvisioningStateAsync());
        Assert.Equal(GraphStorageProvisioningState.Provisioned, await second.GetProvisioningStateAsync());
    }

    /// <summary>Verifies a lone sidecar fails closed without silently creating its counterpart.</summary>
    [Fact]
    public async Task PartialStoreRequiresRepairWithoutCreatingCounterpart()
    {
        using var fixture = new LifecycleFixture();
        Directory.CreateDirectory(fixture.IndexDirectory);
        var graphPath = Path.Combine(fixture.IndexDirectory, "knowledge-graph.db");
        await using (var graph = new SqliteGraphStore(graphPath))
        {
            await graph.InitializeAsync();
        }

        await using var lifecycle = new SqliteGraphStorageLifecycle(fixture.IndexDirectory);
        Assert.Equal(GraphStorageProvisioningState.RepairRequired, await lifecycle.GetProvisioningStateAsync());
        Assert.False(System.IO.File.Exists(lifecycle.DecisionDatabasePath));
    }

    /// <summary>Verifies reviewed recovery quarantines only a corrupt derived store and preserves every authority input.</summary>
    [Fact]
    public async Task ReviewedDerivedRecoveryQuarantinesCorruptionAndPreservesAuthority()
    {
        using var fixture = new LifecycleFixture();
        await using var lifecycle = new SqliteGraphStorageLifecycle(fixture.IndexDirectory);
        await lifecycle.ProvisionAsync();
        var deepIndexPath = Path.Combine(fixture.IndexDirectory, "deep-index.db");
        var sourcePath = Path.Combine(fixture.Root, "source-fixture.txt");
        File.WriteAllText(deepIndexPath, "synthetic deep-index sentinel", Encoding.UTF8);
        File.WriteAllText(sourcePath, "synthetic source sentinel", Encoding.UTF8);
        var decisionHash = HashFile(lifecycle.DecisionDatabasePath);
        var deepIndexHash = HashFile(deepIndexPath);
        var sourceHash = HashFile(sourcePath);
        var corruptPayload = Encoding.UTF8.GetBytes("not a SQLite database");
        SqliteConnection.ClearAllPools();
        File.WriteAllBytes(lifecycle.GraphDatabasePath, corruptPayload);

        Assert.Equal(GraphStorageProvisioningState.RepairRequired, await lifecycle.GetProvisioningStateAsync());
        var rejected = await lifecycle.RecoverDerivedStoreAsync("rebuild derived graph store");
        Assert.False(rejected.Succeeded);
        Assert.Equal(corruptPayload, File.ReadAllBytes(lifecycle.GraphDatabasePath));

        var recovered = await lifecycle.RecoverDerivedStoreAsync(
            GraphDerivedStoreRecoveryService.RecoveryConfirmation);

        Assert.True(recovered.Succeeded);
        Assert.Equal(GraphStorageProvisioningState.Provisioned, await lifecycle.GetProvisioningStateAsync());
        Assert.Equal(decisionHash, HashFile(lifecycle.DecisionDatabasePath));
        Assert.Equal(deepIndexHash, HashFile(deepIndexPath));
        Assert.Equal(sourceHash, HashFile(sourcePath));
        var quarantine = Assert.Single(
            Directory.EnumerateFiles(fixture.IndexDirectory, ".knowledge-graph.quarantine.*.db"));
        Assert.Equal(corruptPayload, File.ReadAllBytes(quarantine));
        Assert.False(File.Exists(Path.Combine(fixture.IndexDirectory, ".knowledge-graph.recovery.json")));
        Assert.False(File.Exists(Path.Combine(fixture.IndexDirectory, ".knowledge-graph.recovery.staging.db")));

        await using var decisions = new SqliteGraphDecisionStore(lifecycle.DecisionDatabasePath);
        await decisions.InitializeAsync();
        Assert.True((await decisions.GetSnapshotAsync()).IsValid);
    }

    /// <summary>Verifies a new lifecycle instance resumes a journaled promotion without discarding quarantine evidence.</summary>
    [Fact]
    public async Task JournaledDerivedRecoveryResumesAfterRestart()
    {
        using var fixture = new LifecycleFixture();
        string graphPath;
        string decisionPath;
        await using (var initial = new SqliteGraphStorageLifecycle(fixture.IndexDirectory))
        {
            await initial.ProvisionAsync();
            graphPath = initial.GraphDatabasePath;
            decisionPath = initial.DecisionDatabasePath;
        }

        var decisionHash = HashFile(decisionPath);
        var recoveryId = Guid.NewGuid().ToString("N");
        var stagingName = ".knowledge-graph.recovery.staging.db";
        var stagingPath = Path.Combine(fixture.IndexDirectory, stagingName);
        var quarantineName = $".knowledge-graph.quarantine.{recoveryId}.db";
        var quarantinePath = Path.Combine(fixture.IndexDirectory, quarantineName);
        File.Copy(graphPath, stagingPath);
        var stagingHash = HashFile(stagingPath);
        SqliteConnection.ClearAllPools();
        File.WriteAllText(graphPath, "corrupt derived graph", Encoding.UTF8);
        File.Move(graphPath, quarantinePath);
        File.WriteAllText(
            Path.Combine(fixture.IndexDirectory, ".knowledge-graph.recovery.json"),
            JsonSerializer.Serialize(new
            {
                Version = 1,
                RecoveryId = recoveryId,
                StagingFileName = stagingName,
                QuarantineFileName = quarantineName,
                StagedSha256 = stagingHash,
                State = "Promoting",
                CreatedUtcTicks = DateTimeOffset.UnixEpoch.UtcTicks,
            }));

        await using var restarted = new SqliteGraphStorageLifecycle(fixture.IndexDirectory);
        Assert.Equal(GraphStorageProvisioningState.RepairRequired, await restarted.GetProvisioningStateAsync());

        var recovered = await restarted.RecoverDerivedStoreAsync(
            GraphDerivedStoreRecoveryService.RecoveryConfirmation);

        Assert.True(recovered.Succeeded);
        Assert.Equal(GraphStorageProvisioningState.Provisioned, await restarted.GetProvisioningStateAsync());
        Assert.Equal(decisionHash, HashFile(decisionPath));
        Assert.True(File.Exists(quarantinePath));
        Assert.False(File.Exists(stagingPath));
        Assert.False(File.Exists(Path.Combine(fixture.IndexDirectory, ".knowledge-graph.recovery.json")));
    }

    /// <summary>Verifies authoritative decision corruption prevents any derived-store quarantine or replacement.</summary>
    [Fact]
    public async Task DerivedRecoveryFailsClosedWhenDecisionAuthorityIsCorrupt()
    {
        using var fixture = new LifecycleFixture();
        await using var lifecycle = new SqliteGraphStorageLifecycle(fixture.IndexDirectory);
        await lifecycle.ProvisionAsync();
        var graphPayload = Encoding.UTF8.GetBytes("corrupt graph");
        var decisionPayload = Encoding.UTF8.GetBytes("corrupt decisions");
        SqliteConnection.ClearAllPools();
        File.WriteAllBytes(lifecycle.GraphDatabasePath, graphPayload);
        File.WriteAllBytes(lifecycle.DecisionDatabasePath, decisionPayload);

        var exception = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(() =>
            lifecycle.RecoverDerivedStoreAsync(GraphDerivedStoreRecoveryService.RecoveryConfirmation));

        Assert.Equal(SqliteKnowledgeFailureKind.Corrupt, exception.Kind);
        Assert.Equal(graphPayload, File.ReadAllBytes(lifecycle.GraphDatabasePath));
        Assert.Equal(decisionPayload, File.ReadAllBytes(lifecycle.DecisionDatabasePath));
        Assert.Empty(Directory.EnumerateFiles(fixture.IndexDirectory, ".knowledge-graph.quarantine.*.db"));
    }

    /// <summary>Verifies a valid newer derived schema is preserved unchanged for a compatible application.</summary>
    [Fact]
    public async Task DerivedRecoveryRejectsUnsupportedNewerSchemaWithoutQuarantine()
    {
        using var fixture = new LifecycleFixture();
        await using var lifecycle = new SqliteGraphStorageLifecycle(fixture.IndexDirectory);
        await lifecycle.ProvisionAsync();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = lifecycle.GraphDatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }

        var newerHash = HashFile(lifecycle.GraphDatabasePath);
        var exception = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(() =>
            lifecycle.RecoverDerivedStoreAsync(GraphDerivedStoreRecoveryService.RecoveryConfirmation));

        Assert.Equal(SqliteKnowledgeFailureKind.UnsupportedSchema, exception.Kind);
        Assert.Equal(newerHash, HashFile(lifecycle.GraphDatabasePath));
        Assert.Empty(Directory.EnumerateFiles(fixture.IndexDirectory, ".knowledge-graph.quarantine.*.db"));
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class LifecycleFixture : IDisposable
    {
        internal LifecycleFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "opensorse-graph-lifecycle-tests", Guid.NewGuid().ToString("N"));
            IndexDirectory = Path.Combine(Root, "index");
        }

        internal string Root { get; }
        internal string IndexDirectory { get; }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
