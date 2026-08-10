using System.Reflection;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Platform;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Indexing.Sqlite.Tests;

/// <summary>Validates immutable schema-3 projection manifests and authority fencing.</summary>
public sealed class SqliteGraphProjectionSourceTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

    /// <summary>A missing deep index produces a valid empty manifest without creating the source database.</summary>
    [Fact]
    public async Task MissingDeepIndexProducesEmptyCompletedManifest()
    {
        using var fixture = new ProjectionFixture();
        await using var source = fixture.CreateProjectionSource();

        var snapshot = await source.OpenCompletedSnapshotAsync();
        var page = await source.ReadPageAsync(snapshot, null, 10);
        var authority = await source.ValidateAuthorityAsync(new GraphAuthorityRequest([], "test-read"));

        Assert.False(File.Exists(fixture.DatabasePath));
        Assert.Equal(0, snapshot.TotalObservationCount);
        Assert.Empty(snapshot.ObservationCounts);
        Assert.True(page.IsLastPage);
        Assert.Empty(page.Observations);
        Assert.True(authority.IsAllowed);
        Assert.Equal(snapshot.ManifestId, authority.CurrentSourceManifestId);
    }

    /// <summary>Disposal fences a queued authority read and observes repeated disposal safely.</summary>
    [Fact]
    public async Task DisposeAsync_FencesQueuedAuthorityReadWithoutUsingDisposedGate()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        var source = fixture.CreateProjectionSource();
        _ = await source.OpenCompletedSnapshotAsync();
        var gate = Assert.IsType<SemaphoreSlim>(
            typeof(SqliteGraphProjectionSource)
                .GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(source));
        await gate.WaitAsync();
        var queued = source.ValidateAuthorityAsync(new GraphAuthorityRequest([], "queued-disposal-read"));

        var disposal = source.DisposeAsync().AsTask();
        gate.Release();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        await source.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => source.OpenCompletedSnapshotAsync());
    }

    /// <summary>All graph-relevant schema-3 rows are captured in deterministic bounded pages.</summary>
    [Fact]
    public async Task CapturesSchemaThreeFactsWithoutAbsolutePathsOrDocumentContent()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();

        var snapshot = await source.OpenCompletedSnapshotAsync();
        var observations = await ReadAllAsync(source, snapshot, pageSize: 2);

        Assert.Equal(snapshot.TotalObservationCount, observations.Count);
        Assert.Equal(snapshot.TotalObservationCount, snapshot.ObservationCounts.Sum(item => item.Count));
        Assert.Contains(observations, item => item is GraphSourceObservation { SourceId: "source-1" });
        Assert.Contains(observations, item => item is GraphFileObservation { FileId: "file-1", SourceId: "source-1" });
        Assert.Contains(observations, item => item is GraphRelationshipObservation { RelationshipId: "relationship-1" });
        Assert.Contains(observations, item => item is GraphCollectionObservation { CollectionId: "collection-1" });
        Assert.Equal(2, observations.OfType<GraphCollectionMembershipObservation>().Count());
        Assert.NotEmpty(observations.OfType<GraphLegacyDecisionObservation>());

        var serialized = string.Join('\n', observations.Select(item => System.Text.Json.JsonSerializer.Serialize(item, item.GetType())));
        Assert.DoesNotContain(fixture.AbsoluteSourcePath, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE-CONTENT-MARKER", serialized, StringComparison.Ordinal);
        var relationship = Assert.Single(observations.OfType<GraphRelationshipObservation>());
        var evidence = Assert.Single(relationship.Evidence);
        Assert.Equal("Same retained invoice identifier.", evidence.Explanation);
    }

    /// <summary>Identical schema-3 content recreates the exact same manifest after adapter restart.</summary>
    [Fact]
    public async Task IdenticalSourceRecreatesDeterministicManifest()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        string firstManifest;
        string firstHash;
        await using (var first = fixture.CreateProjectionSource())
        {
            var snapshot = await first.OpenCompletedSnapshotAsync();
            firstManifest = snapshot.ManifestId;
            firstHash = snapshot.CanonicalManifestHash;
        }

        await using var second = fixture.CreateProjectionSource();
        var recreated = await second.OpenCompletedSnapshotAsync();

        Assert.Equal(firstManifest, recreated.ManifestId);
        Assert.Equal(firstHash, recreated.CanonicalManifestHash);
    }

    /// <summary>Current privacy authority excludes files and every relationship-derived projection that depends on them.</summary>
    [Fact]
    public async Task PrivacyExclusionSuppressesFileRelationshipAndMembership()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: true);
        await using var source = fixture.CreateProjectionSource();

        var snapshot = await source.OpenCompletedSnapshotAsync();
        var observations = await ReadAllAsync(source, snapshot, pageSize: 32);

        Assert.True(Assert.Single(observations.OfType<GraphFileObservation>(), item => item.FileId == "file-2").IsExcluded);
        Assert.True(Assert.Single(observations.OfType<GraphRelationshipObservation>()).IsExcluded);
        Assert.True(Assert.Single(observations.OfType<GraphCollectionMembershipObservation>(), item => item.FileId == "file-2").IsExcluded);
        Assert.True(snapshot.PrivacySequence >= Epoch.UtcTicks);
    }

    /// <summary>A commit after capture fences stale graph reads until a new complete manifest is captured.</summary>
    [Fact]
    public async Task SourceCommitFencesAuthorityUntilReconciled()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        var first = await source.OpenCompletedSnapshotAsync();

        fixture.Execute(
            "UPDATE index_sources SET display_name = 'Renamed source', updated_utc_ticks = $now WHERE id = 'source-1';",
            ("$now", Epoch.AddMinutes(5).UtcTicks));
        var stale = await source.ValidateAuthorityAsync(new GraphAuthorityRequest([], "test-read"));

        Assert.True(stale.IsAvailable);
        Assert.False(stale.IsAllowed);
        Assert.Equal("source-reconciliation-pending", stale.ReasonCode);
        Assert.NotEqual(first.ManifestId, stale.CurrentSourceManifestId);

        var second = await source.OpenCompletedSnapshotAsync();
        var current = await source.ValidateAuthorityAsync(new GraphAuthorityRequest([], "test-read"));
        Assert.NotEqual(first.ManifestId, second.ManifestId);
        Assert.True(current.IsAllowed);
        Assert.Equal(second.ManifestId, current.CurrentSourceManifestId);
    }

    /// <summary>A rename or move retains the file's stable identity while replacing only its path-dependent observation.</summary>
    [Fact]
    public async Task RenameAndMoveRetainStableFileIdentityAndInvalidatePathObservation()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        var (firstSnapshot, firstObservations) = await CaptureAsync(source);
        var firstFile = FileObservation(firstObservations, "file-1");
        var unchangedBefore = ObservationHashesExcept(firstObservations, firstFile.StableKey);

        const string movedPath = "archive/renamed-invoice.pdf";
        fixture.Execute(
            """
            UPDATE index_files
            SET full_path = $full, path_key = $pathKey, relative_path = $relative,
                relative_path_key = $relativeKey, updated_utc_ticks = $updated
            WHERE id = 'file-1';
            """,
            ("$full", Path.Combine(fixture.AbsoluteSourcePath, "archive", "renamed-invoice.pdf")),
            ("$pathKey", "PATH-1-MOVED"),
            ("$relative", movedPath),
            ("$relativeKey", movedPath.ToUpperInvariant()),
            ("$updated", Epoch.AddMinutes(1).UtcTicks));

        var pending = await source.ValidateAuthorityAsync(new GraphAuthorityRequest([], "rename-move"));
        var (secondSnapshot, secondObservations) = await CaptureAsync(source);
        var moved = FileObservation(secondObservations, "file-1");

        Assert.False(pending.IsAllowed);
        Assert.NotEqual(firstSnapshot.ManifestId, secondSnapshot.ManifestId);
        Assert.Equal(firstFile.StableKey, moved.StableKey);
        Assert.Equal(firstFile.FileId, moved.FileId);
        Assert.Equal(firstFile.SourceId, moved.SourceId);
        Assert.Equal(firstFile.ContentHash, moved.ContentHash);
        Assert.NotEqual(firstFile.CanonicalRowHash, moved.CanonicalRowHash);
        Assert.Equal("renamed-invoice.pdf", moved.FileName);
        Assert.Equal(movedPath, moved.RelativePath);
        Assert.Equal("archive", moved.FolderRelativePath);
        Assert.Equal(
            unchangedBefore.OrderBy(item => item.Key, StringComparer.Ordinal),
            ObservationHashesExcept(secondObservations, moved.StableKey)
                .OrderBy(item => item.Key, StringComparer.Ordinal));
    }

    /// <summary>A metadata-only mutation changes the file observation without changing stable or content identity.</summary>
    [Fact]
    public async Task MetadataOnlyChangeRetainsStableAndContentIdentity()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        var (firstSnapshot, firstObservations) = await CaptureAsync(source);
        var firstFile = FileObservation(firstObservations, "file-1");
        var modified = Epoch.AddHours(2);

        fixture.Execute(
            """
            UPDATE index_files
            SET length = 321, modified_utc_ticks = $modified,
                metadata_fingerprint = 'metadata-1-revised', updated_utc_ticks = $updated
            WHERE id = 'file-1';
            """,
            ("$modified", modified.UtcTicks),
            ("$updated", Epoch.AddHours(2).UtcTicks));

        var (secondSnapshot, secondObservations) = await CaptureAsync(source);
        var revised = FileObservation(secondObservations, "file-1");

        Assert.NotEqual(firstSnapshot.ManifestId, secondSnapshot.ManifestId);
        Assert.Equal(firstFile.StableKey, revised.StableKey);
        Assert.Equal(firstFile.ContentHash, revised.ContentHash);
        Assert.NotEqual(firstFile.CanonicalRowHash, revised.CanonicalRowHash);
        Assert.Equal(321, revised.Length);
        Assert.Equal(modified, revised.ModifiedTimeUtc);
        Assert.True(revised.HasBasicMetadata);
    }

    /// <summary>A content change invalidates content-dependent projection input without replacing the stable file identity.</summary>
    [Fact]
    public async Task ContentChangeRetainsFileIdentityAndReplacesContentFingerprint()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        var (firstSnapshot, firstObservations) = await CaptureAsync(source);
        var firstFile = FileObservation(firstObservations, "file-1");
        var replacementHash = new string('C', 64);

        fixture.Execute(
            """
            UPDATE index_files
            SET content_hash = $hash, processor_fingerprint = 'processor-v2',
                updated_utc_ticks = $updated
            WHERE id = 'file-1';
            """,
            ("$hash", replacementHash),
            ("$updated", Epoch.AddHours(3).UtcTicks));

        var (secondSnapshot, secondObservations) = await CaptureAsync(source);
        var revised = FileObservation(secondObservations, "file-1");

        Assert.NotEqual(firstSnapshot.ManifestId, secondSnapshot.ManifestId);
        Assert.Equal(firstFile.StableKey, revised.StableKey);
        Assert.Equal(firstFile.FileId, revised.FileId);
        Assert.NotEqual(firstFile.CanonicalRowHash, revised.CanonicalRowHash);
        Assert.Equal(replacementHash.ToLowerInvariant(), revised.ContentHash);
        Assert.Equal("sha256-v1", revised.ContentHashAlgorithmVersion);
    }

    /// <summary>A retained deletion becomes a deletion observation and suppresses graph inputs that still reference the file.</summary>
    [Fact]
    public async Task DeletedFileBecomesDeletionAndSuppressesDependentObservations()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        var (firstSnapshot, _) = await CaptureAsync(source);
        var deletedAt = Epoch.AddHours(4);

        fixture.Execute(
            "UPDATE index_files SET deleted_utc_ticks = $deleted, updated_utc_ticks = $deleted WHERE id = 'file-1';",
            ("$deleted", deletedAt.UtcTicks));

        var (secondSnapshot, observations) = await CaptureAsync(source);
        var deletion = Assert.Single(
            observations.OfType<GraphDeletionObservation>(),
            item => item.DeletedStableKey == "file:file-1");

        Assert.NotEqual(firstSnapshot.ManifestId, secondSnapshot.ManifestId);
        Assert.Equal(GraphProjectionObservationKind.File, deletion.DeletedKind);
        Assert.Equal("file:file-1", deletion.StableKey);
        Assert.Equal(deletedAt, deletion.ObservedAtUtc);
        Assert.DoesNotContain(observations.OfType<GraphFileObservation>(), item => item.FileId == "file-1");
        Assert.True(Assert.Single(observations.OfType<GraphRelationshipObservation>()).IsExcluded);
        Assert.True(Assert.Single(
            observations.OfType<GraphCollectionMembershipObservation>(),
            item => item.FileId == "file-1").IsExcluded);
    }

    /// <summary>Recreating a retained stable file ID replaces its tombstone without inheriting stale content or path facts.</summary>
    [Fact]
    public async Task RecreatedStableFileIdReplacesTombstoneWithCurrentObservation()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        await CaptureAsync(source);
        var deletedAt = Epoch.AddHours(4);
        fixture.Execute(
            "UPDATE index_files SET deleted_utc_ticks = $deleted, updated_utc_ticks = $deleted WHERE id = 'file-1';",
            ("$deleted", deletedAt.UtcTicks));
        var (deletedSnapshot, deletedObservations) = await CaptureAsync(source);
        var tombstone = Assert.Single(
            deletedObservations.OfType<GraphDeletionObservation>(),
            item => item.DeletedStableKey == "file:file-1");
        var recreatedHash = new string('D', 64);

        fixture.Execute(
            """
            UPDATE index_files
            SET full_path = $full, path_key = 'PATH-1-RECREATED',
                relative_path = 'restored/invoice-v2.pdf', relative_path_key = 'RESTORED/INVOICE-V2.PDF',
                stable_identity = 'stable-1-recreated', length = 444,
                metadata_fingerprint = 'metadata-recreated', content_hash = $hash,
                deleted_utc_ticks = NULL, updated_utc_ticks = $updated
            WHERE id = 'file-1';
            """,
            ("$full", Path.Combine(fixture.AbsoluteSourcePath, "restored", "invoice-v2.pdf")),
            ("$hash", recreatedHash),
            ("$updated", Epoch.AddHours(5).UtcTicks));

        var (recreatedSnapshot, recreatedObservations) = await CaptureAsync(source);
        var recreated = FileObservation(recreatedObservations, "file-1");

        Assert.NotEqual(deletedSnapshot.ManifestId, recreatedSnapshot.ManifestId);
        Assert.Equal(tombstone.StableKey, recreated.StableKey);
        Assert.NotEqual(tombstone.CanonicalRowHash, recreated.CanonicalRowHash);
        Assert.Equal("restored/invoice-v2.pdf", recreated.RelativePath);
        Assert.Equal("restored", recreated.FolderRelativePath);
        Assert.Equal(444, recreated.Length);
        Assert.Equal(recreatedHash.ToLowerInvariant(), recreated.ContentHash);
        Assert.DoesNotContain(
            recreatedObservations.OfType<GraphDeletionObservation>(),
            item => item.DeletedStableKey == recreated.StableKey);
        Assert.False(Assert.Single(recreatedObservations.OfType<GraphRelationshipObservation>()).IsExcluded);
    }

    /// <summary>Equal update timestamps remain hints only; canonical row changes still create a new completed manifest.</summary>
    [Fact]
    public async Task UpdatedTimeCollisionStillProducesChangedCanonicalManifest()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        var (firstSnapshot, firstObservations) = await CaptureAsync(source);
        var firstFile = FileObservation(firstObservations, "file-1");

        fixture.Execute(
            """
            UPDATE index_files
            SET length = 777, metadata_fingerprint = 'same-timestamp-different-metadata',
                content_hash = $hash
            WHERE id = 'file-1';
            """,
            ("$hash", new string('E', 64)));

        var pending = await source.ValidateAuthorityAsync(new GraphAuthorityRequest([], "timestamp-collision"));
        var (secondSnapshot, secondObservations) = await CaptureAsync(source);
        var changed = FileObservation(secondObservations, "file-1");

        Assert.False(pending.IsAllowed);
        Assert.Equal(firstFile.Revision, changed.Revision);
        Assert.NotEqual(firstFile.CanonicalRowHash, changed.CanonicalRowHash);
        Assert.NotEqual(firstSnapshot.CanonicalManifestHash, secondSnapshot.CanonicalManifestHash);
        Assert.NotEqual(firstSnapshot.ManifestId, secondSnapshot.ManifestId);
        Assert.Equal(777, changed.Length);
    }

    /// <summary>A completed replacement manifest proves physical absence without fabricating a source tombstone.</summary>
    [Fact]
    public async Task CompletedManifestProvesPhysicalRowAbsenceWithoutSyntheticDeletion()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        var (firstSnapshot, firstObservations) = await CaptureAsync(source);
        Assert.Contains(firstObservations.OfType<GraphFileObservation>(), item => item.FileId == "file-2");

        fixture.Execute(
            """
            DELETE FROM index_relationship_evidence WHERE relationship_id = 'relationship-1';
            DELETE FROM relationship_pair_overrides WHERE first_file_id = 'file-1' AND second_file_id = 'file-2';
            DELETE FROM smart_collection_member_overrides WHERE file_id = 'file-2';
            DELETE FROM smart_collection_members WHERE file_id = 'file-2';
            DELETE FROM index_relationships WHERE id = 'relationship-1';
            DELETE FROM index_files WHERE id = 'file-2';
            """);

        var pending = await source.ValidateAuthorityAsync(new GraphAuthorityRequest([], "physical-delete"));
        var (secondSnapshot, observations) = await CaptureAsync(source);

        Assert.False(pending.IsAllowed);
        Assert.NotEqual(firstSnapshot.ManifestId, secondSnapshot.ManifestId);
        Assert.True(secondSnapshot.TotalObservationCount < firstSnapshot.TotalObservationCount);
        Assert.Equal(secondSnapshot.TotalObservationCount, observations.Count);
        Assert.DoesNotContain(observations.OfType<GraphFileObservation>(), item => item.FileId == "file-2");
        Assert.DoesNotContain(observations.OfType<GraphDeletionObservation>(), item => item.DeletedStableKey == "file:file-2");
        Assert.DoesNotContain(observations.OfType<GraphRelationshipObservation>(), item => item.RelationshipId == "relationship-1");
        Assert.DoesNotContain(
            observations.OfType<GraphCollectionMembershipObservation>(),
            item => item.FileId == "file-2");
    }

    /// <summary>Capturing and reconciling graph manifests never changes source-file bytes, timestamps, or attributes.</summary>
    [Fact]
    public async Task SnapshotCaptureAndIncrementalChangesLeaveOriginalSourceFileUntouched()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        var originalPath = Path.Combine(fixture.AbsoluteSourcePath, "records", "invoice.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
        var originalBytes = "SYNTHETIC-SOURCE-BYTES"u8.ToArray();
        await File.WriteAllBytesAsync(originalPath, originalBytes);
        File.SetLastWriteTimeUtc(originalPath, Epoch.UtcDateTime);
        var originalWriteTime = File.GetLastWriteTimeUtc(originalPath);
        var originalAttributes = File.GetAttributes(originalPath);
        await using var source = fixture.CreateProjectionSource();

        await CaptureAsync(source);
        fixture.Execute(
            """
            UPDATE index_files
            SET relative_path = 'archive/invoice.pdf', relative_path_key = 'ARCHIVE/INVOICE.PDF',
                metadata_fingerprint = 'metadata-reconciled', updated_utc_ticks = $updated
            WHERE id = 'file-1';
            """,
            ("$updated", Epoch.AddHours(6).UtcTicks));
        await CaptureAsync(source);

        Assert.True(File.Exists(originalPath));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(originalPath));
        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(originalPath));
        Assert.Equal(originalAttributes, File.GetAttributes(originalPath));
        Assert.False(File.Exists(Path.Combine(fixture.AbsoluteSourcePath, "archive", "invoice.pdf")));
    }

    /// <summary>A source database replaced behind an open read connection is detected instead of serving a stale inode.</summary>
    [Fact]
    public async Task ReplacedDeepIndexIsDetectedAcrossPersistentAuthorityConnection()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        var first = await source.OpenCompletedSnapshotAsync();

        if (OperatingSystem.IsWindows())
        {
            File.SetLastWriteTimeUtc(fixture.DatabasePath, DateTime.UtcNow.AddSeconds(2));
        }
        else
        {
            fixture.ReplaceDatabase("Replacement source");
        }

        var stale = await source.ValidateAuthorityAsync(new GraphAuthorityRequest([], "replacement-check"));
        Assert.True(stale.IsAvailable);
        Assert.False(stale.IsAllowed);

        var second = await source.OpenCompletedSnapshotAsync();
        Assert.True((!OperatingSystem.IsWindows() && second.ManifestId != first.ManifestId) ||
                    (OperatingSystem.IsWindows() && second.ManifestId == first.ManifestId));
        var current = await source.ValidateAuthorityAsync(new GraphAuthorityRequest([], "replacement-check"));
        Assert.True(current.IsAllowed);
        Assert.Equal(second.ManifestId, current.CurrentSourceManifestId);
    }

    /// <summary>Absolute-looking source labels and long relative paths expose only bounded non-root display names.</summary>
    [Fact]
    public async Task SnapshotRedactsAbsoluteSourceLabelsAndPreservesLongPathFileName()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        var longRelativePath = string.Concat("records/", new string('x', 600), "/report.pdf");
        fixture.Execute(
            "UPDATE index_sources SET display_name = $label WHERE id = 'source-1'; UPDATE index_files SET relative_path = $path, relative_path_key = $key WHERE id = 'file-1';",
            ("$label", Path.Combine(fixture.AbsoluteSourcePath, "Confidential")),
            ("$path", longRelativePath),
            ("$key", longRelativePath.ToUpperInvariant()));
        await using var source = fixture.CreateProjectionSource();

        var snapshot = await source.OpenCompletedSnapshotAsync();
        var observations = await ReadAllAsync(source, snapshot, pageSize: 32);

        Assert.Equal("Confidential", Assert.Single(observations.OfType<GraphSourceObservation>()).DisplayName);
        var file = Assert.Single(observations.OfType<GraphFileObservation>(), item => item.FileId == "file-1");
        Assert.Equal("report.pdf", file.FileName);
        Assert.StartsWith("long-path/", file.RelativePath, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.AbsoluteSourcePath, file.DisplayLabelOrEmpty(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Malformed and replayed cursors cannot escape the stable bounded page contract.</summary>
    [Fact]
    public async Task ContinuationCursorIsOpaqueValidatedAndDeterministic()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        var snapshot = await source.OpenCompletedSnapshotAsync();
        var first = await source.ReadPageAsync(snapshot, null, 2);

        var second = await source.ReadPageAsync(snapshot, Assert.IsType<GraphProjectionCursor>(first.NextCursor), 2);
        var replay = await source.ReadPageAsync(snapshot, first.NextCursor, 2);

        Assert.Equal(0, first.PageSequence);
        Assert.Equal(1, second.PageSequence);
        Assert.Equal(second.PageSequence, replay.PageSequence);
        Assert.Equal(second.CanonicalPageHash, replay.CanonicalPageHash);
        Assert.Equal(second.NextCursor, replay.NextCursor);
        Assert.Equal(
            second.Observations.Select(item => (item.Kind, item.StableKey, item.CanonicalRowHash)),
            replay.Observations.Select(item => (item.Kind, item.StableKey, item.CanonicalRowHash)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => source.ReadPageAsync(snapshot, new GraphProjectionCursor("not-a-cursor"), 2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => source.ReadPageAsync(snapshot, null, 0));
    }

    /// <summary>A newer source schema fails closed without modifying the database marker.</summary>
    [Fact]
    public async Task UnsupportedNewerDeepIndexSchemaFailsClosed()
    {
        using var fixture = new ProjectionFixture();
        fixture.Execute("PRAGMA user_version = 99;");
        await using var source = fixture.CreateProjectionSource();

        var exception = await Assert.ThrowsAsync<GraphPersistenceException>(() => source.OpenCompletedSnapshotAsync());

        Assert.Equal("source-schema-newer", exception.ReasonCode);
        Assert.Equal(99, fixture.ReadUserVersion());
    }

    /// <summary>Cancellation is observed before snapshot enumeration publishes a manifest.</summary>
    [Fact]
    public async Task SnapshotCaptureHonorsCancellation()
    {
        using var fixture = new ProjectionFixture();
        await fixture.InitializeAndSeedAsync(includePrivacyExclusion: false);
        await using var source = fixture.CreateProjectionSource();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.OpenCompletedSnapshotAsync(cancellation.Token));
    }

    private static async Task<IReadOnlyList<GraphProjectionObservation>> ReadAllAsync(
        IGraphProjectionSource source,
        GraphProjectionSnapshot snapshot,
        int pageSize)
    {
        var observations = new List<GraphProjectionObservation>();
        GraphProjectionCursor? cursor = null;
        do
        {
            var page = await source.ReadPageAsync(snapshot, cursor, pageSize);
            observations.AddRange(page.Observations);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return observations;
    }

    private static async Task<(GraphProjectionSnapshot Snapshot, IReadOnlyList<GraphProjectionObservation> Observations)>
        CaptureAsync(IGraphProjectionSource source)
    {
        var snapshot = await source.OpenCompletedSnapshotAsync();
        return (snapshot, await ReadAllAsync(source, snapshot, GraphLimits.MaximumPageSize));
    }

    private static GraphFileObservation FileObservation(
        IReadOnlyList<GraphProjectionObservation> observations,
        string fileId) =>
        Assert.Single(observations.OfType<GraphFileObservation>(), item => item.FileId == fileId);

    private static IReadOnlyDictionary<string, string> ObservationHashesExcept(
        IReadOnlyList<GraphProjectionObservation> observations,
        string excludedStableKey) => observations
        .Where(item => !string.Equals(item.StableKey, excludedStableKey, StringComparison.Ordinal))
        .ToDictionary(
            item => string.Concat(item.Kind.ToString(), "|", item.StableKey),
            item => item.CanonicalRowHash,
            StringComparer.Ordinal);

    private sealed class ProjectionFixture : IDisposable
    {
        internal ProjectionFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "opensorse-graph-source-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            DatabasePath = Path.Combine(Root, "deep-index.db");
            AbsoluteSourcePath = Path.Combine(Root, "private-source");
        }

        internal string Root { get; }
        internal string DatabasePath { get; }
        internal string AbsoluteSourcePath { get; }

        internal SqliteGraphProjectionSource CreateProjectionSource() =>
            new(DatabasePath, new WindowsPathSemantics());

        internal async Task InitializeAndSeedAsync(bool includePrivacyExclusion)
        {
            await using (var store = new SqliteDeepIndexStore(DatabasePath, new WindowsPathSemantics()))
            {
                await store.InitializeAsync();
            }

            Execute(
                """
                INSERT INTO index_sources(
                    id, root_path, root_path_key, display_name, indexing_level,
                    include_subfolders, enabled, priority, exclusions_json,
                    managed_by_watched_folders, created_utc_ticks, updated_utc_ticks)
                VALUES ('source-1', $root, $rootKey, 'Private records', 2, 1, 1, 0, '[]', 0, $now, $now);

                INSERT INTO index_files(
                    id, source_id, full_path, path_key, relative_path, relative_path_key,
                    stable_identity, file_system_id, length, creation_utc_ticks,
                    modified_utc_ticks, attributes, metadata_fingerprint, content_hash,
                    processor_fingerprint, indexing_level, fully_indexed, deleted_utc_ticks,
                    last_seen_run_id, updated_utc_ticks)
                VALUES
                    ('file-1', 'source-1', $full1, 'PATH-1', 'records/invoice.pdf', 'RECORDS/INVOICE.PDF',
                     'stable-1', 'fs-1', 100, $now, $now, 0, 'metadata-1', $hash1,
                     'processor-v1', 2, 1, NULL, NULL, $now),
                    ('file-2', 'source-1', $full2, 'PATH-2', 'records/receipt.pdf', 'RECORDS/RECEIPT.PDF',
                     'stable-2', 'fs-1', 200, $now, $now, 0, 'metadata-2', $hash2,
                     'processor-v1', 2, 1, NULL, NULL, $now);

                INSERT INTO index_content(
                    content_hash, extracted_text, ocr_text, summary, keywords_json,
                    semantic_json, coverage_level, processor_fingerprint, updated_utc_ticks)
                VALUES ($hash1, 'PRIVATE-CONTENT-MARKER', NULL, NULL, '[]', NULL, 1, 'processor-v1', $now);

                INSERT INTO index_relationships(
                    id, first_file_id, second_file_id, relationship_type, custom_type,
                    confidence, algorithm, algorithm_version, created_utc_ticks,
                    validated_utc_ticks, decision, is_manual, context_key)
                VALUES ('relationship-1', 'file-1', 'file-2', 2, NULL, 2,
                        'relationship-engine', '1.0.0', $now, $now, 1, 0, 'invoice-set');
                INSERT INTO index_relationship_evidence(
                    relationship_id, ordinal, evidence_kind, evidence_key, explanation)
                VALUES ('relationship-1', 0, 1, 'invoice-identifier', 'Same retained invoice identifier.');

                INSERT INTO relationship_pair_overrides(
                    first_file_id, second_file_id, decision, relationship_type,
                    custom_type, changed_utc_ticks)
                VALUES ('file-1', 'file-2', 1, 2, NULL, $now);

                INSERT INTO smart_collections(
                    id, context_key, title, description, relationship_summary,
                    context_type, confidence, creation_source, is_pinned,
                    is_user_renamed, created_utc_ticks, updated_utc_ticks)
                VALUES ('collection-1', 'invoice-set', 'Invoices', 'Synthetic records',
                        'Evidence-backed invoice set', 2, 2, 0, 1, 1, $now, $now);
                INSERT INTO smart_collection_members(
                    collection_id, file_id, membership_source, relationship_id, added_utc_ticks)
                VALUES
                    ('collection-1', 'file-1', 0, 'relationship-1', $now),
                    ('collection-1', 'file-2', 1, 'relationship-1', $now);
                INSERT INTO smart_collection_member_overrides(
                    collection_id, file_id, excluded, changed_utc_ticks)
                VALUES ('collection-1', 'file-1', 0, $now);
                """,
                ("$root", AbsoluteSourcePath),
                ("$rootKey", AbsoluteSourcePath.ToUpperInvariant()),
                ("$full1", Path.Combine(AbsoluteSourcePath, "records", "invoice.pdf")),
                ("$full2", Path.Combine(AbsoluteSourcePath, "records", "receipt.pdf")),
                ("$hash1", new string('A', 64)),
                ("$hash2", new string('B', 64)),
                ("$now", Epoch.UtcTicks));

            if (includePrivacyExclusion)
            {
                Execute(
                    """
                    INSERT INTO index_privacy_rules(
                        source_id, relative_path_key, relative_path, is_excluded,
                        indexing_level_override, suppress_ocr, suppress_summary,
                        suppress_semantic, repair_stage, force_reprocess,
                        updated_utc_ticks, suppress_relationships)
                    VALUES ('source-1', 'RECORDS/RECEIPT.PDF', 'records/receipt.pdf', 1,
                            NULL, 0, 0, 0, NULL, 0, $now, 1);
                    """,
                    ("$now", Epoch.AddMinutes(1).UtcTicks));
            }
        }

        internal void Execute(string sql, params (string Name, object? Value)[] parameters)
        {
            SqliteConnection.ClearAllPools();
            using var connection = new SqliteConnection($"Data Source={DatabasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            command.ExecuteNonQuery();
        }

        internal int ReadUserVersion()
        {
            using var connection = new SqliteConnection($"Data Source={DatabasePath};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }

        internal void ReplaceDatabase(string replacementDisplayName)
        {
            var replacement = Path.Combine(Root, "deep-index-replacement.db");
            File.Copy(DatabasePath, replacement, overwrite: true);
            using (var connection = new SqliteConnection($"Data Source={replacement}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE index_sources SET display_name = $name, updated_utc_ticks = $now WHERE id = 'source-1';";
                command.Parameters.AddWithValue("$name", replacementDisplayName);
                command.Parameters.AddWithValue("$now", Epoch.AddMinutes(10).UtcTicks);
                command.ExecuteNonQuery();
            }

            File.Move(replacement, DatabasePath, overwrite: true);
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

internal static class GraphFileObservationTestExtensions
{
    internal static string DisplayLabelOrEmpty(this GraphFileObservation observation) =>
        string.Concat(observation.FileName, "|", observation.RelativePath, "|", observation.FolderRelativePath);
}
