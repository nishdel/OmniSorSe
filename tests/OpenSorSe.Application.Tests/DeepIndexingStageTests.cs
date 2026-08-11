using OpenSorSe.Application.Content;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Scanner;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Validates bounded production stage processing and physical discovery policy.</summary>
public sealed class DeepIndexingStageTests
{
    /// <summary>Verifies link-like file observations stop safely before content processing.</summary>
    [Fact]
    public async Task ReparsePointStopsFileAtDiscovery()
    {
        var processor = CreateProcessor();
        var work = Work(IndexingStage.FileDiscovered) with
        {
            Observation = Observation() with { Attributes = FileAttributes.ReparsePoint },
        };

        var output = await processor.ProcessAsync(work, new DeepIndexingSettings());

        Assert.Equal(IndexingStageStatus.Skipped, output.Status);
        Assert.True(output.StopsFile);
        Assert.Equal("symbolic-link-not-followed", output.ErrorCode);
    }

    /// <summary>Verifies successful content fingerprint output remains normalized.</summary>
    [Fact]
    public async Task FingerprintStageReturnsSha256Value()
    {
        var processor = CreateProcessor(hasher: new FakeHasher(hash: "abc123"));

        var output = await processor.ProcessAsync(
            Work(IndexingStage.ContentFingerprinted),
            new DeepIndexingSettings());

        Assert.Equal(IndexingStageStatus.Complete, output.Status);
        Assert.Equal("abc123", output.ContentHash);
    }

    /// <summary>Verifies file hashing issues map to durable privacy-safe categories.</summary>
    [Theory]
    [InlineData(FileHashIssueKind.AccessDenied, IndexingFailureCategory.PermissionDenied, false)]
    [InlineData(FileHashIssueKind.FileUnavailable, IndexingFailureCategory.NotFound, false)]
    [InlineData(FileHashIssueKind.FileChangedDuringHashing, IndexingFailureCategory.FileChanged, true)]
    [InlineData(FileHashIssueKind.FileUnreadable, IndexingFailureCategory.FileLocked, true)]
    public async Task FingerprintIssuesMapToDurableFailurePolicy(
        FileHashIssueKind issue,
        IndexingFailureCategory expected,
        bool retryable)
    {
        var processor = CreateProcessor(hasher: new FakeHasher(issue: issue));

        var output = await processor.ProcessAsync(
            Work(IndexingStage.ContentFingerprinted),
            new DeepIndexingSettings());

        Assert.Equal(expected, output.FailureCategory);
        Assert.Equal(retryable, output.IsRetryable);
    }

    /// <summary>Verifies native text is bounded before entering durable storage.</summary>
    [Fact]
    public async Task ExtractedTextIsBoundedForVeryLargeSyntheticDocument()
    {
        var store = new FakeContentStore(Record(nativeText: new string('x', 50_000)));
        var processor = CreateProcessor(contentStore: store);

        var output = await processor.ProcessAsync(
            Work(IndexingStage.TextExtracted),
            new DeepIndexingSettings { MaximumExtractedTextCharacters = 4096 });

        Assert.Equal(IndexingStageStatus.Complete, output.Status);
        Assert.Equal(4096, output.ExtractedText!.Length);
    }

    /// <summary>Verifies OCR unavailability waits instead of permanently failing a file.</summary>
    [Fact]
    public async Task OcrUnavailableWaitsForDependency()
    {
        var store = new FakeContentStore(Record(ocrStatus: OcrStatus.Unavailable));
        var processor = CreateProcessor(contentStore: store, ocrEnabled: true);

        var output = await processor.ProcessAsync(
            Work(IndexingStage.OcrProcessed),
            new DeepIndexingSettings { OcrProcessingEnabled = true });

        Assert.Equal(IndexingStageStatus.WaitingForDependency, output.Status);
        Assert.Equal(IndexingFailureCategory.DependencyUnavailable, output.FailureCategory);
        Assert.Equal("OCR", output.WaitingDependency);
    }

    /// <summary>Verifies OCR output is bounded independently from the legacy content cache.</summary>
    [Fact]
    public async Task OcrTextUsesDurableIndexBound()
    {
        var store = new FakeContentStore(Record(ocrText: new string('o', 20_000), ocrStatus: OcrStatus.Completed));
        var processor = CreateProcessor(contentStore: store, ocrEnabled: true);

        var output = await processor.ProcessAsync(
            Work(IndexingStage.OcrProcessed),
            new DeepIndexingSettings
            {
                OcrProcessingEnabled = true,
                MaximumOcrTextCharacters = 4096,
            });

        Assert.Equal(4096, output.OcrText!.Length);
    }

