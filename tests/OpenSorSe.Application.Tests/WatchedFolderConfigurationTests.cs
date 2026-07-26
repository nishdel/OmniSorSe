#pragma warning disable CS1591

using System.Text.Json.Nodes;
using OpenSorSe.Application.Watching;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

public sealed class WatchedFolderConfigurationTests
{
    [Fact]
    public async Task Manager_AddEditPauseResumeAndRemove_PersistsWithoutDeletingFolder()
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var userFile = Path.Combine(root, "keep.txt");
        await File.WriteAllTextAsync(userFile, "keep");
        var storePath = Path.Combine(workspace, "app-data", "watched-folders.json");
        var manager = CreateManager(storePath);

        try
        {
            var created = await manager.AddAsync(
                new WatchedFolderCreateRequest(root, "Inbox"),
                CancellationToken.None);
            var updated = await manager.UpdateAsync(
                created.Id,
                new WatchedFolderUpdateRequest(
                    "Invoices",
                    false,
                    ["Archive"],
                    ["*.bak"],
                    "documents",
                    "invoice-recipe",
                    true,
                    true,
                    new WatchedFolderNotificationPreferences(WatchedFolderNotificationLevel.ErrorsOnly),
                    TimeSpan.FromSeconds(3),
                    10_000,
                    false),
                CancellationToken.None);
            var paused = await manager.PauseAsync(created.Id, CancellationToken.None);
            var resumed = await manager.ResumeAsync(created.Id, CancellationToken.None);
            var reloaded = CreateManager(storePath);
            var persisted = Assert.Single(await reloaded.ListAsync(CancellationToken.None));

            Assert.Equal("Invoices", updated.DisplayName);
            Assert.False(paused.IsEnabled);
            Assert.Equal(WatchedFolderStatus.Paused, paused.Status);
            Assert.True(resumed.IsEnabled);
            Assert.Equal("invoice-recipe", persisted.SortingRecipeId);
            Assert.True(persisted.AiAnalysisEnabled);
            Assert.Equal(["Archive"], persisted.IgnoredPaths);
            Assert.True(await manager.RemoveAsync(created.Id, CancellationToken.None));
            Assert.True(File.Exists(userFile));
            Assert.True(Directory.Exists(root));
            Assert.Empty(await manager.ListAsync(CancellationToken.None));
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task Manager_MissingFolder_RetainsUnavailableConfiguration()
    {
        var workspace = CreateWorkspace();
        var missing = Path.Combine(workspace, "disconnected");
        var manager = CreateManager(Path.Combine(workspace, "watched.json"));
        try
        {
            var created = await manager.AddAsync(
                new WatchedFolderCreateRequest(missing, "Disconnected drive"),
                CancellationToken.None);

            Assert.Equal(WatchedFolderStatus.Unavailable, created.Status);
            Assert.True(created.IsEnabled);
            Assert.Equal(Path.GetFullPath(missing), created.FolderPath);
            Assert.Single(await manager.ListAsync(CancellationToken.None));
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task Manager_DuplicateAndOverlappingRoots_AreRejectedClearly()
    {
        var workspace = CreateWorkspace();
        var parent = Directory.CreateDirectory(Path.Combine(workspace, "Documents")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(parent, "Invoices")).FullName;
        var sibling = Directory.CreateDirectory(Path.Combine(workspace, "Pictures")).FullName;
        var manager = CreateManager(Path.Combine(workspace, "watched.json"));

        try
        {
            await manager.AddAsync(new WatchedFolderCreateRequest(parent, "Documents"), CancellationToken.None);

            var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.AddAsync(new WatchedFolderCreateRequest(parent + Path.DirectorySeparatorChar, "Again"), CancellationToken.None));
            var overlap = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                manager.AddAsync(new WatchedFolderCreateRequest(child, "Invoices"), CancellationToken.None));
            var separate = await manager.AddAsync(
                new WatchedFolderCreateRequest(sibling, "Pictures"),
                CancellationToken.None);

            Assert.Contains("overlaps", duplicate.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prevent duplicate processing", overlap.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, (await manager.ListAsync(CancellationToken.None)).Count);
            Assert.Equal(Path.GetFullPath(sibling), separate.FolderPath);
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task ConfigurationStore_MalformedJson_ThrowsAndPreservesBytes()
    {
        var workspace = CreateWorkspace();
        var path = Path.Combine(workspace, "watched.json");
        await File.WriteAllTextAsync(path, "{ malformed");
        var before = await File.ReadAllBytesAsync(path);
        var store = new JsonWatchedFolderConfigurationStore(path, new LoggingService());

        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.LoadAsync(CancellationToken.None));

            Assert.Contains("malformed", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task ConfigurationStore_SchemaOne_LoadsAndWritesForwardToSchemaTwo()
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var path = Path.Combine(workspace, "watched.json");
        var store = new JsonWatchedFolderConfigurationStore(path, new LoggingService());
        var manager = new WatchedFolderManager(store, new WatchedFolderPathPolicy());

        try
        {
            var created = await manager.AddAsync(
                new WatchedFolderCreateRequest(root, "Root"),
                CancellationToken.None);
            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            json["SchemaVersion"] = 1;
            await File.WriteAllTextAsync(path, json.ToJsonString());
            var schemaOneBytes = await File.ReadAllBytesAsync(path);
            var legacyStore = new JsonWatchedFolderConfigurationStore(path, new LoggingService());

            var loaded = Assert.Single(await legacyStore.LoadAsync(CancellationToken.None));

            Assert.Equal(created.Id, loaded.Id);
            Assert.Equal(schemaOneBytes, await File.ReadAllBytesAsync(path));
            await legacyStore.SaveAsync([loaded], CancellationToken.None);
            var upgraded = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            Assert.Equal(2, upgraded["SchemaVersion"]!.GetValue<int>());
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(301)]
    public async Task Manager_InvalidQuietPeriod_IsRejected(double seconds)
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var manager = CreateManager(Path.Combine(workspace, "watched.json"));
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                manager.AddAsync(
                    new WatchedFolderCreateRequest(root, QuietPeriod: TimeSpan.FromSeconds(seconds)),
                    CancellationToken.None));
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public void PathPolicy_CanonicalComparisonAndRootContainment_AreTraversalSafe()
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "Root")).FullName;
        var child = Path.Combine(root, "Sub", "..", "file.txt");
        var outside = Path.Combine(workspace, "Root-other", "file.txt");
        var policy = new WatchedFolderPathPolicy();
        try
        {
            Assert.True(policy.IsWithinRoot(root, child));
            Assert.False(policy.IsWithinRoot(root, outside));
            Assert.True(policy.Overlaps(root, Path.Combine(root, "Sub")));
            var volumeRoot = Assert.IsType<string>(Path.GetPathRoot(root));
            Assert.Equal(
                volumeRoot,
                policy.CanonicalizeRoot(volumeRoot),
                WatchedFolderPathPolicy.PathComparer);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    policy.CanonicalizeRoot(root),
                    policy.CanonicalizeRoot(root.ToUpperInvariant()),
                    WatchedFolderPathPolicy.PathComparer);
            }
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Theory]
    [InlineData("draft.tmp")]
    [InlineData("download.crdownload")]
    [InlineData("document.part")]
    [InlineData("~$document.docx")]
    [InlineData(".DS_Store")]
    [InlineData("watched-folders.json")]
    public void PathPolicy_BuiltInTemporaryAndInternalFiles_AreIgnored(string fileName)
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var configuration = CreateConfiguration(root);
        try
        {
            Assert.True(new WatchedFolderPathPolicy().ShouldIgnore(
                configuration,
                Path.Combine(root, fileName),
                FileAttributes.Normal,
                1));
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public void PathPolicy_ExactDirectoryPatternHiddenSizeAndOutsideRules_AreAppliedPrecisely()
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var configuration = CreateConfiguration(root) with
        {
            IgnoredPaths = ["Archive"],
            IgnorePatterns = ["*.bak", "private/**"],
            MaximumFileSizeBytes = 10,
            IgnoreHiddenFiles = true,
        };
        var policy = new WatchedFolderPathPolicy();
        try
        {
            Assert.True(policy.ShouldIgnore(configuration, Path.Combine(root, "Archive", "a.txt"), FileAttributes.Normal, 1));
            Assert.True(policy.ShouldIgnore(configuration, Path.Combine(root, "copy.bak"), FileAttributes.Normal, 1));
            Assert.True(policy.ShouldIgnore(configuration, Path.Combine(root, "private", "a.txt"), FileAttributes.Normal, 1));
            Assert.True(policy.ShouldIgnore(configuration, Path.Combine(root, "hidden.txt"), FileAttributes.Hidden, 1));
            Assert.True(policy.ShouldIgnore(configuration, Path.Combine(root, "large.txt"), FileAttributes.Normal, 11));
            Assert.True(policy.ShouldIgnore(configuration, Path.Combine(workspace, "outside.txt"), FileAttributes.Normal, 1));
            Assert.False(policy.ShouldIgnore(configuration, Path.Combine(root, "Archive-not", "a.txt"), FileAttributes.Normal, 1));
            Assert.False(policy.ShouldIgnore(configuration, Path.Combine(root, "report.txt"), FileAttributes.Normal, 10));
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task ActivityAndCatalogueStores_RoundTripVersionedBoundedRecords()
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var activityStore = new JsonWatchedActivityStore(
            Path.Combine(workspace, "activity.json"),
            new LoggingService());
        var catalogueStore = new JsonWatchedFolderCatalogueStore(
            Path.Combine(workspace, "catalogues.json"),
            new LoggingService());
        var activity = new WatchedActivityEntry(
            "activity:1",
            "watch:1",
            WatchedActivityKind.ChangeBatchDetected,
            DateTimeOffset.UnixEpoch,
            "2 changes grouped.",
            "batch:1",
            2);
        var catalogue = new WatchedFolderCatalogue(
            WatchedFolderLimits.CurrentCatalogueSchemaVersion,
            "catalogue:1",
            "watch:1",
            root,
            DateTimeOffset.UnixEpoch,
            [],
            [],
            DateTimeOffset.UnixEpoch,
            false);
        try
        {
            await activityStore.AppendAsync(activity, CancellationToken.None);
            await catalogueStore.UpsertAsync(catalogue, CancellationToken.None);

            Assert.Equal(activity, Assert.Single(await activityStore.ListAsync("watch:1", 10, CancellationToken.None)));
            var loaded = Assert.IsType<WatchedFolderCatalogue>(
                await catalogueStore.GetAsync("catalogue:1", CancellationToken.None));
            Assert.Equal(catalogue.CatalogueId, loaded.CatalogueId);
            Assert.Equal(catalogue.ConfigurationId, loaded.ConfigurationId);
            Assert.Equal(catalogue.RootPath, loaded.RootPath);
            Assert.Empty(loaded.Files);
            Assert.Empty(loaded.Directories);
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    private static WatchedFolderManager CreateManager(string path) =>
        new(
            new JsonWatchedFolderConfigurationStore(path, new LoggingService()),
            new WatchedFolderPathPolicy());

    private static WatchedFolderConfiguration CreateConfiguration(string root) => new(
        "watch:1",
        root,
        "Root",
        true,
        true,
        [],
        [],
        "default",
        null,
        true,
        false,
        new WatchedFolderNotificationPreferences(),
        WatchedFolderLimits.DefaultQuietPeriod,
        null,
        null,
        WatchedFolderStatus.Watching,
        "catalogue:1");

    private static string CreateWorkspace()
    {
        var path = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"opensorse-watched-config-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteWorkspace(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), fullPath, StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}
