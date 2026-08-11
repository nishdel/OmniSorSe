using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies Semantic Search Beta presentation state, confirmation, cancellation, and safe shell opening.</summary>
public sealed class SemanticSearchViewModelTests
{
    /// <summary>Verifies explained local results are published without exposing vectors.</summary>
    [Fact]
    public async Task Search_Enabled_PublishesExplainedHits()
    {
        var hit = Hit("C:\\Docs\\tax.pdf");
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([hit]),
            new Store(),
            new Launcher());
        viewModel.QueryText = "tax documents";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(hit, Assert.Single(viewModel.Hits));
        Assert.True(viewModel.HasHits);
        Assert.Equal(StatusKind.Success, viewModel.Status.Kind);
    }

    /// <summary>Verifies index deletion requires confirmation and never targets source files.</summary>
    [Fact]
    public async Task ClearIndex_RequiresConfirmation_ThenClearsOwnedStore()
    {
        var store = new Store();
        var background = new BackgroundIndexing();
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([]),
            store,
            new Launcher(),
            background);

        viewModel.RequestClearIndexCommand.Execute(null);
        Assert.True(viewModel.IsClearPending);
        Assert.Equal(0, store.ClearCount);

        await viewModel.ConfirmClearIndexCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsClearPending);
        Assert.Equal(1, store.ClearCount);
        Assert.Equal(1, background.ForgetSourceCount);
        Assert.Contains("source files were not changed", viewModel.Status.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies active indexing propagates explicit cancellation.</summary>
    [Fact]
    public async Task BuildIndex_Cancel_PropagatesToIndexer()
    {
        var indexer = new Indexer { Block = true };
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            indexer,
            new Search([]),
            new Store(),
            new Launcher());

        var running = viewModel.BuildIndexCommand.ExecuteAsync(null);
        await indexer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.CancelCommand.Execute(null);
        await running;

        Assert.True(indexer.WasCancelled);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>Verifies a forged row cannot route an unknown path into the launcher.</summary>
    [Fact]
    public async Task OpenFile_UnknownHit_IsRejectedBeforeLauncher()
    {
        var known = Hit("C:\\Docs\\known.pdf");
        var launcher = new Launcher();
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([known]),
            new Store(),
            launcher);
        viewModel.QueryText = "known";
        await viewModel.SearchCommand.ExecuteAsync(null);

        var forged = Hit("C:\\Outside\\forged.pdf");
        Assert.False(viewModel.OpenFileCommand.CanExecute(forged));
        await viewModel.OpenFileCommand.ExecuteAsync(forged);

        Assert.Empty(launcher.Opened);
    }

    /// <summary>Verifies durable progress, coverage, storage, estimates, and source controls remain understandable.</summary>
    [Fact]
    public async Task RefreshBackgroundIndexing_PublishesCompletePlainLanguageProgress()
    {
        var background = new BackgroundIndexing
        {
            Progress = new IndexingProgressSnapshot
            {
                RunId = "run-1",
                Status = IndexingRunStatus.Running,
                CurrentStage = IndexingStage.TextExtracted,
                CurrentFile = "report.pdf",
                TotalDiscovered = 10,
                Processed = 4,
                Completed = 3,
                Skipped = 1,
                Failed = 0,
                Waiting = 1,
                RetryScheduled = 1,
                FilesPerSecond = 2.5,
                IndexSizeBytes = 512,
                MaximumIndexSizeBytes = 1024,
                Coverage = new SearchCoverage(10, 10, 4, 1, 2, 3),
            },
        };
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([]),
            new Store(),
            new Launcher(),
            background);

        await viewModel.RefreshAsync();

        Assert.Equal(0.4, viewModel.BackgroundProgressValue);
        Assert.Contains("document text", viewModel.CurrentStageText, StringComparison.Ordinal);
        Assert.Contains("report.pdf", viewModel.CurrentFileText, StringComparison.Ordinal);
        Assert.Contains("4 of 10", viewModel.ProcessedCountText, StringComparison.Ordinal);
        Assert.Contains("6 files remaining", viewModel.RemainingCountText, StringComparison.Ordinal);
        Assert.Contains("Waiting 1", viewModel.OutcomeCountText, StringComparison.Ordinal);
        Assert.Contains("files/second", viewModel.ThroughputText, StringComparison.Ordinal);
        Assert.False(viewModel.HasEstimatedTime);
        Assert.Contains("still being built", viewModel.CoverageText, StringComparison.Ordinal);
        Assert.Contains("B of", viewModel.StorageText, StringComparison.Ordinal);
        Assert.Contains("document text", viewModel.StorageBreakdownText, StringComparison.Ordinal);
        Assert.True(viewModel.PauseIndexingCommand.CanExecute(null));
        Assert.False(viewModel.ResumeIndexingCommand.CanExecute(null));

        background.Progress = background.Progress with
        {
            EstimatedRemaining = TimeSpan.FromMinutes(3),
        };
        await viewModel.RefreshAsync();

        Assert.True(viewModel.HasEstimatedTime);
        Assert.Equal("Estimated time remaining: 3 min", viewModel.EstimatedTimeText);
    }

    /// <summary>Verifies failures are inspectable without full paths and shared redacted diagnostics can be opened.</summary>
    [Fact]
    public async Task BackgroundFailuresAndDiagnosticsAreAccessible()
    {
        var background = new BackgroundIndexing
        {
            Failures =
            [
                new IndexingFailure(
                    "run-1",
                    "locked.txt",
                    IndexingStage.ContentFingerprinted,
                    IndexingFailureCategory.FileLocked,
                    "file-locked",
                    2,
                    DateTimeOffset.UnixEpoch,
                    CanRetry: true),
            ],
        };
        var diagnostics = new DiagnosticsWindow();
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([]),
            new Store(),
            new Launcher(),
            background,
            diagnostics);

        await viewModel.RefreshAsync();
        viewModel.OpenIndexingDiagnosticsCommand.Execute(null);

        var failure = Assert.Single(viewModel.IndexingFailures);
        Assert.Equal("locked.txt", failure.FileName);
        Assert.DoesNotContain("C:\\", failure.FileName, StringComparison.Ordinal);
        Assert.True(viewModel.HasIndexingFailures);
        Assert.Contains("full paths are not shown", viewModel.FailureSummaryText, StringComparison.Ordinal);
        Assert.True(diagnostics.WasShown);
    }

    /// <summary>Verifies interpreted filters remain visible and can be removed without silent reinterpretation.</summary>
    [Fact]
    public async Task InterpretedFiltersAreVisibleRemovableAndClearable()
    {
        var filter = new SearchFilter(
            "FileType:pdf",
            SearchFilterKind.FileType,
            "pdf",
            "File type: pdf");
        var search = new InterpretingSearch([filter]);
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            search,
            new Store(),
            new Launcher());
        viewModel.QueryText = "PDF tax records";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasActiveFilters);
        Assert.True(viewModel.AreFiltersVisible);
        Assert.Equal(filter, Assert.Single(viewModel.ActiveFilters));
        Assert.True(viewModel.RemoveFilterCommand.CanExecute(filter));

        await viewModel.RemoveFilterCommand.ExecuteAsync(filter);

        Assert.False(viewModel.HasActiveFilters);
        Assert.NotNull(search.LastRequest);
        Assert.False(search.LastRequest.InterpretFilters);
        Assert.Empty(search.LastRequest.ActiveFilters!);
    }

    /// <summary>Verifies graph context is independently controllable and its partial coverage remains visible.</summary>
    [Fact]
    public async Task KnowledgeGraphContextToggleAndCoverageAreIndependent()
    {
        var search = new InterpretingSearch([])
        {
            GraphCoverage = new GraphProjectionCoverage(
                true,
                true,
                false,
                false,
                7,
                10,
                0,
                0,
                "manifest-1",
                3,
                "Projection continues."),
        };
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            search,
            new Store(),
            new Launcher())
        {
            QueryText = "invoice",
            IncludeRelationshipContext = true,
            IncludeGraphContext = false,
        };

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.NotNull(search.LastRequest);
        Assert.True(search.LastRequest.IncludeRelationshipContext);
        Assert.False(search.LastRequest.IncludeGraphContext);
        Assert.Contains("disabled", viewModel.GraphCoverageText, StringComparison.OrdinalIgnoreCase);

        viewModel.IncludeGraphContext = true;
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(search.LastRequest.IncludeGraphContext);
        Assert.Contains("7 of 10", viewModel.GraphCoverageText, StringComparison.Ordinal);
        Assert.Contains("partial", viewModel.GraphCoverageText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies explicit local-AI settings flow into one request and publish a safe outcome.</summary>
    [Fact]
    public async Task AiAssistanceIsOptionalPerSearchAndPublishesFallbackState()
    {
        var search = new InterpretingSearch([])
        {
            AiAssistance = new AiSearchAssistanceResult(
                AiSearchAssistanceState.Unavailable,
                "Ollama is unavailable. Deterministic local ordering was preserved.",
                0,
                false),
        };
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true, aiSearchEnabled: true),
            new Indexer(),
            search,
            new Store(),
            new Launcher())
        {
            QueryText = "raspberry pi setup",
        };

        Assert.True(viewModel.IsAiAssistanceAvailable);
        Assert.False(viewModel.UseAiAssistance);
        viewModel.UseAiAssistance = true;
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(search.LastRequest!.UseAiAssistance);
        Assert.Contains("Deterministic local ordering", viewModel.AiAssistanceText, StringComparison.Ordinal);
    }

    /// <summary>Verifies discovery reports indeterminate progress instead of a fabricated zero-percent total.</summary>
    [Fact]
    public async Task ActiveDiscoveryWithUnknownTotalIsIndeterminate()
    {
        var background = new BackgroundIndexing
        {
            Progress = new IndexingProgressSnapshot
            {
                RunId = "discovering",
                Status = IndexingRunStatus.Running,
                TotalDiscovered = 0,
                Processed = 2,
            },
        };
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([]),
            new Store(),
            new Launcher(),
            background);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsBackgroundProgressIndeterminate);
        Assert.Contains("determining the total", viewModel.ProcessedCountText, StringComparison.Ordinal);
        Assert.Contains("not yet known", viewModel.RemainingCountText, StringComparison.Ordinal);
    }

    /// <summary>Verifies a known result path uses the existing cross-platform clipboard boundary.</summary>
    [Fact]
    public async Task CopyFullPathUsesClipboardBoundary()
    {
        var hit = Hit("C:\\Docs\\tax records.pdf");
        var clipboard = new Clipboard();
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([hit]),
            new Store(),
            new Launcher(),
            clipboard: clipboard)
        {
            QueryText = "tax",
        };
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.CopyFullPathCommand.ExecuteAsync(hit);

        Assert.Equal(hit.FullPath, clipboard.Text);
        Assert.Contains("copied", viewModel.Status.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies platform clipboard failure remains actionable and does not escape the command.</summary>
    [Fact]
    public async Task CopyFullPathFailureProducesActionableFallback()
    {
        var hit = Hit("C:\\Docs\\tax records.pdf");
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([hit]),
            new Store(),
            new Launcher(),
            clipboard: new Clipboard(shouldFail: true))
        {
            QueryText = "tax",
        };
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.CopyFullPathCommand.ExecuteAsync(hit);

        Assert.Equal(StatusKind.Warning, viewModel.Status.Kind);
        Assert.Contains("copy it manually", viewModel.Status.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies privacy inspection reports categories without exposing raw vectors or text.</summary>
    [Fact]
    public async Task InspectIndexedDataShowsCategoriesAndPlainLanguageSemanticPresence()
    {
        var hit = Hit("C:\\Docs\\private.pdf", "file-1");
        var background = new BackgroundIndexing
        {
            PrivacyItem = Privacy("file-1"),
        };
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([hit]),
            new Store(),
            new Launcher(),
            background);
        viewModel.QueryText = "private";
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.InspectIndexedDataCommand.ExecuteAsync(hit);

        Assert.True(viewModel.HasPrivacyItem);
        Assert.Contains("related-concept data stored", viewModel.PrivacyText, StringComparison.Ordinal);
        Assert.DoesNotContain("0.5", viewModel.PrivacyText, StringComparison.Ordinal);
        Assert.DoesNotContain("private extracted paragraph", viewModel.PrivacyText, StringComparison.Ordinal);
    }

    /// <summary>Verifies forgetting indexed data requires confirmation and never deletes the source file.</summary>
    [Fact]
    public async Task ForgetFileRequiresConfirmationAndLeavesOriginalFileUntouched()
    {
        var hit = Hit("C:\\Docs\\private.pdf", "file-1");
        var background = new BackgroundIndexing
        {
            PrivacyItem = Privacy("file-1"),
        };
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([hit]),
            new Store(),
            new Launcher(),
            background);
        viewModel.QueryText = "private";
        await viewModel.SearchCommand.ExecuteAsync(null);
        await viewModel.InspectIndexedDataCommand.ExecuteAsync(hit);

        viewModel.RequestForgetFileCommand.Execute(null);
        Assert.True(viewModel.IsForgetFilePending);
        Assert.Equal(0, background.ForgetFileCount);

        await viewModel.ConfirmForgetFileCommand.ExecuteAsync(null);

        Assert.Equal(1, background.ForgetFileCount);
        Assert.False(viewModel.IsForgetFilePending);
        Assert.False(viewModel.HasPrivacyItem);
        Assert.Empty(viewModel.Hits);
        Assert.Contains("original file was not changed", viewModel.Status.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies index-only clear and targeted repair commands route through provider-neutral contracts.</summary>
    [Fact]
    public async Task PrivacyAndRepairCommandsUseProviderNeutralService()
    {
        var hit = Hit("C:\\Docs\\private.pdf", "file-1");
        var background = new BackgroundIndexing
        {
            PrivacyItem = Privacy("file-1"),
        };
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([hit]),
            new Store(),
            new Launcher(),
            background);
        viewModel.QueryText = "private";
        await viewModel.SearchCommand.ExecuteAsync(null);
        await viewModel.InspectIndexedDataCommand.ExecuteAsync(hit);

        await viewModel.ClearSemanticDataCommand.ExecuteAsync(null);
        await viewModel.ReindexFileCommand.ExecuteAsync(null);
        await viewModel.VerifyFileCommand.ExecuteAsync(null);
        await viewModel.RefreshMetadataCommand.ExecuteAsync(null);
        await viewModel.RefreshTextCommand.ExecuteAsync(null);
        await viewModel.RefreshOcrCommand.ExecuteAsync(null);
        await viewModel.RegenerateSummaryCommand.ExecuteAsync(null);
        await viewModel.RegenerateSemanticCommand.ExecuteAsync(null);

        Assert.Equal(
            IndexedDataKind.SemanticData | IndexedDataKind.Chunks,
            background.LastClearedData);
        Assert.Equal(
            [
                IndexRepairKind.Rebuild,
                IndexRepairKind.Verify,
                IndexRepairKind.RefreshMetadata,
                IndexRepairKind.RefreshText,
                IndexRepairKind.RefreshOcr,
                IndexRepairKind.RegenerateSummaryAndKeywords,
                IndexRepairKind.RegenerateSemanticData,
            ],
            background.FileRepairs);
    }

    /// <summary>Verifies incomplete coverage identifies exclusions, dependency waits, and failures.</summary>
    [Fact]
    public async Task CoverageExplainsSpecificIncompleteReasons()
    {
        var background = new BackgroundIndexing
        {
            Progress = new IndexingProgressSnapshot
            {
                Coverage = new SearchCoverage(10, 10, 7, 2, 3, 6)
                {
                    ExcludedSourceCount = 2,
                    WaitingForOcrCount = 1,
                    WaitingForAiCount = 1,
                    FailedStageCount = 3,
                },
            },
        };
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([]),
            new Store(),
            new Launcher(),
            background);

        await viewModel.RefreshAsync();

        Assert.Contains("exclusion", viewModel.CoverageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OCR", viewModel.CoverageText, StringComparison.Ordinal);
        Assert.Contains("local-AI", viewModel.CoverageText, StringComparison.Ordinal);
        Assert.Contains("failed", viewModel.CoverageText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies an older Search cannot replace results after the query text changes.</summary>
    [Fact]
    public async Task QueryChangeDiscardsStaleInFlightResults()
    {
        var search = new DeferredSearch();
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            search,
            new Store(),
            new Launcher());
        viewModel.QueryText = "old query";
        var running = viewModel.SearchCommand.ExecuteAsync(null);
        await search.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.QueryText = "new query";
        search.Complete(Hit("C:\\Docs\\stale.pdf", "stale"));
        await running;

        Assert.Empty(viewModel.Hits);
        Assert.Equal("new query", viewModel.QueryText);
    }

    private static SemanticSearchHit Hit(string path, string? fileId = null) => new(
        path,
        CrossPlatformPath.GetFileName(path),
        100,
        "Matched tags: tax",
        ["tax"],
        false,
        false,
        false,
        fileId);

    private static IndexPrivacyItem Privacy(string fileId) => new()
    {
        FileId = fileId,
        SourceId = "source",
        SourceName = "Documents",
        SourceRootPath = "C:\\Docs",
        FileName = "private.pdf",
        RelativePath = "private.pdf",
        IndexingLevel = IndexingLevel.Deep,
        MetadataBytes = 100,
        ExtractedTextCharacters = 200,
        OcrTextCharacters = 50,
        HasSummary = true,
        KeywordCount = 4,
        HasSemanticData = true,
        ChunkCount = 2,
        IsFullyIndexed = true,
        LastIndexedUtc = DateTimeOffset.UnixEpoch,
    };

    private sealed class Configuration(bool enabled, bool aiSearchEnabled = false) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new()
        {
            SemanticSearch = new SemanticSearchSettings { Enabled = enabled },
            Ai = new AiSettings
            {
                Enabled = aiSearchEnabled,
                SearchAssistanceEnabled = aiSearchEnabled,
                SelectedModel = aiSearchEnabled ? "small-local" : null,
            },
        };
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class Indexer : ISemanticIndexer
    {
        public bool Block { get; init; }
        public bool WasCancelled { get; private set; }
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SemanticResult<int>> BuildAsync(
            bool rebuild,
            IProgress<SemanticIndexProgress>? progress,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            if (Block)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    WasCancelled = true;
                    return new SemanticResult<int>(SemanticState.Cancelled, "Cancelled", 0);
                }
            }

            progress?.Report(new SemanticIndexProgress(1, 1, "Indexed"));
            return new SemanticResult<int>(SemanticState.Ready, "Ready", 1);
        }
    }

    private sealed class Search(IReadOnlyList<SemanticSearchHit> hits) : ISemanticSearchService
    {
        public Task<SemanticResult<IReadOnlyList<SemanticSearchHit>>> SearchAsync(
            string query,
            CancellationToken cancellationToken) => Task.FromResult(
                new SemanticResult<IReadOnlyList<SemanticSearchHit>>(
                    SemanticState.Ready,
                    "Ready",
                    hits));
    }

    private sealed class DeferredSearch : ISemanticSearchService
    {
        private readonly TaskCompletionSource<SemanticSearchHit> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete(SemanticSearchHit hit) => _completion.TrySetResult(hit);

        public async Task<SemanticResult<IReadOnlyList<SemanticSearchHit>>> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            var result = await SearchAsync(new SearchRequest(query), cancellationToken);
            return new SemanticResult<IReadOnlyList<SemanticSearchHit>>(
                result.State,
                result.Message,
                result.Hits);
        }

        public async Task<SearchExecutionResult> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            var hit = await _completion.Task;
            return new SearchExecutionResult(
                SemanticState.Ready,
                "ready",
                [hit],
                new SearchInterpretation(request.QueryText, request.QueryText, [request.QueryText], []),
                new SearchCoverage(1, 1, 0, 0, 0, 0));
        }
    }

    private sealed class InterpretingSearch(IReadOnlyList<SearchFilter> interpreted) : ISemanticSearchService
    {
        public SearchRequest? LastRequest { get; private set; }

        public GraphProjectionCoverage? GraphCoverage { get; init; }

        public AiSearchAssistanceResult AiAssistance { get; init; } = AiSearchAssistanceResult.NotRequested;

        public Task<SemanticResult<IReadOnlyList<SemanticSearchHit>>> SearchAsync(
            string query,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SemanticResult<IReadOnlyList<SemanticSearchHit>>(
                SemanticState.Ready,
                "Ready",
                []));

        public Task<SearchExecutionResult> SearchAsync(
            SearchRequest request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var filters = request.InterpretFilters ? interpreted : request.ActiveFilters ?? [];
            return Task.FromResult(new SearchExecutionResult(
                SemanticState.Ready,
                "Ready",
                [],
                new SearchInterpretation(
                    request.QueryText,
                    request.TopicTextOverride ?? "tax records",
                    ["tax", "records"],
                    filters),
                new SearchCoverage(0, 0, 0, 0, 0, 0))
            {
                GraphCoverage = GraphCoverage,
                AiAssistance = AiAssistance,
            });
        }
    }

    private sealed class Store : ISemanticIndexStore
    {
        public int ClearCount { get; private set; }
        public Task<IReadOnlyList<SemanticIndexEntry>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SemanticIndexEntry>>([]);
        public Task ReplaceAsync(IReadOnlyList<SemanticIndexEntry> entries, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class Launcher : IExternalFileLauncher
    {
        public List<string> Opened { get; } = [];
        public Task<ExternalLaunchResult> OpenFileAsync(string fullPath, CancellationToken cancellationToken)
        {
            Opened.Add(fullPath);
            return Task.FromResult(ExternalLaunchResult.Success("Opened"));
        }
        public Task<ExternalLaunchResult> OpenContainingFolderAsync(string fullPath, CancellationToken cancellationToken)
        {
            Opened.Add(fullPath);
            return Task.FromResult(ExternalLaunchResult.Success("Opened"));
        }
    }

    private sealed class Clipboard(bool shouldFail = false) : IClipboardService
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shouldFail)
            {
                throw new NotSupportedException("Synthetic clipboard is unavailable.");
            }

            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class BackgroundIndexing : IBackgroundIndexingService, IIndexPrivacyService
    {
        private readonly IndexingSource _source =
            new("source", "C:\\Docs", "Documents", IndexingLevel.Standard, true, true, 0, []);

        public event EventHandler<IndexingProgressSnapshot>? ProgressChanged;

        public IndexingProgressSnapshot Progress { get; set; } = new();

        public IReadOnlyList<IndexingFailure> Failures { get; set; } = [];

        public IndexPrivacyItem? PrivacyItem { get; set; }

        public int ForgetFileCount { get; private set; }

        public int ForgetSourceCount { get; private set; }

        public IndexedDataKind LastClearedData { get; private set; }

        public List<IndexRepairKind> FileRepairs { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> QueueFolderAsync(
            string rootPath,
            IndexingLevel? level = null,
            bool includeSubfolders = true,
            IReadOnlyList<string>? exclusions = null,
            CancellationToken cancellationToken = default) => Task.FromResult("run");

        public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexingSource>>([_source]);

        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CancelAsync(string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> RetryFailedAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task PrioritizeSourceAsync(string sourceId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveSourceAsync(string sourceId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RebuildAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IndexingProgressSnapshot> GetProgressAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Progress);

        public Task<IndexStorageBreakdown> GetStorageBreakdownAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new IndexStorageBreakdown(10, 20, 30, 40, 50, 60, 70, 80, 512, 1024));

        public Task<IReadOnlyList<IndexingFailure>> GetFailuresAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Failures);

        public Task<IndexMaintenanceResult> MaintainAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new IndexMaintenanceResult(
                [],
                new IndexStorageBreakdown(0, 0, 0, 0, 0, 0, 0, 0, 512, 1024),
                true));

        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
            int maximumCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);

        public Task<SearchCoverage> GetCoverageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Progress.Coverage);

        public Task<IndexPrivacyItem?> InspectFileAsync(
            string fileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PrivacyItem);

        public Task<IReadOnlyList<IndexPrivacyItem>> InspectSourceAsync(
            string sourceId,
            int maximumCount,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IndexPrivacyItem>>(
                PrivacyItem is null ? [] : [PrivacyItem]);

        public Task<IndexPrivacyOperationResult> ForgetFileAsync(
            string fileId,
            CancellationToken cancellationToken = default)
        {
            ForgetFileCount++;
            PrivacyItem = null;
            return Task.FromResult(new IndexPrivacyOperationResult(
                true,
                "source",
                1,
                "Indexed data was forgotten. The original file was not changed."));
        }

        public Task<IndexPrivacyOperationResult> ForgetSourceAsync(
            string sourceId,
            CancellationToken cancellationToken = default)
        {
            ForgetSourceCount++;
            return Task.FromResult(new IndexPrivacyOperationResult(
                true,
                sourceId,
                1,
                "Indexed source data was forgotten. Original files were not changed."));
        }

        public Task<IndexPrivacyOperationResult> SetFilePolicyAsync(
            string fileId,
            IndexPrivacyPolicyChange change,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IndexPrivacyOperationResult(
                true,
                "source",
                1,
                "Policy updated. The original file was not changed."));

        public Task<IndexPrivacyOperationResult> ClearFileDataAsync(
            string fileId,
            IndexedDataKind data,
            CancellationToken cancellationToken = default)
        {
            LastClearedData = data;
            return Task.FromResult(new IndexPrivacyOperationResult(
                true,
                "source",
                1,
                "Generated data cleared. The original file was not changed."));
        }

        public Task<IndexPrivacyOperationResult> RepairFileAsync(
            string fileId,
            IndexRepairKind repair,
            CancellationToken cancellationToken = default)
        {
            FileRepairs.Add(repair);
            return Task.FromResult(new IndexPrivacyOperationResult(
                true,
                "source",
                1,
                "Selective repair queued. The original file was not changed."));
        }

        public Task<IndexPrivacyOperationResult> RepairSourceAsync(
            string sourceId,
            IndexRepairKind repair,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IndexPrivacyOperationResult(
                true,
                sourceId,
                1,
                "Selective source repair queued. Original files were not changed."));

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Publish(IndexingProgressSnapshot progress)
        {
            Progress = progress;
            ProgressChanged?.Invoke(this, progress);
        }
    }

    private sealed class DiagnosticsWindow : IAdvancedDiagnosticsWindowService
    {
        public bool WasShown { get; private set; }

        public void Show() => WasShown = true;
    }
}
