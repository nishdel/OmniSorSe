using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
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

    private static SemanticSearchHit Hit(string path) => new(
        path,
        Path.GetFileName(path),
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
}
