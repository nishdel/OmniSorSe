using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Resilience;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Application.Watching;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Indexing.Sqlite.Tests;

/// <summary>Exercises logical user-state export and reviewed restore against real durable stores.</summary>
public sealed class StateBackupServiceTests
{
    /// <summary>Verifies supported authored state round-trips into an empty schema-6 profile.</summary>
    [Fact]
    public async Task ExportRestore_EmptyProfile_RoundTripsSupportedState()
    {
        await using var source = await BackupFixture.CreateAsync();
        await using var destination = await BackupFixture.CreateAsync();
        await source.SeedUserStateAsync();
        var archive = source.PathFor("roundtrip.oms-state");

        await source.Service.ExportAsync(archive);
        var preview = await destination.Service.PreviewRestoreAsync(archive);
        var restored = await destination.Service.RestoreAsync(
            archive,
            preview.Fingerprint,
            StateRestoreMode.Replace,
            new StateRestoreSelection());

        Assert.True(restored.Applied);
        Assert.Equal(7, restored.RestoredCategoryCount);
        Assert.True(File.Exists(restored.PreRestoreBackupPath));
        Assert.True(destination.Configuration.Current.Features.ShowAdvancedFeatures);
        Assert.Contains(await destination.Store.GetSourcesAsync(), item => item.Id == "source:user");
        Assert.Contains(await destination.SavedViews.ListAsync(), item => item.Id == "view:user");
        var workflow = await destination.Workflows.LoadAsync(CancellationToken.None);
        Assert.Contains(workflow.Recipes, item => item.Id == "recipe:user");
    }

    /// <summary>Verifies the default archive contains no derived document/index payload categories.</summary>
    [Fact]
    public async Task Export_DefaultArchive_ContainsOnlyFixedLogicalEntries()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        await fixture.SeedUserStateAsync();
        fixture.Execute(
            $"""
            INSERT INTO smart_collections(
                id, context_key, title, description, relationship_summary, context_type,
                confidence, creation_source, is_pinned, is_user_renamed, created_utc_ticks, updated_utc_ticks)
            VALUES('collection:auto-export', 'topic:auto-export', 'Generated title',
                   'DERIVED-COLLECTION-DESCRIPTION', 'DERIVED-COLLECTION-EVIDENCE',
                   {(int)RelationshipType.SameTopic}, {(int)RelationshipConfidence.Medium},
                   {(int)SmartCollectionCreationSource.Automatic}, 1, 0, 0, 0);
            """);
        var archivePath = fixture.PathFor("bounded.oms-state");

        await fixture.Service.ExportAsync(archivePath);

