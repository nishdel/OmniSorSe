using OpenSorSe.Application.Catalog;
using OpenSorSe.Application.CatalogSearch;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies bounded deterministic metadata search across application-owned saved snapshots.
/// </summary>
public sealed class CatalogSearchViewModelTests
{
    /// <summary>
    /// Verifies accepted tags and filename signals participate in a stable catalog-wide ranking without accessing live files.
    /// </summary>
    [Fact]
    public async Task SearchAsync_AggregatesAcceptedTagsAndFilenameMatchesInStableRankOrder()
    {
        var tagEntry = CreateEntry("catalog:tag", DateTimeOffset.UnixEpoch, [CreateFile("file:tag", "C:\\Saved\\notes.txt")], [
            new TagAssociation("tag:topic", "file:tag", "Finance", "finance", "Topic", TagSource.UserApproved, TagAcceptanceState.Accepted, null, DateTimeOffset.UnixEpoch),
        ]);
        var filenameEntry = CreateEntry("catalog:name", DateTimeOffset.UnixEpoch.AddMinutes(1), [CreateFile("file:name", "C:\\Saved\\finance-summary.pdf")], []);
        var store = new InMemoryCatalogStore(tagEntry, filenameEntry);
        using var viewModel = new CatalogSearchViewModel(new TestConfigurationService(enabled: true), store)
        {
            QueryText = "finance",
        };

        await viewModel.SearchAsync();

        Assert.Equal(2, viewModel.Hits.Count);
        Assert.Equal("finance-summary.pdf", viewModel.Hits[0].FileName);
        Assert.Contains("filename", viewModel.Hits[0].MatchExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("notes.txt", viewModel.Hits[1].FileName);
        Assert.Contains("tag match: Finance", viewModel.Hits[1].MatchExplanation, StringComparison.Ordinal);
        Assert.Equal(1, store.ListCallCount);
        Assert.Equal(2, store.LoadCallCount);
        Assert.Equal(StatusKind.Success, viewModel.Status.Kind);
    }

    /// <summary>Verifies search hits carry the saved snapshot name without changing matching or ranking.</summary>
    [Fact]
    public async Task SearchAsync_NamedSnapshot_PublishesHistoricalIdentityContext()
    {
        var entry = CreateEntry(
            "catalog:named",
            DateTimeOffset.UnixEpoch,
            [CreateFile("file:one", "C:\\Saved\\budget.txt")],
            []) with
        { DisplayName = "Quarterly baseline" };
        using var viewModel = new CatalogSearchViewModel(
            new TestConfigurationService(enabled: true),
            new InMemoryCatalogStore(entry))
        {
            QueryText = "budget",
        };

        await viewModel.SearchAsync();

        var hit = Assert.Single(viewModel.Hits);
        Assert.Equal("Quarterly baseline", hit.CatalogDisplayName);
        Assert.Contains("Quarterly baseline", hit.SnapshotLabel, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies an empty or disabled query does not enumerate application-owned catalog storage.
    /// </summary>
    [Fact]
    public async Task SearchAsync_EmptyOrDisabled_DoesNotReadCatalog()
    {
        var store = new InMemoryCatalogStore(CreateEntry("catalog:one", DateTimeOffset.UnixEpoch, [CreateFile("file:one", "C:\\Saved\\one.txt")], []));
        using var emptyViewModel = new CatalogSearchViewModel(new TestConfigurationService(enabled: true), store);
        using var disabledViewModel = new CatalogSearchViewModel(new TestConfigurationService(enabled: false), store) { QueryText = "one" };

        await emptyViewModel.SearchAsync();
        await disabledViewModel.SearchAsync();

        Assert.Equal(0, store.ListCallCount);
        Assert.Empty(emptyViewModel.Hits);
        Assert.Contains("disabled", disabledViewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the bounded presentation cap and selected-hit opening retain the exact saved entry for shell-owned results display.
    /// </summary>
    [Fact]
    public async Task SearchAndOpenAsync_CapsHitsAndRaisesExactSavedEntry()
    {
        var entry = CreateEntry(
            "catalog:many",
            DateTimeOffset.UnixEpoch,
            Enumerable.Range(0, 205).Select(index => CreateFile($"file:{index}", $"C:\\Saved\\match-{index:D3}.txt")).ToArray(),
            []);
        var store = new InMemoryCatalogStore(entry);
        using var viewModel = new CatalogSearchViewModel(new TestConfigurationService(enabled: true), store) { QueryText = "match" };
        CatalogEntry? opened = null;
        viewModel.EntryOpened += (_, value) => opened = value;

        await viewModel.SearchAsync();
        Assert.Equal(200, viewModel.Hits.Count);
        Assert.Contains("top 200", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StatusKind.Warning, viewModel.Status.Kind);
        viewModel.SelectedHit = viewModel.Hits[0];
        await viewModel.OpenSelectedHitCommand.ExecuteAsync(null);

        Assert.Same(entry, opened);
    }

    /// <summary>
    /// Verifies maintenance-driven invalidation clears historical hit cache before a removed entry can be reopened.
    /// </summary>
    [Fact]
    public async Task InvalidateResults_ClearsCachedHistoricalHits()
    {
        var entry = CreateEntry("catalog:one", DateTimeOffset.UnixEpoch, [CreateFile("file:one", "C:\\Saved\\one.txt")], []);
        var store = new InMemoryCatalogStore(entry);
        using var viewModel = new CatalogSearchViewModel(new TestConfigurationService(enabled: true), store) { QueryText = "one" };

        await viewModel.SearchAsync();
        viewModel.InvalidateResults();

        Assert.Empty(viewModel.Hits);
        Assert.Null(viewModel.SelectedHit);
        Assert.Contains("changed", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies a named query persists without hits and reruns current catalog search over v0.6 user-managed tags.
    /// </summary>
    [Fact]
    public async Task SavedSearch_SaveAndRun_UsesCurrentCatalogAndPersistedUserTags()
    {
        var entry = CreateEntry("catalog:one", DateTimeOffset.UnixEpoch, [CreateFile("file:one", "C:\\Saved\\notes.txt")], [
            new TagAssociation("tag:user:quarterly", "file:one", "Quarterly Review", "quarterly-review", "User", TagSource.UserApproved, TagAcceptanceState.Accepted, null, DateTimeOffset.UnixEpoch),
        ]);
        var savedStore = new InMemorySavedSearchStore();
        using var viewModel = new CatalogSearchViewModel(new TestConfigurationService(enabled: true), new InMemoryCatalogStore(entry), savedStore)
        {
            QueryText = "quarterly-review",
            SavedSearchName = "Quarterly files",
        };

        await viewModel.SaveCurrentSearchCommand.ExecuteAsync(null);

        var saved = Assert.Single(viewModel.SavedSearches);
        Assert.Equal("Quarterly files", saved.Name);
        Assert.Equal("quarterly-review", saved.QueryText);
        Assert.Empty(viewModel.Hits);
        Assert.Null(viewModel.SavedSearchName);

        viewModel.QueryText = "different";
        viewModel.SelectedSavedSearch = saved;
        await viewModel.RunSelectedSavedSearchCommand.ExecuteAsync(null);

        Assert.Equal("quarterly-review", viewModel.QueryText);
        Assert.Single(viewModel.Hits);
        Assert.Contains("tag match", viewModel.Hits[0].MatchExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not persisted", viewModel.SavedSearchStatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies disabling the catalog blocks saving/running but still permits removal of private saved query text.
    /// </summary>
    [Fact]
    public async Task SavedSearch_DisabledCatalog_AllowsMaintenanceButNotSaveOrRun()
    {
        var savedStore = new InMemorySavedSearchStore(new SavedCatalogSearch(
            "saved:one",
            "Private query",
            "private",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch));
        using var viewModel = new CatalogSearchViewModel(new TestConfigurationService(enabled: false), new InMemoryCatalogStore(), savedStore)
        {
            QueryText = "new",
            SavedSearchName = "New",
        };

        await viewModel.RefreshSavedSearchesAsync();
        viewModel.SelectedSavedSearch = Assert.Single(viewModel.SavedSearches);

        Assert.False(viewModel.SaveCurrentSearchCommand.CanExecute(null));
        Assert.False(viewModel.RunSelectedSavedSearchCommand.CanExecute(null));
        Assert.True(viewModel.RemoveSelectedSavedSearchCommand.CanExecute(null));
        await viewModel.RemoveSelectedSavedSearchCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SavedSearches);
        Assert.Equal(1, savedStore.RemoveCallCount);
    }

    /// <summary>
    /// Verifies duplicate names do not replace data and two-step reset can recover an unreadable preset file.
    /// </summary>
    [Fact]
    public async Task SavedSearch_DuplicateAndCorruption_RequireExplicitReset()
    {
        var existing = new SavedCatalogSearch("saved:one", "Finance", "finance", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var savedStore = new InMemorySavedSearchStore(existing);
        using var viewModel = new CatalogSearchViewModel(new TestConfigurationService(enabled: true), new InMemoryCatalogStore(), savedStore)
        {
            QueryText = "other",
            SavedSearchName = "finance",
        };
        await viewModel.RefreshSavedSearchesAsync();

        await viewModel.SaveCurrentSearchAsync();

        Assert.Equal(0, savedStore.SaveCallCount);
        Assert.Contains("already", viewModel.SavedSearchStatusText, StringComparison.OrdinalIgnoreCase);
        savedStore.FailList = true;
        await viewModel.RefreshSavedSearchesAsync();
        Assert.Contains("recover", viewModel.SavedSearchStatusText, StringComparison.OrdinalIgnoreCase);
        viewModel.RequestResetSavedSearchesCommand.Execute(null);
        Assert.True(viewModel.IsSavedSearchResetPending);
        Assert.Equal(0, savedStore.ClearCallCount);
        await viewModel.ConfirmResetSavedSearchesCommand.ExecuteAsync(null);

        Assert.Equal(1, savedStore.ClearCallCount);
        Assert.False(viewModel.IsSavedSearchResetPending);
        Assert.Empty(viewModel.SavedSearches);
        Assert.Contains("not changed", viewModel.SavedSearchStatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies rename preserves preset identity, query, and creation time while clear resets transient results.</summary>
    [Fact]
    public async Task SavedSearch_RenameAndClearQuery_PreserveOwnedDataAndResetSearchState()
    {
        var created = DateTimeOffset.UnixEpoch.AddDays(1);
        var savedStore = new InMemorySavedSearchStore(
            new SavedCatalogSearch("saved:one", "Old name", "invoice", created, created));
        var entry = CreateEntry(
            "catalog:one",
            DateTimeOffset.UnixEpoch,
            [CreateFile("file:one", "C:\\Saved\\invoice.pdf")],
            []);
        using var viewModel = new CatalogSearchViewModel(
            new TestConfigurationService(enabled: true),
            new InMemoryCatalogStore(entry),
            savedStore);
        await viewModel.RefreshSavedSearchesAsync();
        viewModel.SelectedSavedSearch = Assert.Single(viewModel.SavedSearches);
        viewModel.SavedSearchRenameText = "Invoices";

        await viewModel.RenameSelectedSavedSearchCommand.ExecuteAsync(null);

        var renamed = Assert.Single(viewModel.SavedSearches);
        Assert.Equal("saved:one", renamed.Id);
        Assert.Equal("Invoices", renamed.Name);
        Assert.Equal("invoice", renamed.QueryText);
        Assert.Equal(created, renamed.CreatedAtUtc);

        viewModel.QueryText = "invoice";
        await viewModel.SearchAsync();
        Assert.True(viewModel.IsSearchCompleted);
        Assert.Single(viewModel.Hits);

        viewModel.ClearQueryCommand.Execute(null);
        Assert.Null(viewModel.QueryText);
        Assert.Empty(viewModel.Hits);
        Assert.Null(viewModel.SelectedHit);
        Assert.False(viewModel.IsSearchCompleted);
        Assert.False(viewModel.HasNoHits);
    }

    private static CatalogEntry CreateEntry(string id, DateTimeOffset savedAtUtc, IReadOnlyList<ResultFile> files, IReadOnlyList<TagAssociation> tags) => new(
        id,
        savedAtUtc,
        new ResultsSnapshot(
            $"session:{id}",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            files,
            [new ResultDirectory("C:\\Saved", "Saved")],
            [],
            [],
            [],
            new ResultsSnapshotStatistics(files.Count, 1, 0, 0, 0, 0, 0),
            true),
        tags);

    private static ResultFile CreateFile(string id, string path) => new(
        id,
        path,
        Path.GetFileName(path),
        Path.GetExtension(path),
        1,
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
        private readonly IReadOnlyList<CatalogEntry> _entries;

        public InMemoryCatalogStore(params CatalogEntry[] entries)
        {
            _entries = entries;
        }

        public int ListCallCount { get; private set; }

        public int LoadCallCount { get; private set; }

        public Task<IReadOnlyList<CatalogEntrySummary>> ListAsync(CancellationToken cancellationToken)
        {
            ListCallCount++;
            return Task.FromResult<IReadOnlyList<CatalogEntrySummary>>(_entries
                .OrderByDescending(entry => entry.SavedAtUtc)
                .Select(entry => new CatalogEntrySummary(entry.Id, entry.SavedAtUtc, entry.Snapshot.Files.Count, 1, 0, 0))
                .ToArray());
        }

        public Task<CatalogEntry?> LoadAsync(string entryId, CancellationToken cancellationToken)
        {
            LoadCallCount++;
            return Task.FromResult<CatalogEntry?>(_entries.FirstOrDefault(entry => string.Equals(entry.Id, entryId, StringComparison.Ordinal)));
        }

        public Task<CatalogEntrySummary> SaveAsync(CatalogEntry entry, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> RemoveAsync(string entryId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ClearAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class InMemorySavedSearchStore : ISavedCatalogSearchStore
    {
        private readonly List<SavedCatalogSearch> _searches;

        public InMemorySavedSearchStore(params SavedCatalogSearch[] searches)
        {
            _searches = searches.ToList();
        }

        public bool FailList { get; set; }

        public int SaveCallCount { get; private set; }

        public int RemoveCallCount { get; private set; }

        public int ClearCallCount { get; private set; }

        public Task<IReadOnlyList<SavedCatalogSearch>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailList)
            {
                throw new InvalidDataException("Test saved-search data is malformed.");
            }

            return Task.FromResult<IReadOnlyList<SavedCatalogSearch>>(_searches
                .OrderByDescending(search => search.UpdatedAtUtc)
                .ThenBy(search => search.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }

        public Task<SavedCatalogSearch> SaveAsync(SavedCatalogSearch search, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCallCount++;
            _searches.RemoveAll(candidate => string.Equals(candidate.Id, search.Id, StringComparison.Ordinal));
            _searches.Add(search);
            return Task.FromResult(search);
        }

        public Task<bool> RemoveAsync(string searchId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RemoveCallCount++;
            return Task.FromResult(_searches.RemoveAll(search => string.Equals(search.Id, searchId, StringComparison.Ordinal)) > 0);
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ClearCallCount++;
            FailList = false;
            _searches.Clear();
            return Task.CompletedTask;
        }
    }
}
