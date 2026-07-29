using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.Indexing;
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
        using var viewModel = new SemanticSearchViewModel(
            new Configuration(true),
            new Indexer(),
            new Search([]),
            store,
            new Launcher());

        viewModel.RequestClearIndexCommand.Execute(null);
        Assert.True(viewModel.IsClearPending);
        Assert.Equal(0, store.ClearCount);

        await viewModel.ConfirmClearIndexCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsClearPending);
        Assert.Equal(1, store.ClearCount);
        Assert.Contains("Source files were not changed", viewModel.Status.Message, StringComparison.Ordinal);
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

    private static SemanticSearchHit Hit(string path) => new(
        path,
        CrossPlatformPath.GetFileName(path),
        100,
        "Matched tags: tax",
        ["tax"],
        false,
        false,
        false);

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

    private sealed class BackgroundIndexing : IBackgroundIndexingService
    {
        private readonly IndexingSource _source =
            new("source", "C:\\Docs", "Documents", IndexingLevel.Standard, true, true, 0, []);

        public event EventHandler<IndexingProgressSnapshot>? ProgressChanged;

        public IndexingProgressSnapshot Progress { get; set; } = new();

        public IReadOnlyList<IndexingFailure> Failures { get; set; } = [];

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
