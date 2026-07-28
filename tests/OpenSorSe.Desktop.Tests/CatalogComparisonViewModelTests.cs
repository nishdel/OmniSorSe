using OpenSorSe.Application.Catalog;
using OpenSorSe.Application.CatalogComparison;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies bounded, cancellable historical snapshot comparison presentation.</summary>
public sealed class CatalogComparisonViewModelTests
{
    /// <summary>Verifies disabled comparison never reads application-owned catalog data.</summary>
    [Fact]
    public async Task RefreshEntriesAsync_DisabledCatalog_DoesNotReadStore()
    {
        var store = new InMemoryCatalogStore(CreateEntry("one", []));
        using var viewModel = new CatalogComparisonViewModel(
            new TestConfigurationService(enabled: false),
            store,
            new CatalogComparisonService());

        await viewModel.RefreshEntriesAsync();

        Assert.Equal(0, store.ListCallCount);
        Assert.Empty(viewModel.Entries);
        Assert.Contains("disabled", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies success publishes complete statistics, default changed rows, filters, and historical opening.</summary>
    [Fact]
    public async Task CompareAsync_ValidSelections_PublishesFiltersAndOpensLoadedSnapshots()
    {
        var baseline = CreateEntry("baseline", [
            CreateFile("old:same", "/scope/same.txt"),
            CreateFile("old:modified", "/scope/modified.txt", size: 1),
            CreateFile("old:removed", "/scope/removed.txt"),
        ]);
        var current = CreateEntry("current", [
            CreateFile("new:same", "/scope/same.txt"),
            CreateFile("new:modified", "/scope/modified.txt", size: 2),
            CreateFile("new:added", "/scope/added.txt"),
        ]);
        var store = new InMemoryCatalogStore(baseline, current);
        using var viewModel = new CatalogComparisonViewModel(
            new TestConfigurationService(enabled: true),
            store,
            new CatalogComparisonService());
        var opened = new List<CatalogEntry>();
        viewModel.EntryOpened += (_, entry) => opened.Add(entry);

        await viewModel.RefreshEntriesAsync();
        viewModel.BaselineSelection = viewModel.Entries.Single(entry => entry.Id == baseline.Id);
        viewModel.CurrentSelection = viewModel.Entries.Single(entry => entry.Id == current.Id);
        await viewModel.CompareAsync();

        Assert.Equal(3, viewModel.Changes.Count);
        Assert.DoesNotContain(viewModel.Changes, change => change.Kind == CatalogComparisonChangeKind.Unchanged);
        Assert.Contains("added 1", viewModel.StatisticsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scopes match", viewModel.ScopeStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.OpenBaselineCommand.CanExecute(null));
        Assert.True(viewModel.OpenCurrentCommand.CanExecute(null));

        viewModel.SelectedFilter = CatalogComparisonFilter.All;
        Assert.Equal(4, viewModel.Changes.Count);
        viewModel.FilterText = "added";
        Assert.Single(viewModel.Changes);
        viewModel.OpenBaselineCommand.Execute(null);
        viewModel.OpenCurrentCommand.Execute(null);
        Assert.Equal([baseline, current], opened);
    }

    /// <summary>Verifies duplicate selections are rejected without loading snapshots.</summary>
    [Fact]
    public async Task CompareAsync_DuplicateSelection_DoesNotLoadStore()
    {
        var entry = CreateEntry("one", []);
        var store = new InMemoryCatalogStore(entry);
        using var viewModel = new CatalogComparisonViewModel(
            new TestConfigurationService(enabled: true),
            store,
            new CatalogComparisonService());
        await viewModel.RefreshEntriesAsync();
        viewModel.BaselineSelection = Assert.Single(viewModel.Entries);
        viewModel.CurrentSelection = viewModel.BaselineSelection;

        await viewModel.CompareAsync();

        Assert.Equal(0, store.LoadCallCount);
        Assert.Contains("distinct", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CompareCommand.CanExecute(null));
    }

    /// <summary>Verifies maximum presentation is capped while complete aggregate counts and filter validation remain available.</summary>
    [Fact]
    public async Task CompareAsync_LargeResult_CapsRowsAndValidatesFilterText()
    {
        var baseline = CreateEntry("baseline", []);
        var current = CreateEntry("current", Enumerable.Range(0, 600)
            .Select(index => CreateFile($"file:{index}", $"/scope/added-{index:D3}.txt"))
            .ToArray());
        using var viewModel = new CatalogComparisonViewModel(
            new TestConfigurationService(enabled: true),
            new InMemoryCatalogStore(baseline, current),
            new CatalogComparisonService());
        await viewModel.RefreshEntriesAsync();
        viewModel.BaselineSelection = viewModel.Entries.Single(entry => entry.Id == baseline.Id);
        viewModel.CurrentSelection = viewModel.Entries.Single(entry => entry.Id == current.Id);

        await viewModel.CompareAsync();

        Assert.Equal(500, viewModel.Changes.Count);
        Assert.Contains("first 500 of 600", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("added 600", viewModel.StatisticsText, StringComparison.OrdinalIgnoreCase);

        viewModel.FilterText = new string('x', 513);
        Assert.Empty(viewModel.Changes);
        Assert.Contains("512", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("added 600", viewModel.StatisticsText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies explicit cancellation prevents stale service output from being published.</summary>
    [Fact]
    public async Task CompareAsync_ExplicitCancellation_LeavesUsableSelectionsAndNoRows()
    {
        var baseline = CreateEntry("baseline", []);
        var current = CreateEntry("current", []);
        var comparison = new BlockingComparisonService();
        using var viewModel = new CatalogComparisonViewModel(
            new TestConfigurationService(enabled: true),
            new InMemoryCatalogStore(baseline, current),
            comparison);
        await viewModel.RefreshEntriesAsync();
        viewModel.BaselineSelection = viewModel.Entries.Single(entry => entry.Id == baseline.Id);
        viewModel.CurrentSelection = viewModel.Entries.Single(entry => entry.Id == current.Id);

        var work = viewModel.CompareAsync();
        await comparison.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(viewModel.CancelCommand.CanExecute(null));
        viewModel.CancelCommand.Execute(null);
        await work;

        Assert.False(viewModel.IsBusy);
        Assert.Empty(viewModel.Changes);
        Assert.NotNull(viewModel.BaselineSelection);
        Assert.NotNull(viewModel.CurrentSelection);
        Assert.Contains("cancelled", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a selector change cancels active work so old results cannot publish under new choices.</summary>
    [Fact]
    public async Task CompareAsync_SelectionChangesDuringWork_CancelsStalePublication()
    {
        var baseline = CreateEntry("baseline", []);
        var current = CreateEntry("current", []);
        var replacement = CreateEntry("replacement", []);
        var comparison = new BlockingComparisonService();
        using var viewModel = new CatalogComparisonViewModel(
            new TestConfigurationService(enabled: true),
            new InMemoryCatalogStore(baseline, current, replacement),
            comparison);
        await viewModel.RefreshEntriesAsync();
        viewModel.BaselineSelection = viewModel.Entries.Single(entry => entry.Id == baseline.Id);
        viewModel.CurrentSelection = viewModel.Entries.Single(entry => entry.Id == current.Id);

        var work = viewModel.CompareAsync();
        await comparison.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.CurrentSelection = viewModel.Entries.Single(entry => entry.Id == replacement.Id);
        await work;

        Assert.False(viewModel.IsBusy);
        Assert.Empty(viewModel.Changes);
        Assert.Equal(replacement.Id, viewModel.CurrentSelection.Id);
        Assert.Contains("selection changed", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies changed catalog data clears cached entries used by historical open commands.</summary>
    [Fact]
    public async Task InvalidateCatalog_AfterComparison_ClearsRowsAndOpenCommands()
    {
        var baseline = CreateEntry("baseline", []);
        var current = CreateEntry("current", [CreateFile("file:one", "/scope/one.txt")]);
        using var viewModel = new CatalogComparisonViewModel(
            new TestConfigurationService(enabled: true),
            new InMemoryCatalogStore(baseline, current),
            new CatalogComparisonService());
        await viewModel.RefreshEntriesAsync();
        viewModel.BaselineSelection = viewModel.Entries.Single(entry => entry.Id == baseline.Id);
        viewModel.CurrentSelection = viewModel.Entries.Single(entry => entry.Id == current.Id);
        await viewModel.CompareAsync();

        viewModel.InvalidateCatalog();

        Assert.Empty(viewModel.Changes);
        Assert.False(viewModel.OpenBaselineCommand.CanExecute(null));
        Assert.False(viewModel.OpenCurrentCommand.CanExecute(null));
        Assert.Contains("changed", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies store failures remain recoverable and do not publish partial comparison state.</summary>
    [Fact]
    public async Task CompareAsync_LoadFailure_PreservesEmptyComparisonState()
    {
        var baseline = CreateEntry("baseline", []);
        var current = CreateEntry("current", []);
        var store = new InMemoryCatalogStore(baseline, current) { FailLoads = true };
        using var viewModel = new CatalogComparisonViewModel(
            new TestConfigurationService(enabled: true),
            store,
            new CatalogComparisonService());
        await viewModel.RefreshEntriesAsync();
        viewModel.BaselineSelection = viewModel.Entries.Single(entry => entry.Id == baseline.Id);
        viewModel.CurrentSelection = viewModel.Entries.Single(entry => entry.Id == current.Id);

        await viewModel.CompareAsync();

        Assert.Empty(viewModel.Changes);
        Assert.Contains("could not be compared", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private static CatalogEntry CreateEntry(string name, IReadOnlyList<ResultFile> files) => new(
        $"catalog:{name}",
        DateTimeOffset.UnixEpoch,
        new ResultsSnapshot(
            $"session:{name}",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            files,
            [],
            [],
            [],
            [],
            new ResultsSnapshotStatistics(files.Count, 0, 0, 0, 0, 0, 0),
            true),
        [])
    {
        DisplayName = name,
        SourceRoots = ["/scope"],
    };

    private static ResultFile CreateFile(string id, string path, long size = 1) => new(
        id,
        path,
        CrossPlatformPath.GetFileName(path),
        Path.GetExtension(path),
        size,
        DateTimeOffset.UnixEpoch,
        FileCategory.Document,
        "Document",
        DuplicateStatus.Unique,
        null,
        false);

    private sealed class TestConfigurationService : IConfigurationService
    {
        public TestConfigurationService(bool enabled)
        {
            Current = new ApplicationSettings { Catalog = new CatalogSettings { Enabled = enabled } };
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

    private sealed class InMemoryCatalogStore : IResultsCatalogStore
    {
        private readonly List<CatalogEntry> _entries;

        public InMemoryCatalogStore(params CatalogEntry[] entries)
        {
            _entries = entries.ToList();
        }

        public int ListCallCount { get; private set; }

        public int LoadCallCount { get; private set; }

        public bool FailLoads { get; set; }

        public Task<IReadOnlyList<CatalogEntrySummary>> ListAsync(CancellationToken cancellationToken)
        {
            ListCallCount++;
            return Task.FromResult<IReadOnlyList<CatalogEntrySummary>>(_entries.Select(ToSummary).ToArray());
        }

        public Task<CatalogEntry?> LoadAsync(string entryId, CancellationToken cancellationToken)
        {
            LoadCallCount++;
            if (FailLoads)
            {
                throw new InvalidDataException("Test catalog failure.");
            }

            return Task.FromResult<CatalogEntry?>(_entries.FirstOrDefault(entry => entry.Id == entryId));
        }

        public Task<CatalogEntrySummary> SaveAsync(CatalogEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(string entryId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ClearAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        private static CatalogEntrySummary ToSummary(CatalogEntry entry) => new(
            entry.Id,
            entry.SavedAtUtc,
            entry.Snapshot.Files.Count,
            entry.Snapshot.Directories.Count,
            0,
            0)
        {
            DisplayName = entry.DisplayName,
            SourceRoots = entry.SourceRoots,
        };
    }

    private sealed class BlockingComparisonService : ICatalogComparisonService
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CatalogComparisonResult Compare(CatalogEntry baseline, CatalogEntry current, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation should have interrupted the test comparison.");
        }
    }
}
