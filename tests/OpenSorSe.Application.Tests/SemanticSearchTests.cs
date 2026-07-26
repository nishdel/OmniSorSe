using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Tags;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies provenance tags and local deterministic hybrid Semantic Search Beta behavior.</summary>
public sealed class SemanticSearchTests
{
    /// <summary>Verifies deterministic feature hashing is stable, normalized, and local.</summary>
    [Fact]
    public void FeatureHashingEmbedding_SameInput_IsDeterministicAndNormalized()
    {
        var provider = new FeatureHashingEmbeddingProvider();

        var first = provider.Embed("tax documents 2023");
        var second = provider.Embed("tax documents 2023");

        Assert.Equal(256, first.Count);
        Assert.Equal(first, second);
        Assert.InRange(Math.Sqrt(first.Sum(value => value * value)), 0.999, 1.001);
    }

    /// <summary>Verifies confirmed file/date tags remain distinct from suggested embedded and OCR candidates.</summary>
    [Fact]
    public void ProvenanceTagGenerator_MixedEvidence_PreservesConfidenceAndState()
    {
        var tags = ProvenanceTagGenerator.Generate(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Finance", "invoice.pdf")),
            "10:20",
            [
                new ExtractedMetadataField("Modified UTC", "2023-05-01T00:00:00.0000000+00:00", ContentProvenance.Filesystem),
                new ExtractedMetadataField("Keywords", "tax; invoice", ContentProvenance.EmbeddedMetadata),
            ],
            "Quarterly tax declaration invoice reference",
            "scanned receipt amount invoice",
            DateTimeOffset.UnixEpoch);

        Assert.Contains(tags, tag =>
            tag.Source == TagSource.FileType &&
            tag.AcceptanceState == TagAcceptanceState.Accepted &&
            tag.IsSystem);
        Assert.Contains(tags, tag =>
            tag.Source == TagSource.Date &&
            tag.NormalizedValue == "2023" &&
            tag.AcceptanceState == TagAcceptanceState.Accepted);
        Assert.Contains(tags, tag =>
            tag.Source == TagSource.EmbeddedMetadata &&
            tag.AcceptanceState == TagAcceptanceState.Suggested &&
            tag.Confidence == 0.9);
        Assert.Contains(tags, tag =>
            tag.Source == TagSource.OcrCandidate &&
            tag.AcceptanceState == TagAcceptanceState.Suggested &&
            tag.Confidence == 0.55);
        Assert.Equal(tags.Count, tags.Select(tag => tag.NormalizedValue).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Verifies confirmed user tags outrank low-confidence OCR candidates with an understandable explanation.</summary>
    [Fact]
    public async Task SemanticSearch_UserConfirmedTag_OutranksOcrCandidate()
    {
        var configuration = new Configuration(enabled: true);
        var embedding = new FeatureHashingEmbeddingProvider();
        var store = new MemoryIndexStore();
        var now = DateTimeOffset.UnixEpoch;
        await store.ReplaceAsync(
            [
                Entry(
                    "C:\\Docs\\confirmed.pdf",
                    [Tag("tax", TagSource.UserApproved, TagAcceptanceState.Accepted, 1)],
                    [],
                    [],
                    embedding,
                    now),
                Entry(
                    "C:\\Docs\\ocr.pdf",
                    [Tag("tax", TagSource.OcrCandidate, TagAcceptanceState.Suggested, 0.55)],
                    [],
                    [],
                    embedding,
                    now,
                    ["tax"]),
            ],
            CancellationToken.None);
        var service = new SemanticSearchService(configuration, embedding, store);

        var result = await service.SearchAsync("tax", CancellationToken.None);

        Assert.Equal(SemanticState.Ready, result.State);
        Assert.Equal("confirmed.pdf", result.Value[0].FileName);
        Assert.Contains("Matched tags: tax", result.Value[0].Explanation, StringComparison.Ordinal);
        Assert.Contains(result.Value, hit => hit.MatchedOcrText);
    }

    /// <summary>Verifies metadata, native text, OCR text, and exact filename signals are explained.</summary>
    [Fact]
    public async Task SemanticSearch_HybridSignals_ExplainMatches()
    {
        var configuration = new Configuration(enabled: true);
        var embedding = new FeatureHashingEmbeddingProvider();
        var store = new MemoryIndexStore();
        await store.ReplaceAsync(
            [
                Entry(
                    "C:\\Docs\\warranty.pdf",
                    [],
                    ["washing", "machine", "warranty"],
                    ["appliance"],
                    embedding,
                    DateTimeOffset.UnixEpoch,
                    ["serial"]),
            ],
            CancellationToken.None);
        var service = new SemanticSearchService(configuration, embedding, store);

        var exact = await service.SearchAsync("warranty.pdf", CancellationToken.None);
        var metadata = await service.SearchAsync("washing", CancellationToken.None);
        var native = await service.SearchAsync("appliance", CancellationToken.None);
        var ocr = await service.SearchAsync("serial", CancellationToken.None);

        Assert.Contains("Exact filename match", Assert.Single(exact.Value).Explanation, StringComparison.Ordinal);
        Assert.True(Assert.Single(metadata.Value).MatchedMetadata);
        Assert.True(Assert.Single(native.Value).MatchedNativeText);
        Assert.True(Assert.Single(ocr.Value).MatchedOcrText);
    }

    /// <summary>Verifies German suffix variants and diacritic folding match deterministic native-text terms.</summary>
    [Fact]
    public async Task SemanticSearch_GermanDiacriticsAndSuffixes_MatchNormalizedTerms()
    {
        var configuration = new Configuration(enabled: true);
        var embedding = new FeatureHashingEmbeddingProvider();
        var store = new MemoryIndexStore();
        await store.ReplaceAsync(
            [
                Entry(
                    "C:\\Docs\\muenchen-2026.pdf",
                    [],
                    [],
                    ["rechnung", "munchen", "2026"],
                    embedding,
                    DateTimeOffset.UnixEpoch),
            ],
            CancellationToken.None);
        var service = new SemanticSearchService(configuration, embedding, store);

        var result = await service.SearchAsync("Münchner Rechnungen 2026", CancellationToken.None);

        var hit = Assert.Single(result.Value);
        Assert.True(hit.MatchedNativeText);
        Assert.Contains("native text", hit.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies tag normalization folds diacritics while preserving display text and provenance.</summary>
    [Fact]
    public void ProvenanceTagGenerator_Diacritics_UsesSearchableNormalizedValue()
    {
        var tags = ProvenanceTagGenerator.Generate(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "München", "bericht.pdf")),
            "1:2",
            [new ExtractedMetadataField("Keywords", "Büro", ContentProvenance.EmbeddedMetadata)],
            null,
            null,
            DateTimeOffset.UnixEpoch);

        Assert.Contains(tags, tag => tag.DisplayName == "Büro" && tag.NormalizedValue == "buro");
    }

    /// <summary>Verifies disabled semantic search performs no index-store read.</summary>
    [Fact]
    public async Task SemanticSearch_Disabled_DoesNotReadIndex()
    {
        var store = new MemoryIndexStore();
        var service = new SemanticSearchService(
            new Configuration(enabled: false),
            new FeatureHashingEmbeddingProvider(),
            store);

        var result = await service.SearchAsync("documents", CancellationToken.None);

        Assert.Equal(SemanticState.Disabled, result.State);
        Assert.Equal(0, store.ListCount);
    }

    /// <summary>Verifies incremental indexing reuses unchanged entries, reindexes tag changes, and removes deleted files.</summary>
    [Fact]
    public async Task SemanticIndexer_IncrementalRefresh_ReusesChangesAndRemovesDeleted()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("invoice.pdf");
        await File.WriteAllTextAsync(path, "known");
        var content = new MemoryContentStore();
        content.Records.Add(Record(path, "5:0", [Tag("tax", TagSource.UserApproved, TagAcceptanceState.Accepted, 1)]));
        var embedding = new CountingEmbeddingProvider();
        var indexStore = new MemoryIndexStore();
        var indexer = new SemanticIndexer(
            new Configuration(enabled: true),
            content,
            embedding,
            indexStore,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        Assert.Equal(SemanticState.Ready, (await indexer.BuildAsync(false, null, CancellationToken.None)).State);
        Assert.Equal(1, embedding.CallCount);
        await indexer.BuildAsync(false, null, CancellationToken.None);
        Assert.Equal(1, embedding.CallCount);

        content.Records[0] = content.Records[0] with
        {
            Tags =
            [
                Tag("tax", TagSource.UserApproved, TagAcceptanceState.Rejected, 1) with
                {
                    UpdatedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1),
                },
            ],
        };
        await indexer.BuildAsync(false, null, CancellationToken.None);
        Assert.Equal(2, embedding.CallCount);

        File.Delete(path);
        var removed = await indexer.BuildAsync(false, null, CancellationToken.None);
        Assert.Equal(SemanticState.Empty, removed.State);
        Assert.Empty(await indexStore.ListAsync(CancellationToken.None));
    }

    /// <summary>Verifies cancellation does not publish a partial replacement.</summary>
    [Fact]
    public async Task SemanticIndexer_Cancelled_DoesNotPublishPartialIndex()
    {
        using var temporary = new TemporaryDirectory();
        var first = temporary.PathFor("one.txt");
        var second = temporary.PathFor("two.txt");
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");
        var content = new MemoryContentStore();
        content.Records.Add(Record(first, "3:0", []));
        content.Records.Add(Record(second, "3:0", []));
        var store = new MemoryIndexStore();
        var original = Entry(
            first,
            [],
            [],
            [],
            new FeatureHashingEmbeddingProvider(),
            DateTimeOffset.UnixEpoch);
        await store.ReplaceAsync([original], CancellationToken.None);
        var indexer = new SemanticIndexer(
            new Configuration(enabled: true),
            content,
            new FeatureHashingEmbeddingProvider(),
            store);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<SemanticIndexProgress>(_ => cancellation.Cancel());

        var result = await indexer.BuildAsync(true, progress, cancellation.Token);

        Assert.Equal(SemanticState.Cancelled, result.State);
        Assert.Equal([original], await store.ListAsync(CancellationToken.None));
    }

    /// <summary>Verifies malformed local index JSON recovers as an empty rebuildable index.</summary>
    [Fact]
    public async Task JsonSemanticIndexStore_CorruptIndex_RecoversEmpty()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.PathFor("semantic.json");
        await File.WriteAllTextAsync(path, "{ corrupt");
        var store = new JsonSemanticIndexStore(path, new Logging());

        Assert.Empty(await store.ListAsync(CancellationToken.None));

        var entry = Entry(
            temporary.PathFor("known.txt"),
            [],
            [],
            [],
            new FeatureHashingEmbeddingProvider(),
            DateTimeOffset.UnixEpoch);
        await store.ReplaceAsync([entry], CancellationToken.None);
        Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Empty(Directory.GetFiles(temporary.Path, "*.tmp"));
    }