    /// <summary>Verifies optional local-AI enrichment waits and later completes through the provider abstraction.</summary>
    [Fact]
    public async Task OptionalAiEnrichmentRecoversWhenProviderReturns()
    {
        var enrichment = new FakeEnrichment();
        var processor = CreateProcessor(enrichment: enrichment);
        var work = Work(IndexingStage.SummaryKeywordsGenerated) with
        {
            Level = IndexingLevel.Deep,
            ExtractedText = "quarterly project report",
        };
        var settings = new DeepIndexingSettings { AiProcessingEnabled = true };

        var waiting = await processor.ProcessAsync(work, settings);
        enrichment.Available = true;
        var complete = await processor.ProcessAsync(work, settings);

        Assert.Equal(IndexingStageStatus.WaitingForDependency, waiting.Status);
        Assert.Equal(IndexingStageStatus.Complete, complete.Status);
        Assert.Equal("A local bounded summary.", complete.Summary);
        Assert.Contains("quarterly", complete.Keywords!);
    }

    /// <summary>Verifies Deep selected chunks respect the configured maximum without arbitrary source-size assumptions.</summary>
    [Fact]
    public async Task DeepChunksRespectMaximumPolicy()
    {
        var processor = CreateProcessor();
        var work = Work(IndexingStage.SummaryKeywordsGenerated) with
        {
            Level = IndexingLevel.Deep,
            ExtractedText = new string('z', 100_000),
        };

        var output = await processor.ProcessAsync(
            work,
            new DeepIndexingSettings { MaximumSemanticChunksPerDocument = 3 });

        Assert.Equal(3, output.SelectedChunks!.Count);
        Assert.All(output.SelectedChunks, chunk => Assert.InRange(chunk.Length, 1, 1000));
        Assert.InRange(output.Summary!.Length, 1, 512);
    }

    /// <summary>Verifies empty content still produces a safe filename representation.</summary>
    [Fact]
    public async Task EmptyFileCanCompleteKeywordAndRepresentationStages()
    {
        var processor = CreateProcessor();
        var summary = await processor.ProcessAsync(
            Work(IndexingStage.SummaryKeywordsGenerated) with
            {
                Level = IndexingLevel.Deep,
                ExtractedText = string.Empty,
            },
            new DeepIndexingSettings());
        var semantic = await processor.ProcessAsync(
            Work(IndexingStage.SemanticRepresentationGenerated),
            new DeepIndexingSettings());

        Assert.Empty(summary.SelectedChunks!);
        Assert.Contains("document", summary.Keywords!);
        Assert.Equal(4, semantic.SemanticRepresentation!.Count);
    }

    /// <summary>Verifies summary privacy controls retain no summary or generated keywords.</summary>
    [Fact]
    public async Task SummaryPrivacyControlSuppressesSummaryAndKeywords()
    {
        var processor = CreateProcessor();
        var output = await processor.ProcessAsync(
            Work(IndexingStage.SummaryKeywordsGenerated) with
            {
                Level = IndexingLevel.Deep,
                ExtractedText = "private source document text",
            },
            new DeepIndexingSettings
            {
                SummaryProcessingEnabled = false,
                SemanticProcessingEnabled = true,
            });

        Assert.Null(output.Summary);
        Assert.Empty(output.Keywords!);
        Assert.NotEmpty(output.SelectedChunks!);
    }

    /// <summary>Verifies the related-concept privacy control suppresses vectors and selected chunks.</summary>
    [Fact]
    public async Task SemanticPrivacyControlSuppressesRepresentationsAndChunks()
    {
        var processor = CreateProcessor();
        var settings = new DeepIndexingSettings
        {
            SummaryProcessingEnabled = true,
            SemanticProcessingEnabled = false,
        };
        var summary = await processor.ProcessAsync(
            Work(IndexingStage.SummaryKeywordsGenerated) with
            {
                Level = IndexingLevel.Deep,
                ExtractedText = "private source document text",
            },
            settings);
        var semantic = await processor.ProcessAsync(
            Work(IndexingStage.SemanticRepresentationGenerated),
            settings);

        Assert.Null(summary.SelectedChunks);
        Assert.Equal(IndexingStageStatus.Skipped, semantic.Status);
        Assert.Null(semantic.SemanticRepresentation);
    }