        using var archive = ZipFile.OpenRead(archivePath);
        Assert.Equal(["manifest.json", "state.json"], archive.Entries.Select(item => item.FullName).OrderBy(value => value));
        using var reader = new StreamReader(archive.GetEntry("state.json")!.Open(), Encoding.UTF8);
        var state = await reader.ReadToEndAsync();
        using var document = JsonDocument.Parse(state);
        Assert.Equal(
            ["profiles", "recipes", "relationshipAuthority", "savedViews", "settings", "smartCollectionAuthority", "smartTagAuthority", "sources", "watchedFolders"],
            document.RootElement.EnumerateObject().Select(item => item.Name).OrderBy(value => value));
        Assert.DoesNotContain("DERIVED-COLLECTION", state, StringComparison.Ordinal);
    }

    /// <summary>Verifies an exact format-1 logical payload remains readable after the format-2 writer is introduced.</summary>
    [Fact]
    public async Task Preview_FormatOneBackup_RemainsSupported()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        await fixture.SeedUserStateAsync();
        var current = fixture.PathFor("current.oms-state");
        var legacy = fixture.PathFor("legacy-v1.oms-state");
        await fixture.Service.ExportAsync(current);

        JsonObject manifest;
        JsonObject state;
        using (var archive = ZipFile.OpenRead(current))
        {
            manifest = JsonNode.Parse(await ReadEntryTextAsync(archive, "manifest.json"))!.AsObject();
            state = JsonNode.Parse(await ReadEntryTextAsync(archive, "state.json"))!.AsObject();
        }

        Assert.True(state.Remove("smartCollectionAuthority"));
        var stateJson = state.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        manifest["formatVersion"] = 1;
        manifest["stateSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson)));
        using (var archive = ZipFile.Open(legacy, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "manifest.json", manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            await WriteEntryAsync(archive, "state.json", stateJson);
        }

        var preview = await fixture.Service.PreviewRestoreAsync(legacy);

        Assert.Equal("1", preview.BackupVersion);
        Assert.Equal(0, preview.SmartCollectionAuthorityCount);

        fixture.Execute(
            $"""
            INSERT INTO smart_collections(
                id, context_key, title, description, relationship_summary, context_type,
                confidence, creation_source, is_pinned, is_user_renamed, created_utc_ticks, updated_utc_ticks)
            VALUES('collection:current', NULL, 'Current authority', 'Current', 'Current',
                   {(int)RelationshipType.SameProject}, {(int)RelationshipConfidence.Confirmed},
                   {(int)SmartCollectionCreationSource.Manual}, 1, 1, 0, 0);
            """);
        var restored = await fixture.Service.RestoreAsync(
            legacy,
            preview.Fingerprint,
            StateRestoreMode.Replace,
            new StateRestoreSelection());

        Assert.True(restored.Applied);
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM smart_collections WHERE id = 'collection:current';"));
    }

    /// <summary>Verifies archive entries cannot escape the fixed, non-extracting format.</summary>
    [Fact]
    public async Task Preview_ArchiveTraversalEntry_IsRejected()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        var archivePath = fixture.PathFor("traversal.oms-state");
        await fixture.Service.ExportAsync(archivePath);
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
        {
            var entry = archive.CreateEntry("../escape.txt");
            await using var target = entry.Open();
            await target.WriteAsync("hostile"u8.ToArray());
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.PreviewRestoreAsync(archivePath));
        Assert.False(File.Exists(fixture.PathFor("escape.txt")));
    }

    /// <summary>Verifies unsupported future backup formats fail before state deserialization or application.</summary>
    [Fact]
    public async Task Preview_FutureFormat_IsRejected()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        var archivePath = fixture.PathFor("future.oms-state");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(
                archive,
                "manifest.json",
                "{\"formatVersion\":99,\"applicationVersion\":\"future\",\"sourceRevision\":\"future\",\"buildConfiguration\":\"Release\",\"schemaVersion\":6,\"createdAtUtc\":\"2026-01-01T00:00:00Z\",\"stateSha256\":\"00\"}");
            await WriteEntryAsync(archive, "state.json", "{}");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.PreviewRestoreAsync(archivePath));
    }

    /// <summary>Verifies a truncated container is rejected without touching current state.</summary>
    [Fact]
    public async Task Preview_TruncatedArchive_IsRejected()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        var archivePath = fixture.PathFor("truncated.oms-state");
        await File.WriteAllBytesAsync(archivePath, "PK\u0003\u0004truncated"u8.ToArray());

        await Assert.ThrowsAnyAsync<InvalidDataException>(() => fixture.Service.PreviewRestoreAsync(archivePath));
        Assert.False(fixture.Configuration.Current.Features.ShowAdvancedFeatures);
    }

    /// <summary>Verifies duplicate fixed entry names are rejected before payload parsing.</summary>
    [Fact]
    public async Task Preview_DuplicateEntry_IsRejected()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        var archivePath = fixture.PathFor("duplicate.oms-state");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "manifest.json", "{}");
            await WriteEntryAsync(archive, "state.json", "{}");
            await WriteEntryAsync(archive, "state.json", "{}");
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.PreviewRestoreAsync(archivePath));
    }

    /// <summary>Verifies backup restores explicit authority only when the exact indexed identity still exists.</summary>
    [Fact]
    public async Task Restore_ExistingExactFileIdentity_RoundTripsUserTagAuthority()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        await fixture.SeedUserStateAsync();
        var (fileId, tagId) = await fixture.SeedIndexedUserTagAsync();
        var archive = fixture.PathFor("authority.oms-state");
        await fixture.Service.ExportAsync(archive);
        await fixture.Store.RemoveTagAsync(fileId, tagId, DateTimeOffset.UtcNow);
        Assert.DoesNotContain(await fixture.Store.GetFileSmartTagsAsync(fileId), item => item.Definition.TagId == tagId);

        var preview = await fixture.Service.PreviewRestoreAsync(archive);
        var restored = await fixture.Service.RestoreAsync(
            archive,
            preview.Fingerprint,
            StateRestoreMode.Merge,
            new StateRestoreSelection());

        Assert.Equal(1, restored.RestoredSmartTagAuthorityCount);
        Assert.Contains(await fixture.Store.GetFileSmartTagsAsync(fileId), item => item.Definition.TagId == tagId);
    }

    /// <summary>Verifies a user-created relationship round-trips only through exact active file identities.</summary>
    [Fact]
    public async Task Restore_ExistingExactFilePair_RoundTripsRelationshipAuthority()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        await fixture.SeedUserStateAsync();
        var (first, second) = await fixture.SeedIndexedFilePairAsync();
        await fixture.Store.LinkFilesAsync(
            first,
            second,
            RelationshipType.SameProject,
            null,
            true,
            DateTimeOffset.UnixEpoch);
        var authority = await fixture.Store.ExportRelationshipUserAuthorityAsync(100);
        Assert.Single(authority);
        var archive = fixture.PathFor("relationship-authority.oms-state");
        await fixture.Service.ExportAsync(archive);
        await fixture.Store.RemoveRelationshipUserAuthorityAsync(authority);
        Assert.Empty(await fixture.Store.ExportRelationshipUserAuthorityAsync(100));

        var preview = await fixture.Service.PreviewRestoreAsync(archive);
        var restored = await fixture.Service.RestoreAsync(
            archive,
            preview.Fingerprint,
            StateRestoreMode.Merge,
            new StateRestoreSelection());

        Assert.Equal(1, restored.RestoredRelationshipAuthorityCount);
        var restoredAuthority = Assert.Single(await fixture.Store.ExportRelationshipUserAuthorityAsync(100));
        Assert.Equal((first, second), (restoredAuthority.FirstFileId, restoredAuthority.SecondFileId));
        Assert.True(restoredAuthority.IsManualRelationship);
        Assert.Equal(RelationshipDecision.AlwaysRelate, restoredAuthority.Decision);
    }

    /// <summary>Verifies format 2 restores authored Smart Collection state but not generated relationship edges.</summary>
    [Fact]
    public async Task Restore_FormatTwo_RoundTripsSmartCollectionAuthority()
    {
        await using var fixture = await BackupFixture.CreateAsync();
        await fixture.SeedUserStateAsync();
        var (first, second) = await fixture.SeedIndexedFilePairAsync();
        await fixture.Store.LinkFilesAsync(
            first,
            second,
            RelationshipType.SameProject,
            null,
            true,
            DateTimeOffset.UnixEpoch);
        fixture.Execute(
            $"""
            INSERT INTO smart_collections(
                id, context_key, title, description, relationship_summary, context_type,
                confidence, creation_source, is_pinned, is_user_renamed, created_utc_ticks, updated_utc_ticks)
            VALUES('collection:user', NULL, 'My Project', 'User collection', 'Manual context',
                   {(int)RelationshipType.SameProject}, {(int)RelationshipConfidence.Confirmed},
                   {(int)SmartCollectionCreationSource.Merged}, 1, 1, 0, 0);
            INSERT INTO smart_collection_members(collection_id, file_id, membership_source, relationship_id, added_utc_ticks)
            VALUES('collection:user', '{first}', {(int)CollectionMembershipSource.Manual}, NULL, 0);
            INSERT INTO smart_collection_member_overrides(collection_id, file_id, excluded, changed_utc_ticks)
            VALUES('collection:user', '{second}', 1, 0);
            INSERT INTO forgotten_smart_collections(context_key, forgotten_utc_ticks)
            VALUES('project:forgotten-context', 0);
            """);
        var archive = fixture.PathFor("collection-authority.oms-state");
        await fixture.Service.ExportAsync(archive);
        fixture.Execute("DELETE FROM smart_collections WHERE id = 'collection:user';");
        fixture.Execute("DELETE FROM forgotten_smart_collections;");

        var preview = await fixture.Service.PreviewRestoreAsync(archive);
        var restored = await fixture.Service.RestoreAsync(
            archive,
            preview.Fingerprint,
            StateRestoreMode.Merge,
            new StateRestoreSelection());

        Assert.True(preview.SmartCollectionAuthorityCount > 0);
        Assert.True(restored.RestoredSmartCollectionAuthorityCount >= 3);
        var collection = Assert.IsType<SmartCollectionDetails>(
            await fixture.Store.GetCollectionAsync("collection:user", 10));
        Assert.True(collection.Collection.IsPinned);
        Assert.Equal(SmartCollectionCreationSource.Merged, collection.Collection.CreationSource);
        Assert.Contains(collection.Members, item => item.FileId == first && item.MembershipSource == CollectionMembershipSource.Manual);
        Assert.DoesNotContain(collection.Members, item => item.FileId == second);
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM forgotten_smart_collections WHERE context_key = 'project:forgotten-context';"));
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM relationship_pair_overrides;"));
        Assert.Equal("1", fixture.Scalar("SELECT COUNT(*) FROM index_relationships WHERE is_manual = 1;"));
    }

    /// <summary>Verifies a mid-restore write failure rolls prior categories back and retains a recovery point.</summary>
    [Fact]
    public async Task Restore_MidApplyFailure_RollsBackCurrentState()
    {
        await using var source = await BackupFixture.CreateAsync();
        await source.SeedUserStateAsync();
        var archive = source.PathFor("restore-source.oms-state");
        await source.Service.ExportAsync(archive);
        await using var destination = await BackupFixture.CreateAsync(new FailOnceBeforeCategory("saved-views"));
        await destination.Configuration.SaveAsync(
            new ApplicationSettings { Features = new FeatureSettings { ShowAdvancedFeatures = false } },
            CancellationToken.None);
        var preview = await destination.Service.PreviewRestoreAsync(archive);

        await Assert.ThrowsAsync<IOException>(() => destination.Service.RestoreAsync(
            archive,
            preview.Fingerprint,
            StateRestoreMode.Replace,
            new StateRestoreSelection()));

        Assert.False(destination.Configuration.Current.Features.ShowAdvancedFeatures);
        Assert.NotEmpty(Directory.EnumerateFiles(
            Path.Combine(destination.Paths.Paths.StateDirectory, "state-backups"),
            "pre-restore-*.oms-state"));
    }

    /// <summary>Verifies cancellation is honored before a reviewed restore mutates any category.</summary>
    [Fact]
    public async Task Restore_PreCancelled_DoesNotApplyState()
    {
        await using var source = await BackupFixture.CreateAsync();
        await source.SeedUserStateAsync();
        var archive = source.PathFor("cancelled.oms-state");
        await source.Service.ExportAsync(archive);
        await using var destination = await BackupFixture.CreateAsync();
        var preview = await destination.Service.PreviewRestoreAsync(archive);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => destination.Service.RestoreAsync(
            archive,
            preview.Fingerprint,
            StateRestoreMode.Replace,
            new StateRestoreSelection(),
            cancellation.Token));

        Assert.False(destination.Configuration.Current.Features.ShowAdvancedFeatures);
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(value));
    }

    private static async Task<string> ReadEntryTextAsync(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private sealed class FailOnceBeforeCategory(string category) : IStateRestoreFaultInjector
    {
        private int _remaining = 1;

        public Task BeforeCategoryAsync(string candidate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(candidate, category, StringComparison.Ordinal) && Interlocked.Exchange(ref _remaining, 0) == 1)
            {
                throw new IOException("Injected atomic-state write failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BackupFixture : IAsyncDisposable
    {
        private readonly string _root;

        private BackupFixture(
            string root,
            ApplicationPathProvider paths,
            JsonConfigurationService configuration,
            SqliteDeepIndexStore store,
            JsonSavedDiscoveryViewStore savedViews,
            JsonWorkflowLibraryStore workflows,
            StateBackupService service)
        {
            _root = root;
            Paths = paths;
            Configuration = configuration;
            Store = store;
            SavedViews = savedViews;
            Workflows = workflows;
            Service = service;
        }

        public ApplicationPathProvider Paths { get; }
        public JsonConfigurationService Configuration { get; }
        public SqliteDeepIndexStore Store { get; }
        public JsonSavedDiscoveryViewStore SavedViews { get; }
        public JsonWorkflowLibraryStore Workflows { get; }
        public StateBackupService Service { get; }

        public static async Task<BackupFixture> CreateAsync(IStateRestoreFaultInjector? faults = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "OmniSorSe-state-backup-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var paths = new ApplicationPathProvider(HostPlatformKind.Windows, _ => null, root, root);
            paths.EnsureOwnedDirectories();
            var configuration = new JsonConfigurationService(paths.SettingsFilePath, _ => null);
            await configuration.InitializeAsync(CancellationToken.None);
            var store = new SqliteDeepIndexStore(
                Path.Combine(paths.Paths.DataDirectory, "deep-index.db"),
                PlatformServices.CurrentPathSemantics);
            await store.InitializeAsync(CancellationToken.None);
            var logging = new LoggingService();
            var savedViews = new JsonSavedDiscoveryViewStore(
                Path.Combine(paths.Paths.StateDirectory, "saved-views.json"),
                logging);
            var workflows = new JsonWorkflowLibraryStore(
                Path.Combine(paths.Paths.StateDirectory, "workflow-library.json"),
                new WorkflowValidator(new WorkflowTemplateEngine()),
                logging);
            var watched = new JsonWatchedFolderConfigurationStore(
                Path.Combine(paths.Paths.StateDirectory, "watched-folders.json"),
                logging);
            var service = new StateBackupService(
                configuration,
                store,
                paths,
                savedViews,
                store,
                store,
                watched,
                workflows,
                faults ?? new NoOpStateRestoreFaultInjector());
            return new BackupFixture(root, paths, configuration, store, savedViews, workflows, service);
        }

        public string PathFor(string name) => Path.Combine(_root, name);

        public void Execute(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(Paths.Paths.DataDirectory, "deep-index.db")};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public string Scalar(string sql)
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(Paths.Paths.DataDirectory, "deep-index.db")};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
        }

        public async Task SeedUserStateAsync()
        {
            await Configuration.SaveAsync(
                new ApplicationSettings { Features = new FeatureSettings { ShowAdvancedFeatures = true } },
                CancellationToken.None);
            var sourceRoot = Path.Combine(_root, "source");
            Directory.CreateDirectory(sourceRoot);
            await Store.UpsertSourceAsync(new IndexingSource(
                "source:user",
                sourceRoot,
                "User source",
                IndexingLevel.Deep,
                true,
                true,
                5,
                []));
            await SavedViews.SaveAsync(new SavedDiscoveryView(
                "view:user",
                "Invoices",
                new DiscoveryQueryState("invoice", []),
                1,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch));
            var recipe = BuiltInWorkflowLibrary.Recipes[0] with
            {
                Id = "recipe:user",
                Name = "User recipe",
                IsBuiltIn = false,
                Origin = new WorkflowProfileOrigin(WorkflowOriginKind.UserCreated),
            };
            await Workflows.SaveAsync([], [recipe], CancellationToken.None);
        }

        public async Task<(string FileId, string TagId)> SeedIndexedUserTagAsync()
        {
            var source = Assert.Single(await Store.GetSourcesAsync(), item => item.Id == "source:user");
            var fullPath = Path.Combine(source.RootPath, "authority.txt");
            await File.WriteAllTextAsync(fullPath, "backup authority fixture");
            var runId = await Store.BeginRunAsync(source.Id, DateTimeOffset.UnixEpoch);
            await Store.EnqueueDiscoveredFilesAsync(
                runId,
                [new IndexingFileObservation(
                    fullPath,
                    "authority.txt",
                    "backup-authority-identity",
                    "backup-authority-volume",
                    new FileInfo(fullPath).Length,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    FileAttributes.Normal,
                    "backup-authority-metadata")],
                "backup-authority-processor",
                1);
            await Store.CompleteDiscoveryAsync(runId, new HashSet<string> { "authority.txt" }, DateTimeOffset.UnixEpoch);
            var fileId = await Store.ResolveActiveFileIdAsync(fullPath);
            Assert.NotNull(fileId);
            await Store.AddUserTagAsync(fileId, "Private:Review", DateTimeOffset.UnixEpoch);
            var tag = Assert.Single(await Store.GetFileSmartTagsAsync(fileId), item => item.Definition.Type == SmartTagType.UserTag);
            return (fileId, tag.Definition.TagId);
        }

        public async Task<(string FirstFileId, string SecondFileId)> SeedIndexedFilePairAsync()
        {
            var source = Assert.Single(await Store.GetSourcesAsync(), item => item.Id == "source:user");
            var observations = new List<IndexingFileObservation>();
            foreach (var name in new[] { "first.txt", "second.txt" })
            {
                var fullPath = Path.Combine(source.RootPath, name);
                await File.WriteAllTextAsync(fullPath, name);
                observations.Add(new IndexingFileObservation(
                    fullPath,
                    name,
                    $"relationship-{name}",
                    "relationship-volume",
                    new FileInfo(fullPath).Length,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    FileAttributes.Normal,
                    $"relationship-metadata-{name}"));
            }

            var runId = await Store.BeginRunAsync(source.Id, DateTimeOffset.UnixEpoch);
            await Store.EnqueueDiscoveredFilesAsync(runId, observations, "relationship-processor", 1);
            await Store.CompleteDiscoveryAsync(
                runId,
                observations.Select(item => item.RelativePath).ToHashSet(StringComparer.Ordinal),
                DateTimeOffset.UnixEpoch);
            var ids = new List<string>();
            foreach (var observation in observations)
            {
                var id = await Store.ResolveActiveFileIdAsync(observation.FullPath);
                Assert.NotNull(id);
                ids.Add(id);
            }

            return string.CompareOrdinal(ids[0], ids[1]) < 0 ? (ids[0], ids[1]) : (ids[1], ids[0]);
        }

        public async ValueTask DisposeAsync()
        {
            await Store.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
