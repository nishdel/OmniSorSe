using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

/// <summary>Tests configured eviction without losing compatibility-store user authority.</summary>
public sealed class RebuildableCacheBudgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"omnisorse-cache-budget-{Guid.NewGuid():N}");
    private readonly LoggingService _logging = new();

    /// <summary>Cache writes honor the configured encoded budget and retain an older explicit decision first.</summary>
    [Theory]
    [InlineData(TagAcceptanceState.Accepted)]
    [InlineData(TagAcceptanceState.Rejected)]
    public async Task ContentEvictionAndPruningPreserveUserDecisions(TagAcceptanceState state)
    {
        var path = Path.Combine(_root, "content-index.json");
        var store = new JsonContentStore(path, _logging, () => 4096);
        var authored = Record("authored.txt", 0) with { Tags = [Tag(state)] };
        await store.UpsertAsync(authored, CancellationToken.None);
        for (var i = 1; i <= 8; i++)
        {
            await store.UpsertAsync(Record($"derived-{i}.txt", i), CancellationToken.None);
        }

        Assert.InRange(new FileInfo(path).Length, 1, 4096);
        Assert.NotNull(await store.GetAsync(authored.FullPath, CancellationToken.None));
        Assert.True((await store.ListAsync(CancellationToken.None)).Count < 9);
        await store.PruneRebuildableAsync(CancellationToken.None);
        var retained = Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Equal(authored.FullPath, retained.FullPath);
        Assert.Equal(state, Assert.Single(retained.Tags).AcceptanceState);
    }

    /// <summary>A budget smaller than authored state fails before publication and preserves the stored decision.</summary>
    [Fact]
    public async Task LoweringLimitBelowAuthorityPreservesExistingFile()
    {
        var path = Path.Combine(_root, "content-index.json");
        long limit = 16_384;
        var store = new JsonContentStore(path, _logging, () => limit);
        await store.UpsertAsync(Record("authored.txt", 0) with { NativeText = new string('x', 8_000), Tags = [Tag(TagAcceptanceState.Rejected)] }, CancellationToken.None);
        var original = await File.ReadAllBytesAsync(path);
        limit = 4096;

        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpsertAsync(Record("new.txt", 2), CancellationToken.None));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal(TagAcceptanceState.Rejected, Assert.Single(Assert.Single(await store.ListAsync(CancellationToken.None)).Tags).AcceptanceState);
    }

    /// <summary>Derived search cache eviction and cleanup preserve records containing legacy user decisions.</summary>
    [Fact]
    public async Task SearchCacheEvictsDerivedEntriesBeforeAuthoredTags()
    {
        var path = Path.Combine(_root, "semantic-index.json");
        var store = new JsonSemanticIndexStore(path, _logging, () => 4096);
        var entries = Enumerable.Range(0, 10).Select(index => new SemanticIndexEntry(
            Path.Combine(_root, $"{index}.txt"), "source", "index", $"{index}.txt",
            index == 0 ? [Tag(TagAcceptanceState.Accepted)] : [], [], [new string('t', 700)], [], [0.5f],
            DateTimeOffset.UnixEpoch.AddDays(index))).ToArray();
        await store.ReplaceAsync(entries, CancellationToken.None);

        Assert.InRange(new FileInfo(path).Length, 1, 4096);
        Assert.Contains(await store.ListAsync(CancellationToken.None), entry => entry.FullPath == entries[0].FullPath);
        await store.PruneRebuildableAsync(CancellationToken.None);
        Assert.Equal(entries[0].FullPath, Assert.Single(await store.ListAsync(CancellationToken.None)).FullPath);
    }

    private ContentRecord Record(string name, int day) => new(Path.Combine(_root, name), 1, DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch.AddDays(day), [], new string('t', 700), null, OcrStatus.Skipped, null, []);

    /// <summary>Malformed compatibility state is retained because its authority cannot be safely classified.</summary>
    [Fact]
    public async Task CleanupPreservesUnreadableCompatibilityStores()
    {
        Directory.CreateDirectory(_root);
        var contentPath = Path.Combine(_root, "content-index.json");
        var semanticPath = Path.Combine(_root, "semantic-index.json");
        await File.WriteAllTextAsync(contentPath, "unreadable potential decisions");
        await File.WriteAllTextAsync(semanticPath, "unreadable potential decisions");
        var content = new JsonContentStore(contentPath, _logging);
        var semantic = new JsonSemanticIndexStore(semanticPath, _logging);
        await Assert.ThrowsAsync<InvalidDataException>(() => content.PruneRebuildableAsync(CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => semantic.PruneRebuildableAsync(CancellationToken.None));
        Assert.Equal("unreadable potential decisions", await File.ReadAllTextAsync(contentPath));
        Assert.Equal("unreadable potential decisions", await File.ReadAllTextAsync(semanticPath));
    }

    private static TagAssociation Tag(TagAcceptanceState state) => new("tag", "file", "Keep", "keep", "Topic",
        TagSource.AiSuggestion, state, "User decision", DateTimeOffset.UnixEpoch);

    /// <inheritdoc />
    public void Dispose()
    {
        _logging.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
