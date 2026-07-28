using OpenSorSe.Application.Catalog;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Logging;
using OpenSorSe.Application.Tags;
using OpenSorSe.Scanner.Models;
using System.Text.Json.Nodes;

namespace OpenSorSe.Application.Tests;

/// <summary>
/// Verifies bounded application-owned result-catalog persistence without user-folder access.
/// </summary>
public sealed class JsonResultsCatalogStoreTests
{
    /// <summary>
    /// Verifies a display-safe snapshot and accepted non-deterministic tag round trip while unsupported tag states are omitted.
    /// </summary>
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsSnapshotAndAcceptedNonDeterministicTags()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "catalog.json");
        var store = new JsonResultsCatalogStore(path, new LoggingService());
        var snapshot = CreateSnapshot();
        var accepted = new TagAssociation("tag:accepted", "file:0", "Finance", "finance", "Topic", TagSource.UserApproved, TagAcceptanceState.Accepted, null, DateTimeOffset.UnixEpoch);
        var deterministic = accepted with { TagId = "tag:deterministic", Source = TagSource.Deterministic };
        var rejected = accepted with { TagId = "tag:rejected", AcceptanceState = TagAcceptanceState.Rejected };

        try
        {
            await store.SaveAsync(new CatalogEntry("catalog:one", DateTimeOffset.UnixEpoch, snapshot, [accepted, deterministic, rejected]), CancellationToken.None);
            var summaries = await store.ListAsync(CancellationToken.None);
            var loaded = await store.LoadAsync("catalog:one", CancellationToken.None);

            Assert.Single(summaries);
            Assert.Equal(1, summaries[0].FileCount);
            Assert.NotNull(loaded);
            Assert.Equal(snapshot.SessionId, loaded.Snapshot.SessionId);
            Assert.Equal(snapshot.Files, loaded.Snapshot.Files);
            Assert.Equal(snapshot.Directories, loaded.Snapshot.Directories);
            Assert.IsAssignableFrom<IReadOnlyList<ResultFile>>(loaded.Snapshot.Files);
            Assert.Equal([accepted], loaded.AcceptedTags);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies v0.8 snapshot identity metadata round trips with deterministic path de-duplication and immutable results.
    /// </summary>
    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsNameAndBoundedSourceScope()
    {
        var directory = CreateDirectory();
        var store = new JsonResultsCatalogStore(Path.Combine(directory, "catalog.json"), new LoggingService());
        var entry = new CatalogEntry("catalog:identity", DateTimeOffset.UnixEpoch, CreateSnapshot(), [])
        {
            DisplayName = "  Quarterly baseline  ",
            SourceRoots = ["C:\\Selected", "c:/selected/", "/Data", "/data"],
        };

        try
        {
            var summary = await store.SaveAsync(entry, CancellationToken.None);
            var loaded = Assert.IsType<CatalogEntry>(await store.LoadAsync(entry.Id, CancellationToken.None));

            Assert.Equal("Quarterly baseline", summary.DisplayName);
            Assert.Equal(["C:\\Selected", "/Data", "/data"], summary.SourceRoots);
            Assert.Equal(summary.DisplayName, loaded.DisplayName);
            Assert.Equal(summary.SourceRoots, loaded.SourceRoots);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)loaded.SourceRoots).Add("/another"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies a schema-one catalog is read without mutation and upgrades only on a later successful write.
    /// </summary>
    [Fact]
    public async Task LoadAsync_SchemaOneCatalog_ReadsWithoutMutationAndWritesForwardToSchemaTwo()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "catalog.json");
        var store = new JsonResultsCatalogStore(path, new LoggingService());

        try
        {
            await store.SaveAsync(new CatalogEntry("catalog:legacy", DateTimeOffset.UnixEpoch, CreateSnapshot(), []), CancellationToken.None);
            var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            document["SchemaVersion"] = 1;
            foreach (var entryNode in document["Entries"]!.AsArray())
            {
                var entryObject = entryNode!.AsObject();
                entryObject.Remove("DisplayName");
                entryObject.Remove("SourceRoots");
            }

            await File.WriteAllTextAsync(path, document.ToJsonString());
            var legacyBytes = await File.ReadAllBytesAsync(path);

            var loaded = Assert.IsType<CatalogEntry>(await store.LoadAsync("catalog:legacy", CancellationToken.None));
            Assert.Null(loaded.DisplayName);
            Assert.Empty(loaded.SourceRoots);
            Assert.Equal(legacyBytes, await File.ReadAllBytesAsync(path));

            await store.SaveAsync(loaded with { DisplayName = "Legacy baseline" }, CancellationToken.None);
            var upgraded = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            Assert.Equal(2, upgraded["SchemaVersion"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies invalid identity metadata and excessive roots cannot replace an existing valid catalog.
    /// </summary>
    [Fact]
    public async Task SaveAsync_InvalidOrExcessiveIdentityMetadata_PreservesExistingCatalog()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "catalog.json");
        var store = new JsonResultsCatalogStore(path, new LoggingService());

        try
        {
            var existing = new CatalogEntry("catalog:one", DateTimeOffset.UnixEpoch, CreateSnapshot(), []);
            await store.SaveAsync(existing, CancellationToken.None);
            var originalBytes = await File.ReadAllBytesAsync(path);
            var invalidName = existing with { DisplayName = "invalid\u0001name" };
            var invalidRoot = existing with { SourceRoots = ["relative/path"] };
            var excessiveRoots = existing with
            {
                SourceRoots = Enumerable.Range(0, CatalogLimits.MaximumSourceRootCount + 1)
                    .Select(index => $"/source/{index}")
                    .ToArray(),
            };

            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalidName, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalidRoot, CancellationToken.None));
            await Assert.ThrowsAsync<CatalogCapacityExceededException>(() => store.SaveAsync(excessiveRoots, CancellationToken.None));

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the store retains precisely the newest bounded set in deterministic newest-first order.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ExceedingEntryLimit_RetainsNewestEntries()
    {
        var directory = CreateDirectory();
        var store = new JsonResultsCatalogStore(Path.Combine(directory, "catalog.json"), new LoggingService());

        try
        {
            for (var index = 0; index < CatalogLimits.MaximumEntryCount + 1; index++)
            {
                await store.SaveAsync(
                    new CatalogEntry($"catalog:{index:D2}", DateTimeOffset.UnixEpoch.AddMinutes(index), CreateSnapshot(), []),
                    CancellationToken.None);
            }

            var summaries = await store.ListAsync(CancellationToken.None);

            Assert.Equal(CatalogLimits.MaximumEntryCount, summaries.Count);
            Assert.Equal("catalog:10", summaries[0].Id);
            Assert.DoesNotContain(summaries, summary => summary.Id == "catalog:00");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies oversized snapshots are rejected rather than truncated and leave existing data intact.
    /// </summary>
    [Fact]
    public async Task SaveAsync_OversizedSnapshot_RejectsWithoutReplacingPriorEntry()
    {
        var directory = CreateDirectory();
        var store = new JsonResultsCatalogStore(Path.Combine(directory, "catalog.json"), new LoggingService());

        try
        {
            await store.SaveAsync(new CatalogEntry("catalog:small", DateTimeOffset.UnixEpoch, CreateSnapshot(), []), CancellationToken.None);
            var oversized = CreateSnapshot(CatalogLimits.MaximumFilesPerEntry + 1);

            await Assert.ThrowsAsync<CatalogCapacityExceededException>(() => store.SaveAsync(
                new CatalogEntry("catalog:large", DateTimeOffset.UnixEpoch.AddMinutes(1), oversized, []),
                CancellationToken.None));

            var summaries = await store.ListAsync(CancellationToken.None);
            Assert.Equal(["catalog:small"], summaries.Select(summary => summary.Id));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies accepted tag capacity is enforced by persistence even when a caller bypasses the tag editor.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ExcessiveAcceptedTags_RejectsWithoutReplacingPriorEntry()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "catalog.json");
        var store = new JsonResultsCatalogStore(path, new LoggingService());

        try
        {
            var existing = new CatalogEntry("catalog:small", DateTimeOffset.UnixEpoch, CreateSnapshot(), []);
            await store.SaveAsync(existing, CancellationToken.None);
            var originalBytes = await File.ReadAllBytesAsync(path);
            var tags = Enumerable.Range(0, UserTagLimits.MaximumAcceptedTagsPerFile + 1)
                .Select(index => new TagAssociation(
                    $"tag:{index}",
                    "file:0",
                    $"Tag {index}",
                    $"tag-{index}",
                    "User",
                    TagSource.UserApproved,
                    TagAcceptanceState.Accepted,
                    null,
                    DateTimeOffset.UnixEpoch))
                .ToArray();

            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(
                new CatalogEntry("catalog:tags", DateTimeOffset.UnixEpoch.AddMinutes(1), CreateSnapshot(), tags),
                CancellationToken.None));

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an oversized application-owned catalog is rejected before JSON materialization and left untouched.
    /// </summary>
    [Fact]
    public async Task ListAsync_OversizedCatalog_ThrowsAndPreservesFile()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "catalog.json");
        Directory.CreateDirectory(directory);
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(CatalogLimits.MaximumCatalogFileBytes + 1);
        }

        var store = new JsonResultsCatalogStore(path, new LoggingService());
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync(CancellationToken.None));
            Assert.Equal(CatalogLimits.MaximumCatalogFileBytes + 1, new FileInfo(path).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies malformed application-owned JSON is not silently treated as an empty catalog.
    /// </summary>
    [Fact]
    public async Task ListAsync_MalformedCatalog_ThrowsInvalidDataException()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "catalog.json");
        await File.WriteAllTextAsync(path, "{invalid");
        var store = new JsonResultsCatalogStore(path, new LoggingService());

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync(CancellationToken.None));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies malformed nested snapshot records produce the catalog error contract instead of null-reference failures.
    /// </summary>
    [Fact]
    public async Task ListAsync_NullNestedFile_ThrowsInvalidDataExceptionAndPreservesCatalog()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "catalog.json");
        var store = new JsonResultsCatalogStore(path, new LoggingService());

        try
        {
            await store.SaveAsync(new CatalogEntry("catalog:one", DateTimeOffset.UnixEpoch, CreateSnapshot(), []), CancellationToken.None);
            var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            document["Entries"]![0]!["Snapshot"]!["Files"]![0] = null;
            await File.WriteAllTextAsync(path, document.ToJsonString());
            var originalBytes = await File.ReadAllBytesAsync(path);

            await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync(CancellationToken.None));

            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies invalid nested numeric and enum values are rejected rather than published.</summary>
    [Fact]
    public async Task ListAsync_InvalidNestedValues_ThrowsInvalidDataException()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "catalog.json");
        var store = new JsonResultsCatalogStore(path, new LoggingService());

        try
        {
            await store.SaveAsync(new CatalogEntry("catalog:one", DateTimeOffset.UnixEpoch, CreateSnapshot(), []), CancellationToken.None);
            var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            var file = document["Entries"]![0]!["Snapshot"]!["Files"]![0]!.AsObject();
            file["SizeInBytes"] = -1;
            file["DuplicateStatus"] = 999;
            await File.WriteAllTextAsync(path, document.ToJsonString());

            await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies cancellation prevents directory creation and catalog writes before the operation begins.
    /// </summary>
    [Fact]
    public async Task StoreOperations_PreCancelled_DoNotCreateApplicationData()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensorse-catalog-{Guid.NewGuid():N}");
        var store = new JsonResultsCatalogStore(Path.Combine(directory, "catalog.json"), new LoggingService());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SaveAsync(
            new CatalogEntry("catalog:one", DateTimeOffset.UnixEpoch, CreateSnapshot(), []),
            cancellation.Token));
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>
    /// Verifies removing a selected application-owned entry preserves other entries and reports missing identifiers without mutation.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_RemovesOnlyRequestedExistingEntry()
    {
        var directory = CreateDirectory();
        var store = new JsonResultsCatalogStore(Path.Combine(directory, "catalog.json"), new LoggingService());

        try
        {
            await store.SaveAsync(new CatalogEntry("catalog:one", DateTimeOffset.UnixEpoch, CreateSnapshot(), []), CancellationToken.None);
            await store.SaveAsync(new CatalogEntry("catalog:two", DateTimeOffset.UnixEpoch.AddMinutes(1), CreateSnapshot(), []), CancellationToken.None);

            var removed = await store.RemoveAsync("catalog:one", CancellationToken.None);
            var missing = await store.RemoveAsync("catalog:missing", CancellationToken.None);
            var summaries = await store.ListAsync(CancellationToken.None);

            Assert.True(removed);
            Assert.False(missing);
            Assert.Equal(["catalog:two"], summaries.Select(summary => summary.Id));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies an explicitly requested clear removes only valid application-owned catalog data and does not erase malformed data automatically.
    /// </summary>
    [Fact]
    public async Task ClearAsync_ValidCatalogRemovesFileButMalformedCatalogIsPreserved()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "catalog.json");
        var store = new JsonResultsCatalogStore(path, new LoggingService());

        try
        {
            await store.SaveAsync(new CatalogEntry("catalog:one", DateTimeOffset.UnixEpoch, CreateSnapshot(), []), CancellationToken.None);
            await store.ClearAsync(CancellationToken.None);
            Assert.False(File.Exists(path));

            await File.WriteAllTextAsync(path, "{invalid");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ClearAsync(CancellationToken.None));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies separate catalog instances serialize concurrent save transactions without lost entries.</summary>
    [Fact]
    public async Task SaveAsync_MultipleInstancesConcurrently_PreservesEveryEntry()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "catalog.json");
        try
        {
            var entries = Enumerable.Range(0, CatalogLimits.MaximumEntryCount)
                .Select(index => new CatalogEntry(
                    $"catalog:{index:D2}",
                    DateTimeOffset.UnixEpoch.AddMinutes(index),
                    CreateSnapshot(),
                    []))
                .ToArray();

            await Task.WhenAll(entries.Select(entry =>
                new JsonResultsCatalogStore(path, new LoggingService())
                    .SaveAsync(entry, CancellationToken.None)));

            var reloaded = await new JsonResultsCatalogStore(path, new LoggingService())
                .ListAsync(CancellationToken.None);
            Assert.Equal(CatalogLimits.MaximumEntryCount, reloaded.Count);
            Assert.Equal(
                entries.Select(entry => entry.Id).OrderBy(value => value, StringComparer.Ordinal),
                reloaded.Select(entry => entry.Id).OrderBy(value => value, StringComparer.Ordinal));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensorse-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static ResultsSnapshot CreateSnapshot(int fileCount = 1)
    {
        var files = Enumerable.Range(0, fileCount)
            .Select(index => new ResultFile(
                $"file:{index}",
                $"C:\\Selected\\file-{index}.txt",
                $"file-{index}.txt",
                ".txt",
                1,
                DateTimeOffset.UnixEpoch,
                FileCategory.Document,
                "Document",
                DuplicateStatus.Unique,
                null,
                false))
            .ToArray();
        return new ResultsSnapshot(
            "session:one",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            Array.AsReadOnly(files),
            Array.AsReadOnly(new[] { new ResultDirectory("C:\\Selected", "Selected") }),
            [],
            [],
            [],
            new ResultsSnapshotStatistics(fileCount, 1, 0, 0, 0, 0, 0),
            true);
    }
}