    /// <summary>Verifies metadata changes during queueing are retryable instead of publishing stale state.</summary>
    [Fact]
    public async Task MetadataChangeDuringProcessingSchedulesRetry()
    {
        using var fixture = new TemporaryDirectory();
        var path = fixture.File("changed.txt", "content");
        var processor = CreateProcessor();
        var work = Work(IndexingStage.MetadataIndexed, path) with
        {
            Observation = Observation(path) with { MetadataFingerprint = "outdated" },
        };

        var output = await processor.ProcessAsync(work, new DeepIndexingSettings());

        Assert.Equal(IndexingFailureCategory.FileChanged, output.FailureCategory);
        Assert.True(output.IsRetryable);
    }

    /// <summary>Verifies physical discovery excludes generated folders and configured patterns.</summary>
    [Fact]
    public async Task PhysicalDiscoveryAppliesGeneratedFolderAndPatternExclusions()
    {
        using var fixture = new TemporaryDirectory();
        fixture.File("keep.txt", string.Empty);
        fixture.File("ignore.tmp", "temporary");
        fixture.File(Path.Combine("bin", "generated.dll"), "binary");
        fixture.File(Path.Combine(".opensorse", "duplicate-recovery", "copy.txt"), "recovery");
        var discovery = new PhysicalIndexFileDiscovery(
            FileIdentityProviderFactory.CreateCurrent(),
            PlatformServices.CurrentPathSemantics);
        var source = new IndexingSource(
            "source",
            fixture.Root,
            "Fixture",
            IndexingLevel.Basic,
            true,
            true,
            0,
            ["*.tmp"]);

        var files = await DiscoverAsync(discovery, source, new DeepIndexingSettings());

        Assert.Single(files);
        Assert.Equal("keep.txt", files[0].RelativePath);
        Assert.Equal(0, files[0].Length);
    }

    /// <summary>Verifies an empty physical folder produces a deterministic empty discovery.</summary>
    [Fact]
    public async Task PhysicalDiscoverySupportsEmptyFolder()
    {
        using var fixture = new TemporaryDirectory();
        var discovery = new PhysicalIndexFileDiscovery(
            FileIdentityProviderFactory.CreateCurrent(),
            PlatformServices.CurrentPathSemantics);

        var files = await DiscoverAsync(
            discovery,
            new IndexingSource("source", fixture.Root, "Empty", IndexingLevel.Basic, true, true, 0, []),
            new DeepIndexingSettings());

        Assert.Empty(files);
    }

