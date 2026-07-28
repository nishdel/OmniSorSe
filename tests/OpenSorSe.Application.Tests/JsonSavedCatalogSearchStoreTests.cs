using OpenSorSe.Application.CatalogSearch;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

/// <summary>
/// Verifies bounded atomic saved-query persistence in disposable application-owned test directories.
/// </summary>
public sealed class JsonSavedCatalogSearchStoreTests
{
    /// <summary>Verifies records round trip in deterministic update order and replacement preserves capacity.</summary>
    [Fact]
    public async Task SaveAndListAsync_RoundTripsReplacesAndOrdersSearches()
    {
        var directory = CreateDirectory();
        var store = new JsonSavedCatalogSearchStore(Path.Combine(directory, "saved-catalog-searches.json"), new LoggingService());
        try
        {
            await store.SaveAsync(CreateSearch("search:one", " First ", " finance ", 0), CancellationToken.None);
            await store.SaveAsync(CreateSearch("search:two", "Second", "photos", 2), CancellationToken.None);
            await store.SaveAsync(CreateSearch("search:one", "First updated", "finance quarterly", 3) with { CreatedAtUtc = DateTimeOffset.UnixEpoch }, CancellationToken.None);

            var searches = await store.ListAsync(CancellationToken.None);

            Assert.Equal(2, searches.Count);
            Assert.Equal(["search:one", "search:two"], searches.Select(search => search.Id));
            Assert.Equal("First updated", searches[0].Name);
            Assert.Equal("finance quarterly", searches[0].QueryText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies a distinct twenty-sixth record is rejected without evicting curated data.</summary>
    [Fact]
    public async Task SaveAsync_AtCapacity_RejectsNewRecordWithoutMutation()
    {
        var directory = CreateDirectory();
        var store = new JsonSavedCatalogSearchStore(Path.Combine(directory, "saved-catalog-searches.json"), new LoggingService());
        try
        {
            for (var index = 0; index < SavedCatalogSearchLimits.MaximumSearchCount; index++)
            {
                await store.SaveAsync(CreateSearch($"search:{index}", $"Search {index}", $"query {index}", index), CancellationToken.None);
            }

            await Assert.ThrowsAsync<SavedCatalogSearchCapacityExceededException>(() => store.SaveAsync(
                CreateSearch("search:overflow", "Overflow", "overflow", 30),
                CancellationToken.None));

            var searches = await store.ListAsync(CancellationToken.None);
            Assert.Equal(SavedCatalogSearchLimits.MaximumSearchCount, searches.Count);
            Assert.DoesNotContain(searches, search => search.Id == "search:overflow");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies malformed data is preserved for diagnosis but explicit clear recovers the store.</summary>
    [Fact]
    public async Task ClearAsync_MalformedFile_ExplicitlyRecoversSavedSearchStorage()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "saved-catalog-searches.json");
        await File.WriteAllTextAsync(path, "{invalid");
        var store = new JsonSavedCatalogSearchStore(path, new LoggingService());
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync(CancellationToken.None));
            Assert.True(File.Exists(path));

            await store.ClearAsync(CancellationToken.None);

            Assert.False(File.Exists(path));
            Assert.Empty(await store.ListAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies a null persisted record is reported through the store's corruption contract.</summary>
    [Fact]
    public async Task ListAsync_NullRecord_ThrowsInvalidDataExceptionAndPreservesFile()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "saved-catalog-searches.json");
        await File.WriteAllTextAsync(path, "{\"SchemaVersion\":1,\"Searches\":[null]}");
        var originalBytes = await File.ReadAllBytesAsync(path);
        var store = new JsonSavedCatalogSearchStore(path, new LoggingService());

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync(CancellationToken.None));
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies selected removal is isolated and pre-cancellation performs no I/O.</summary>
    [Fact]
    public async Task RemoveAndCancellation_AffectOnlyAuthorizedSavedSearchState()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "saved-catalog-searches.json");
        var store = new JsonSavedCatalogSearchStore(path, new LoggingService());
        try
        {
            await store.SaveAsync(CreateSearch("search:one", "One", "one", 0), CancellationToken.None);
            await store.SaveAsync(CreateSearch("search:two", "Two", "two", 1), CancellationToken.None);
            Assert.True(await store.RemoveAsync("search:one", CancellationToken.None));
            Assert.False(await store.RemoveAsync("search:missing", CancellationToken.None));
            Assert.Equal(["search:two"], (await store.ListAsync(CancellationToken.None)).Select(search => search.Id));

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.ClearAsync(cancellation.Token));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies invalid names, queries, timestamps, and paths are rejected explicitly.</summary>
    [Fact]
    public async Task InvalidInput_IsRejectedWithoutCreatingStorage()
    {
        Assert.Throws<ArgumentException>(() => new JsonSavedCatalogSearchStore("relative.json", new LoggingService()));
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "saved-catalog-searches.json");
        var store = new JsonSavedCatalogSearchStore(path, new LoggingService());
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(CreateSearch("search:one", "Bad\u0001Name", "valid", 0), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(CreateSearch("search:one", "Valid", "Bad\u0001Query", 0), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(CreateSearch("search:one", "Valid", "valid", 0) with
            {
                CreatedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(2),
                UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            }, CancellationToken.None));
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies an oversized owned store is rejected before deserialization and remains untouched.</summary>
    [Fact]
    public async Task ListAsync_OversizedStore_ThrowsAndPreservesFile()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "saved-catalog-searches.json");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(SavedCatalogSearchLimits.MaximumStoreFileBytes + 1);
        }

        var store = new JsonSavedCatalogSearchStore(path, new LoggingService());
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => store.ListAsync(CancellationToken.None));
            Assert.Equal(SavedCatalogSearchLimits.MaximumStoreFileBytes + 1, new FileInfo(path).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verifies separate saved-search instances serialize concurrent updates without lost records.</summary>
    [Fact]
    public async Task SaveAsync_MultipleInstancesConcurrently_PreservesEverySearch()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "saved-catalog-searches.json");
        try
        {
            var searches = Enumerable.Range(0, 20)
                .Select(index => CreateSearch(
                    $"search:{index:D2}",
                    $"Search {index:D2}",
                    $"query {index:D2}",
                    index))
                .ToArray();

            await Task.WhenAll(searches.Select(search =>
                new JsonSavedCatalogSearchStore(path, new LoggingService())
                    .SaveAsync(search, CancellationToken.None)));

            var reloaded = await new JsonSavedCatalogSearchStore(path, new LoggingService())
                .ListAsync(CancellationToken.None);
            Assert.Equal(20, reloaded.Count);
            Assert.Equal(
                searches.Select(search => search.Id).OrderBy(value => value, StringComparer.Ordinal),
                reloaded.Select(search => search.Id).OrderBy(value => value, StringComparer.Ordinal));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SavedCatalogSearch CreateSearch(string id, string name, string query, int minute) => new(
        id,
        name,
        query,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch.AddMinutes(minute));

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"opensorse-saved-searches-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