    private static SemanticIndexEntry Entry(
        string path,
        IReadOnlyList<TagAssociation> tags,
        IReadOnlyList<string> metadata,
        IReadOnlyList<string> native,
        IEmbeddingProvider embedding,
        DateTimeOffset time,
        IReadOnlyList<string>? ocr = null) => new(
            Path.GetFullPath(path),
            "source",
            Guid.NewGuid().ToString("N"),
            Path.GetFileName(path),
            tags,
            metadata,
            native,
            ocr ?? [],
            embedding.Embed($"{Path.GetFileName(path)} {string.Join(' ', metadata)} {string.Join(' ', native)} {string.Join(' ', ocr ?? [])}"),
            time);

    private static TagAssociation Tag(
        string value,
        TagSource source,
        TagAcceptanceState state,
        double confidence) => new(
            $"tag:{source}:{value}",
            "file",
            value,
            value,
            "Test",
            source,
            state,
            "Test provenance",
            DateTimeOffset.UnixEpoch)
        {
            Confidence = confidence,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            ProvenanceDetails = "Test provenance",
            SourceFingerprint = "source",
        };

    private static ContentRecord Record(
        string path,
        string fingerprint,
        IReadOnlyList<TagAssociation> tags)
    {
        var parts = fingerprint.Split(':');
        return new ContentRecord(
            Path.GetFullPath(path),
            long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            [new ExtractedMetadataField("Keywords", "invoice tax", ContentProvenance.EmbeddedMetadata)],
            "native invoice",
            "ocr receipt",
            OcrStatus.Completed,
            "fake",
            [])
        {
            Tags = tags,
        };
    }

