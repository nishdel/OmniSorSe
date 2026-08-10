using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.Tests.KnowledgeGraph;

/// <summary>Exercises non-rebuildable graph-decision durability and privacy-floor boundaries.</summary>
public sealed class SqliteGraphDecisionStoreDurabilityTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 8, 4, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies privacy decisions and their recovery floor become durable atomically.</summary>
    [Fact]
    public async Task PrivacyDecisionPublishesVerifiedBackupAndFloorAtomically()
    {
        using var fixture = new DecisionFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        await EnableAsync(store);
        var created = await store.AppendAsync(CreateManual(expectedSequence: 0), Epoch);
        var command = new GraphDecisionCommand
        {
            Kind = GraphDecisionKind.Exclude,
            SubjectId = "file-1",
            Reason = "privacy-scope-file",
            ExpectedSequence = created.Sequence,
            ExpectedControlSettingsRevision = 1,
        };

        var excluded = await store.AppendAsync(command, Epoch.AddMinutes(1));
        var replay = await store.AppendAsync(command, Epoch.AddMinutes(2));

        Assert.Equal(excluded.DecisionId, replay.DecisionId);
        Assert.Equal(2, excluded.Sequence);
        Assert.Equal((2L, 1L, 1L, 1L), fixture.ReadRecoveryState());
        var backup = Assert.Single(fixture.BackupDatabases());
        Assert.True(File.Exists(backup + ".manifest.json"));
        Assert.Equal(2L, fixture.Scalar<long>(backup, "SELECT COUNT(*) FROM graph_native_decisions;"));
        Assert.Equal(1L, fixture.Scalar<long>(backup, "SELECT minimum_restorable_privacy_sequence FROM decision_recovery_state WHERE singleton_id = 1;"));

        using var stream = File.OpenRead(backup);
        var checksum = Convert.ToHexString(SHA256.HashData(stream));
        using var manifest = JsonDocument.Parse(File.ReadAllText(backup + ".manifest.json"));
        Assert.Equal(checksum, manifest.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(1, fixture.Scalar<int>(fixture.DatabasePath,
            "SELECT COUNT(*) FROM decision_backup_catalog WHERE state = 'Committed' AND privacy_sequence = 1 AND is_pinned = 1;"));
    }

    /// <summary>Verifies backup preparation failure cannot partially mutate authoritative decisions.</summary>
    [Fact]
    public async Task BackupPreparationFailureLeavesAuthoritativeLedgerUntouched()
    {
        using var fixture = new DecisionFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        await EnableAsync(store);
        var before = await store.GetSnapshotAsync();
        fixture.BlockBackupDirectory();
        var command = new GraphDecisionCommand
        {
            Kind = GraphDecisionKind.Forget,
            SubjectId = "file-1",
            Reason = "privacy-scope-file",
            ExpectedSequence = before.Sequence,
            ExpectedControlSettingsRevision = 1,
        };

        await Assert.ThrowsAnyAsync<IOException>(() => store.AppendAsync(command, Epoch));

        var after = await store.GetSnapshotAsync();
        Assert.Equal(before, after);
        Assert.Equal((0L, 0L, 0L, 1L), fixture.ReadRecoveryState());
        Assert.Empty(await store.ReadAsync(0, 10));
    }

    /// <summary>Verifies clear operations advance generations and retire unsafe recovery points.</summary>
    [Fact]
    public async Task ClearUsesNewGenerationAndRetiresPreClearPrivacyBackup()
    {
        using var fixture = new DecisionFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        await EnableAsync(store);
        await store.AppendAsync(CreateManual(expectedSequence: 0), Epoch);
        await store.AppendAsync(
            new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.Exclude,
                SubjectId = "file-1",
                Reason = "privacy-scope-file",
                ExpectedSequence = 1,
                ExpectedControlSettingsRevision = 1,
            },
            Epoch.AddMinutes(1));
        var before = await store.GetSnapshotAsync();

        var cleared = await store.ClearAsync("CLEAR GRAPH DECISIONS", Epoch.AddMinutes(2));
        var after = await store.GetSnapshotAsync();

        Assert.True(cleared.Succeeded);
        Assert.Equal(2, cleared.AffectedCount);
        Assert.Equal(0, after.Sequence);
        Assert.NotEqual(before.CheckpointId, after.CheckpointId);
        Assert.Equal("decision-checkpoint-2-0", after.CheckpointId);
        Assert.Equal((0L, 2L, 2L, 2L), fixture.ReadRecoveryState());
        var backup = Assert.Single(fixture.BackupDatabases());
        Assert.Equal(0L, fixture.Scalar<long>(backup, "SELECT COUNT(*) FROM graph_native_decisions;"));
        Assert.Equal(2L, fixture.Scalar<long>(backup, "SELECT minimum_restorable_privacy_sequence FROM decision_recovery_state WHERE singleton_id = 1;"));
        Assert.Equal(2, fixture.Scalar<int>(fixture.DatabasePath,
            "SELECT COUNT(*) FROM decision_backup_catalog WHERE state = 'Superseded' AND privacy_sequence < 2;"));
    }

    /// <summary>Verifies ordinary manual decisions receive verified, bounded recovery points.</summary>
    [Fact]
    public async Task OrdinaryDecisionBackupsAreVerifiedAndBounded()
    {
        using var fixture = new DecisionFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        await EnableAsync(store);
        for (var sequence = 0L; sequence < 7; sequence++)
        {
            await store.AppendAsync(
                new GraphDecisionCommand
                {
                    Kind = GraphDecisionKind.LinkNodes,
                    SubjectId = $"node-{sequence}",
                    TargetId = $"target-{sequence}",
                    ExpectedSequence = sequence,
                    ExpectedControlSettingsRevision = 1,
                },
                Epoch.AddSeconds(sequence));
        }

        Assert.Equal(5, fixture.BackupDatabases().Count);
        Assert.Equal(5, fixture.Scalar<int>(fixture.DatabasePath,
            "SELECT COUNT(*) FROM decision_backup_catalog WHERE state = 'Committed' AND backup_class = 'ordinary' AND is_pinned = 0;"));
        Assert.Equal(2, fixture.Scalar<int>(fixture.DatabasePath,
            "SELECT COUNT(*) FROM decision_backup_catalog WHERE state = 'Superseded' AND backup_class = 'ordinary';"));
        foreach (var backup in fixture.BackupDatabases())
        {
            Assert.True(System.IO.File.Exists(backup + ".manifest.json"));
            using var stream = System.IO.File.OpenRead(backup);
            var checksum = Convert.ToHexString(SHA256.HashData(stream));
            using var manifest = JsonDocument.Parse(System.IO.File.ReadAllText(backup + ".manifest.json"));
            Assert.Equal(checksum, manifest.RootElement.GetProperty("sha256").GetString());
        }
    }

    /// <summary>Verifies combined quota pressure rejects a decision before ledger mutation.</summary>
    [Fact]
    public async Task CombinedQuotaFailureLeavesDecisionLedgerUntouched()
    {
        using var fixture = new DecisionFixture();
        await using (var graph = new SqliteGraphStore(fixture.GraphDatabasePath))
        {
            await graph.InitializeAsync();
        }
        fixture.Execute(
            fixture.GraphDatabasePath,
            "INSERT INTO graph_meta(key, value) VALUES ('maximum_total_storage_bytes', '16777216') ON CONFLICT(key) DO UPDATE SET value = excluded.value; CREATE TABLE quota_test_filler(payload BLOB NOT NULL); INSERT INTO quota_test_filler(payload) VALUES (zeroblob(16777216));");

        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        await EnableAsync(store);
        var before = await store.GetSnapshotAsync();

        var exception = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(() =>
            store.AppendAsync(CreateManual(expectedSequence: 0), Epoch));

        Assert.Equal(SqliteKnowledgeFailureKind.Full, exception.Kind);
        Assert.Equal(before, await store.GetSnapshotAsync());
        Assert.Empty(await store.ReadAsync(0, 10));
        Assert.Empty(fixture.BackupDatabases());
    }

    /// <summary>Verifies corrupt checkpoints and unsupported future schemas fail closed.</summary>
    [Fact]
    public async Task InitializationRejectsCheckpointCorruptionAndNewerSchema()
    {
        using var fixture = new DecisionFixture();
        await using (var store = fixture.CreateStore())
        {
            await store.InitializeAsync();
            await EnableAsync(store);
            await store.AppendAsync(CreateManual(expectedSequence: 0), Epoch);
        }

        fixture.Execute(fixture.DatabasePath,
            "UPDATE decision_recovery_state SET decision_checkpoint_hash = 'BAD' WHERE singleton_id = 1;");
        await using (var corrupt = fixture.CreateStore())
        {
            var exception = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(() => corrupt.InitializeAsync());
            Assert.Equal(SqliteKnowledgeFailureKind.Corrupt, exception.Kind);
        }

        fixture.Execute(fixture.DatabasePath,
            "UPDATE decision_recovery_state SET decision_checkpoint_hash = (SELECT CASE WHEN COUNT(*) = 0 THEN '' ELSE decision_checkpoint_hash END FROM decision_recovery_state) WHERE singleton_id = 1;");
        fixture.Execute(fixture.DatabasePath, "PRAGMA user_version = 99;");
        await using var newer = fixture.CreateStore();
        var newerException = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(() => newer.InitializeAsync());
        Assert.Equal(SqliteKnowledgeFailureKind.UnsupportedSchema, newerException.Kind);
    }

    /// <summary>Verifies lifecycle contention is bounded and decision identifiers cannot contain paths.</summary>
    [Fact]
    public async Task LifecycleLockHasBoundedContentionAndDecisionIdsRejectPaths()
    {
        using var fixture = new DecisionFixture();
        var lifecycle = new SqliteKnowledgeLifecycleLock(fixture.LifecycleLockPath);
        await using var lease = await lifecycle.AcquireAsync(TimeSpan.FromSeconds(1));
        var blocked = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(
            () => lifecycle.AcquireAsync(TimeSpan.FromMilliseconds(50)));
        Assert.Equal(SqliteKnowledgeFailureKind.Busy, blocked.Kind);
        await lease.DisposeAsync();

        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        await EnableAsync(store);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendAsync(
            new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.Exclude,
                SubjectId = Path.Combine(fixture.Root, "private.pdf"),
                ExpectedSequence = 0,
                ExpectedControlSettingsRevision = 1,
            },
            Epoch));
    }

    /// <summary>Verifies paged legacy mirror capture survives restart and publishes only when complete.</summary>
    [Fact]
    public async Task LegacyMirrorPagingResumesAfterRestartAndPublishesAtomically()
    {
        using var fixture = new DecisionFixture();
        var observations = new[]
        {
            LegacyObservation("legacy:one", "decision-1", revision: 1),
            LegacyObservation("legacy:two", "decision-2", revision: 2),
        };
        var manifestId = $"kg-legacy:{LegacyManifestHash(observations).ToLowerInvariant()}";

        await using (var first = fixture.CreateStore())
        {
            await first.InitializeAsync();
            await first.BeginLegacyMirrorAsync(manifestId, observations.Length, Epoch);
            await first.StageLegacyMirrorPageAsync(manifestId, 0, [observations[0]], Epoch);
            Assert.Null(await first.GetLegacyMirrorManifestIdAsync());
        }

        await using (var resumed = fixture.CreateStore())
        {
            await resumed.InitializeAsync();
            await resumed.BeginLegacyMirrorAsync(manifestId, observations.Length, Epoch.AddMinutes(1));
            await resumed.StageLegacyMirrorPageAsync(manifestId, 0, [observations[0]], Epoch.AddMinutes(1));
            await resumed.StageLegacyMirrorPageAsync(manifestId, 1, [observations[1]], Epoch.AddMinutes(1));
            await resumed.CompleteLegacyMirrorAsync(manifestId, observations.Length, Epoch.AddMinutes(1));

            Assert.Equal(manifestId, await resumed.GetLegacyMirrorManifestIdAsync());
        }

        Assert.Equal(2, fixture.Scalar<int>(fixture.DatabasePath,
            "SELECT COUNT(*) FROM legacy_relationship_decision_mirror WHERE is_present = 1 AND manifest_id LIKE 'kg-legacy:%';"));
        Assert.Equal(2, fixture.Scalar<int>(fixture.DatabasePath,
            "SELECT COUNT(*) FROM legacy_mirror_ingest_rows WHERE manifest_id LIKE 'kg-legacy:%';"));
    }

    /// <summary>Verifies incomplete or conflicting mirror input never replaces the last complete generation.</summary>
    [Fact]
    public async Task LegacyMirrorRejectsIncompleteAndConflictingPagesWithoutChangingAuthority()
    {
        using var fixture = new DecisionFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        await EnableAsync(store);
        await store.AppendAsync(CreateManual(expectedSequence: 0), Epoch);
        var authorityBefore = await store.GetSnapshotAsync();
        var first = LegacyObservation("legacy:first", "decision-first", revision: 1);
        await store.BeginLegacyMirrorAsync("legacy-manifest-first", 1, Epoch);
        await store.StageLegacyMirrorPageAsync("legacy-manifest-first", 0, [first], Epoch);
        await store.CompleteLegacyMirrorAsync("legacy-manifest-first", 1, Epoch);

        var replacement = LegacyObservation("legacy:replacement", "decision-replacement", revision: 2);
        await store.BeginLegacyMirrorAsync("legacy-manifest-replacement", 2, Epoch.AddMinutes(1));
        await store.StageLegacyMirrorPageAsync("legacy-manifest-replacement", 0, [replacement], Epoch.AddMinutes(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CompleteLegacyMirrorAsync("legacy-manifest-replacement", 2, Epoch.AddMinutes(1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.StageLegacyMirrorPageAsync(
                "legacy-manifest-replacement",
                0,
                [replacement with { CanonicalRowHash = HashText("conflicting-row") }],
                Epoch.AddMinutes(1)));

        Assert.Equal("legacy-manifest-first", await store.GetLegacyMirrorManifestIdAsync());
        Assert.Equal(1, fixture.Scalar<int>(fixture.DatabasePath,
            "SELECT COUNT(*) FROM legacy_relationship_decision_mirror WHERE is_present = 1 AND legacy_key = 'decision-first';"));
        Assert.Equal(authorityBefore, await store.GetSnapshotAsync());
    }

    /// <summary>Verifies an empty completed schema-3 decision manifest publishes without a synthetic row.</summary>
    [Fact]
    public async Task EmptyLegacyMirrorManifestCompletesIdempotently()
    {
        using var fixture = new DecisionFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        var manifestId = $"kg-legacy:{HashText(string.Empty).ToLowerInvariant()}";

        await store.BeginLegacyMirrorAsync(manifestId, 0, Epoch);
        await store.StageLegacyMirrorPageAsync(manifestId, 0, [], Epoch);
        await store.StageLegacyMirrorPageAsync(manifestId, 0, [], Epoch);
        await store.CompleteLegacyMirrorAsync(manifestId, 0, Epoch);
        await store.CompleteLegacyMirrorAsync(manifestId, 0, Epoch);

        Assert.Equal(manifestId, await store.GetLegacyMirrorManifestIdAsync());
        Assert.Equal(0, fixture.Scalar<int>(fixture.DatabasePath,
            "SELECT COUNT(*) FROM legacy_relationship_decision_mirror WHERE is_present = 1;"));
    }

    /// <summary>Verifies a verified ordinary recovery point restores a corrupt primary at the same privacy floor.</summary>
    [Fact]
    public async Task VerifiedRecoveryPointRestoresCorruptDecisionLedger()
    {
        using var fixture = new DecisionFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        await EnableAsync(store);
        await store.AppendAsync(CreateManual(expectedSequence: 0), Epoch);
        await store.AppendAsync(
            new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.LinkNodes,
                SubjectId = "node-two",
                TargetId = "node-three",
                ExpectedSequence = 1,
                ExpectedControlSettingsRevision = 1,
            },
            Epoch.AddMinutes(1));
        var recoveryPoint = Assert.Single(
            await store.GetRecoveryPointsAsync(),
            item => item.DecisionSequence == 1);
        fixture.Execute(fixture.DatabasePath,
            "UPDATE decision_recovery_state SET decision_checkpoint_hash = 'CORRUPT' WHERE singleton_id = 1;");

        var restored = await store.RestoreAsync(
            recoveryPoint.RecoveryPointId,
            GraphDecisionRecoveryService.RestoreConfirmation,
            Epoch.AddMinutes(2));

        Assert.True(restored.Succeeded);
        var snapshot = await store.GetSnapshotAsync();
        Assert.True(snapshot.IsValid);
        Assert.Equal(1, snapshot.Sequence);
        Assert.Single(await store.ReadAsync(0, 10));
    }

    /// <summary>Verifies confirmation, missing IDs, and corrupt managed backups fail closed.</summary>
    [Fact]
    public async Task RecoveryRejectsWrongConfirmationMissingAndCorruptBackup()
    {
        using var fixture = new DecisionFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        await EnableAsync(store);
        await store.AppendAsync(CreateManual(expectedSequence: 0), Epoch);
        var point = Assert.Single(await store.GetRecoveryPointsAsync());
        var before = await store.GetSnapshotAsync();

        Assert.False((await store.RestoreAsync(point.RecoveryPointId, "restore", Epoch)).Succeeded);
        Assert.False((await store.RestoreAsync(
            "decision-backup-missing",
            GraphDecisionRecoveryService.RestoreConfirmation,
            Epoch)).Succeeded);
        Assert.Equal(before, await store.GetSnapshotAsync());

        fixture.Execute(fixture.DatabasePath, "PRAGMA user_version = 99;");
        var newer = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(() => store.RestoreAsync(
            point.RecoveryPointId,
            GraphDecisionRecoveryService.RestoreConfirmation,
            Epoch));
        Assert.Equal(SqliteKnowledgeFailureKind.UnsupportedSchema, newer.Kind);
        fixture.Execute(fixture.DatabasePath, "PRAGMA user_version = 1;");

        var backup = Assert.Single(fixture.BackupDatabases());
        using (var stream = new FileStream(backup, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Position = Math.Min(128, stream.Length - 1);
            stream.WriteByte(0xFF);
            stream.Flush(flushToDisk: true);
        }

        var corruptPoint = Assert.Single(await store.GetRecoveryPointsAsync());
        Assert.Equal("corrupt", corruptPoint.StatusCode);
        var exception = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(() => store.RestoreAsync(
            point.RecoveryPointId,
            GraphDecisionRecoveryService.RestoreConfirmation,
            Epoch));
        Assert.Equal(SqliteKnowledgeFailureKind.Corrupt, exception.Kind);

        var foreignId = "decision-backup-foreign";
        var foreignPath = Path.Combine(fixture.BackupDirectory, $"{foreignId}.db");
        fixture.CreateDatabase(
            foreignPath,
            "PRAGMA application_id = 123; PRAGMA user_version = 1; CREATE TABLE foreign_data(value TEXT NOT NULL);");
        var foreignSha = HashFile(foreignPath);
        File.WriteAllText(
            foreignPath + ".manifest.json",
            JsonSerializer.Serialize(new
            {
                backupId = foreignId,
                state = "Committed",
                relativePath = $"{foreignId}.db",
                sha256 = foreignSha,
                byteLength = new FileInfo(foreignPath).Length,
                schemaVersion = 1,
                storeGeneration = 1,
                maximumDecisionSequence = 0,
                privacySequence = 0,
                isPinned = false,
                committedAtUtc = Epoch,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var foreignException = await Assert.ThrowsAsync<SqliteKnowledgeStoreException>(() => store.RestoreAsync(
            foreignId,
            GraphDecisionRecoveryService.RestoreConfirmation,
            Epoch));
        Assert.Equal(SqliteKnowledgeFailureKind.Corrupt, foreignException.Kind);
    }

    /// <summary>Verifies a structurally valid recovery point below the retained privacy floor cannot be restored.</summary>
    [Fact]
    public async Task RecoveryRejectsStalePrivacyPoint()
    {
        using var fixture = new DecisionFixture();
        await using var store = fixture.CreateStore();
        await store.InitializeAsync();
        await EnableAsync(store);
        await store.AppendAsync(CreateManual(expectedSequence: 0), Epoch);
        var staleBackup = Assert.Single(fixture.BackupDatabases());
        var retainedBackup = Path.Combine(fixture.Root, "retained-stale.db");
        var retainedManifest = retainedBackup + ".manifest.json";
        File.Copy(staleBackup, retainedBackup);
        File.Copy(staleBackup + ".manifest.json", retainedManifest);
        var staleId = Path.GetFileNameWithoutExtension(staleBackup);
        await store.AppendAsync(
            new GraphDecisionCommand
            {
                Kind = GraphDecisionKind.Exclude,
                SubjectId = "file-private",
                Reason = "privacy-scope-file",
                ExpectedSequence = 1,
                ExpectedControlSettingsRevision = 1,
            },
            Epoch.AddMinutes(1));
        File.Copy(retainedBackup, Path.Combine(fixture.BackupDirectory, $"{staleId}.db"));
        File.Copy(retainedManifest, Path.Combine(fixture.BackupDirectory, $"{staleId}.db.manifest.json"));

        var stale = Assert.Single(
            await store.GetRecoveryPointsAsync(),
            item => item.RecoveryPointId == staleId);
        Assert.False(stale.IsRestorable);
        Assert.Equal("privacy-floor-stale", stale.StatusCode);
        var rejected = await store.RestoreAsync(
            staleId,
            GraphDecisionRecoveryService.RestoreConfirmation,
            Epoch.AddMinutes(2));
        Assert.False(rejected.Succeeded);
        Assert.Equal(2, (await store.GetSnapshotAsync()).Sequence);
    }

    /// <summary>Verifies initialization completes a durably journaled interrupted restore promotion.</summary>
    [Fact]
    public async Task InterruptedRestorePromotionRecoversOnInitialization()
    {
        using var fixture = new DecisionFixture();
        string recoveryPointId;
        string backupPath;
        await using (var store = fixture.CreateStore())
        {
            await store.InitializeAsync();
            await EnableAsync(store);
            await store.AppendAsync(CreateManual(expectedSequence: 0), Epoch);
            var point = Assert.Single(await store.GetRecoveryPointsAsync());
            recoveryPointId = point.RecoveryPointId;
            backupPath = Assert.Single(fixture.BackupDatabases());
        }

        var stagingName = "knowledge-decisions.db.restore.staging.synthetic";
        var stagingPath = Path.Combine(fixture.Root, stagingName);
        File.Copy(backupPath, stagingPath);
        fixture.Execute(fixture.DatabasePath,
            "UPDATE decision_recovery_state SET decision_checkpoint_hash = 'CORRUPT' WHERE singleton_id = 1;");
        using (var stream = File.OpenRead(stagingPath))
        {
            var checksum = Convert.ToHexString(SHA256.HashData(stream));
            File.WriteAllText(
                Path.Combine(fixture.Root, ".knowledge-decisions.restore.json"),
                JsonSerializer.Serialize(new
                {
                    recoveryPointId,
                    stagingFileName = stagingName,
                    previousFileName = ".knowledge-decisions.restore.previous.db",
                    sha256 = checksum,
                    state = "Promoting",
                    startedAtUtc = Epoch,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }

        await using var recovered = fixture.CreateStore();
        await recovered.InitializeAsync();
        var snapshot = await recovered.GetSnapshotAsync();
        Assert.True(snapshot.IsValid);
        Assert.Equal(1, snapshot.Sequence);
        Assert.False(File.Exists(Path.Combine(fixture.Root, ".knowledge-decisions.restore.json")));
        Assert.False(File.Exists(stagingPath));
    }

    /// <summary>Verifies durable restore copying and hashing observe cancellation between bounded chunks.</summary>
    [Fact]
    public void RestoreFileOperationsObserveChunkCancellationDeterministically()
    {
        var payload = new byte[128 * 1024];
        using (var cancellation = new CancellationTokenSource())
        using (var source = new CancelAfterFirstReadStream(payload, cancellation))
        using (var destination = new MemoryStream())
        {
            Assert.Throws<OperationCanceledException>(() =>
                SqliteGraphDecisionStore.CopyStreamWithCancellation(
                    source,
                    destination,
                    cancellation.Token));
            Assert.Equal(0, destination.Length);
        }

        using (var cancellation = new CancellationTokenSource())
        using (var source = new CancelAfterFirstReadStream(payload, cancellation))
        {
            Assert.Throws<OperationCanceledException>(() =>
                SqliteGraphDecisionStore.HashStreamWithCancellation(source, cancellation.Token));
        }
    }

    private static GraphLegacyDecisionObservation LegacyObservation(string stableKey, string decisionKey, long revision) => new()
    {
        StableKey = stableKey,
        CanonicalRowHash = HashText($"{stableKey}|{decisionKey}|{revision}"),
        Revision = revision,
        ObservedAtUtc = Epoch.AddSeconds(revision),
        DecisionNamespace = "relationship",
        LegacyDecisionKey = decisionKey,
        ActionCode = "confirm",
    };

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private sealed class CancelAfterFirstReadStream : MemoryStream
    {
        private readonly CancellationTokenSource _cancellation;
        private bool _cancelled;

        public CancelAfterFirstReadStream(byte[] payload, CancellationTokenSource cancellation)
            : base(payload, writable: false)
        {
            _cancellation = cancellation;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            if (!_cancelled && read > 0)
            {
                _cancelled = true;
                _cancellation.Cancel();
            }

            return read;
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string LegacyManifestHash(IEnumerable<GraphLegacyDecisionObservation> observations) =>
        HashText(string.Join(
            '\n',
            observations
                .OrderBy(item => item.Kind.ToString(), StringComparer.Ordinal)
                .ThenBy(item => item.StableKey, StringComparer.Ordinal)
                .Select(item => $"{item.Kind}|{item.StableKey}|{item.CanonicalRowHash}")));

    private static GraphDecisionCommand CreateManual(long expectedSequence) => new()
    {
        Kind = GraphDecisionKind.CreateManualEntity,
        SubjectId = "manual:entity-1",
        Label = "Synthetic entity",
        NodeKind = GraphNodeKind.ManualEntity,
        ExpectedSequence = expectedSequence,
        ExpectedControlSettingsRevision = 1,
    };

    private static async Task EnableAsync(SqliteGraphDecisionStore store) =>
        _ = await store.SetControlSettingsAsync(
            new GraphControlSettings
            {
                IsEnabled = true,
                ConsentConfirmed = true,
            },
            expectedRevision: 0,
            Epoch);

    private sealed class DecisionFixture : IDisposable
    {
        internal DecisionFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "opensorse-decision-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "knowledge-decisions.db");
            GraphDatabasePath = Path.Combine(Root, "knowledge-graph.db");
            LifecycleLockPath = Path.Combine(Root, ".knowledge-data.lifecycle.lock");
        }

        internal string Root { get; }
        internal string DatabasePath { get; }
        internal string GraphDatabasePath { get; }
        internal string LifecycleLockPath { get; }
        internal string BackupDirectory => Path.Combine(Root, "backups", "knowledge-decisions");

        internal SqliteGraphDecisionStore CreateStore() => new(DatabasePath);

        internal IReadOnlyList<string> BackupDatabases() => Directory.Exists(BackupDirectory)
            ? Directory.GetFiles(BackupDirectory, "decision-backup-*.db", SearchOption.TopDirectoryOnly)
            : [];

        internal (long Sequence, long Privacy, long Floor, long Generation) ReadRecoveryState()
        {
            using var connection = Open(DatabasePath, readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT current_decision_sequence, current_privacy_sequence, minimum_restorable_privacy_sequence, active_store_generation FROM decision_recovery_state WHERE singleton_id = 1;";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
        }

        internal T Scalar<T>(string path, string sql)
        {
            using var connection = Open(path, readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
        }

        internal void Execute(string path, string sql)
        {
            SqliteConnection.ClearAllPools();
            using var connection = Open(path, readOnly: false);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        internal void CreateDatabase(string path, string sql)
        {
            SqliteConnection.ClearAllPools();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        internal void BlockBackupDirectory()
        {
            Directory.Delete(BackupDirectory, recursive: true);
            File.WriteAllText(BackupDirectory, "synthetic blocker");
        }

        private static SqliteConnection Open(string path, bool readOnly)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            connection.Open();
            return connection;
        }

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