    /// <summary>Verifies processor fingerprints change when relevant extraction policy changes.</summary>
    [Fact]
    public void ProcessorFingerprintTracksRelevantPolicy()
    {
        var processor = CreateProcessor();

        var first = processor.CreateProcessorFingerprint(new DeepIndexingSettings());
        var second = processor.CreateProcessorFingerprint(new DeepIndexingSettings
        {
            MaximumSemanticChunksPerDocument = 9,
        });
        var privacyChanged = processor.CreateProcessorFingerprint(new DeepIndexingSettings
        {
            SemanticProcessingEnabled = false,
        });

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, privacyChanged);
        Assert.Equal(64, first.Length);
    }

    private static DefaultIndexingStageProcessor CreateProcessor(
        IFileHasher? hasher = null,
        FakeContentStore? contentStore = null,
        IIndexingEnrichmentProvider? enrichment = null,
        bool ocrEnabled = false)
    {
        var store = contentStore ?? new FakeContentStore(Record());
        return new DefaultIndexingStageProcessor(
            new FakeConfiguration(ocrEnabled),
            hasher ?? new FakeHasher(hash: "hash"),
            new FakeContentIndexer(),
            store,
            new FakeEmbedding(),
            enrichment);
    }

    private static IndexingWorkItem Work(IndexingStage stage, string? path = null)
    {
        var actualPath = path ?? Path.Combine(Path.GetTempPath(), "document.txt");
        return new IndexingWorkItem
        {
            JobId = "job",
            RunId = "run",
            FileId = "file",
            SourceId = "source",
            FullPath = actualPath,
            RelativePath = Path.GetFileName(actualPath),
            Level = IndexingLevel.Standard,
            Stage = stage,
            Attempt = 1,
            ProcessorFingerprint = "processor",
            Observation = Observation(actualPath),
        };
    }

    private static IndexingFileObservation Observation(string? path = null) =>
        new(
            path ?? Path.Combine(Path.GetTempPath(), "document.txt"),
            Path.GetFileName(path ?? "document.txt"),
            "stable",
            "volume",
            10,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            FileAttributes.Normal,
            "metadata");

    private static ContentRecord Record(
        string? nativeText = null,
        string? ocrText = null,
        OcrStatus ocrStatus = OcrStatus.Skipped) =>
        new(
            Path.Combine(Path.GetTempPath(), "document.txt"),
            10,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            [],
            nativeText,
            ocrText,
            ocrStatus,
            null,
            []);

    private static async Task<IReadOnlyList<IndexingFileObservation>> DiscoverAsync(
        IIndexFileDiscovery discovery,
        IndexingSource source,
        DeepIndexingSettings settings)
    {
        var files = new List<IndexingFileObservation>();
        await foreach (var file in discovery.DiscoverAsync(source, settings))
        {
            files.Add(file);
        }

        return files;
    }

    private sealed class FakeConfiguration : IConfigurationService
    {
        public FakeConfiguration(bool ocrEnabled)
        {
            Current = new ApplicationSettings
            {
                Content = new ContentSettings { OcrEnabled = ocrEnabled },
            };
        }

        public ApplicationSettings Current { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHasher : IFileHasher
    {
        private readonly string? _hash;
        private readonly FileHashIssueKind? _issue;

        public FakeHasher(string? hash = null, FileHashIssueKind? issue = null)
        {
            _hash = hash;
            _issue = issue;
        }

        public Task<FileHashResult> HashAsync(
            IReadOnlyCollection<FileEntry> files,
            CancellationToken cancellationToken = default)
        {
            var file = files.Single();
            var output = file with
            {
                Hash = _hash is null ? null : new FileHash("SHA-256", _hash),
            };
            var issues = _issue is { } issue
                ? new[] { new FileHashIssue(file.FullPath, issue, "controlled") }
                : [];
            return Task.FromResult(new FileHashResult(
                [output],
                new FileHashStatistics(1, _hash is null ? 0 : 1, 0, issues.Length),
                issues));
        }
    }

    private sealed class FakeContentIndexer : IContentIndexingService
    {
        public Task<ContentIndexingSummary> IndexAsync(
            IReadOnlyCollection<FileEntry> files,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ContentIndexingSummary(files.Count, files.Count, 0, 0, 0, 0));

        public Task<ContentIndexingSummary> IndexAsync(
            IReadOnlyCollection<FileEntry> files,
            ContentIndexingOptions? options,
            CancellationToken cancellationToken) =>
            IndexAsync(files, cancellationToken);
    }

    private sealed class FakeContentStore : IContentStore
    {
        private ContentRecord? _record;

        public FakeContentStore(ContentRecord? record)
        {
            _record = record;
        }

        public Task<ContentRecord?> GetAsync(string fullPath, CancellationToken cancellationToken) =>
            Task.FromResult(_record);

        public Task<IReadOnlyList<ContentRecord>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContentRecord>>(_record is null ? [] : [_record]);

        public Task UpsertAsync(ContentRecord record, CancellationToken cancellationToken)
        {
            _record = record;
            return Task.CompletedTask;
        }

        public Task RemoveMissingAsync(IReadOnlyCollection<string> knownPaths, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            _record = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmbedding : IEmbeddingProvider
    {
        public int Dimensions => 4;

        public IReadOnlyList<float> Embed(string text) => [0.5f, 0.5f, 0.5f, 0.5f];
    }

    private sealed class FakeEnrichment : IIndexingEnrichmentProvider
    {
        public bool Available { get; set; }

        public string Version => "fake-v1";

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Available);

        public Task<IndexingEnrichmentResult> EnrichAsync(
            string fileName,
            string boundedText,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IndexingEnrichmentResult(
                "A local bounded summary.",
                ["quarterly", "report"]));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "OpenSorSe-deep-stage-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string File(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