    private sealed class Configuration(bool enabled) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new()
        {
            SemanticSearch = new SemanticSearchSettings { Enabled = enabled },
        };
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingEmbeddingProvider : IEmbeddingProvider
    {
        private readonly FeatureHashingEmbeddingProvider _inner = new();
        public int CallCount { get; private set; }
        public int Dimensions => _inner.Dimensions;
        public IReadOnlyList<float> Embed(string text)
        {
            CallCount++;
            return _inner.Embed(text);
        }
    }

    private sealed class MemoryIndexStore : ISemanticIndexStore
    {
        private IReadOnlyList<SemanticIndexEntry> _entries = [];
        public int ListCount { get; private set; }
        public Task<IReadOnlyList<SemanticIndexEntry>> ListAsync(CancellationToken cancellationToken)
        {
            ListCount++;
            return Task.FromResult(_entries);
        }
        public Task ReplaceAsync(IReadOnlyList<SemanticIndexEntry> entries, CancellationToken cancellationToken)
        {
            _entries = Array.AsReadOnly(entries.ToArray());
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            _entries = [];
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryContentStore : IContentStore
    {
        public List<ContentRecord> Records { get; } = [];
        public Task<ContentRecord?> GetAsync(string fullPath, CancellationToken cancellationToken) =>
            Task.FromResult(Records.FirstOrDefault(record => record.FullPath == fullPath));
        public Task<IReadOnlyList<ContentRecord>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContentRecord>>(Records.ToArray());
        public Task UpsertAsync(ContentRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveMissingAsync(IReadOnlyCollection<string> knownPaths, CancellationToken cancellationToken)
        {
            Records.RemoveAll(record => !knownPaths.Contains(record.FullPath));
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            Records.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }

    private sealed class Logging : ILoggingService
    {
        public void Initialize(LogLevel minimumLevel) { }
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void Dispose() { }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"opensorse-semantic-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public string PathFor(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
