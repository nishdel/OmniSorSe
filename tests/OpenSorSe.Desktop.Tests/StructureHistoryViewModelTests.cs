using OpenSorSe.Application.Structure;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies the advanced structure-history workflow and accessible diagram state.</summary>
public sealed class StructureHistoryViewModelTests
{
    /// <summary>Verifies history loads, filters by root and status, and selects a safe default.</summary>
    [Fact]
    public async Task RefreshAndFilters_ProjectBoundedHistory()
    {
        var store = new MemoryHistoryStore();
        store.Records.Add(Record("one", "C:\\One", RestructuringStatus.Previewed));
        store.Records.Add(Record("two", "C:\\Two", RestructuringStatus.Applied));
        using var viewModel = CreateViewModel(store);

        await viewModel.RefreshAsync();

        Assert.Equal(2, viewModel.History.Count);
        Assert.NotNull(viewModel.SelectedHistory);

        viewModel.RootFilter = "Two";
        Assert.Equal("two", Assert.Single(viewModel.History).OperationId);

        viewModel.RootFilter = null;
        viewModel.SelectedStatusFilter = viewModel.StatusFilters.Single(option =>
            option.Status == RestructuringStatus.Previewed);
        Assert.Equal("one", Assert.Single(viewModel.History).OperationId);
    }

    /// <summary>Verifies source/proposed comparison exposes textual change labels without color dependency.</summary>
    [Fact]
    public async Task ProposedSnapshot_ProjectsAccessibleComparisonRows()
    {
        var source = Snapshot("C:\\Root", Node("invoice.pdf", "invoice"));
        var proposed = Snapshot(
            "C:\\Root",
            Node("Documents", "dir", isDirectory: true),
            Node(Path.Combine("Documents", "invoice.pdf"), "invoice"));
        var store = new MemoryHistoryStore();
        store.Records.Add(Record(
            "preview",
            "C:\\Root",
            RestructuringStatus.Previewed,
            source,
            proposed));
        using var viewModel = CreateViewModel(store);
        await viewModel.RefreshAsync();

        viewModel.SelectedSnapshot = viewModel.SnapshotOptions.Single(option =>
            option.Kind == StructureSnapshotKind.Proposed);

        Assert.Contains(viewModel.DiagramRows, row => row.ChangeText == "Moved");
        Assert.Contains(viewModel.DiagramRows, row => row.ChangeText == "Added");
        Assert.Contains("Moved: 1", viewModel.ComparisonSummary, StringComparison.Ordinal);
    }

    /// <summary>Verifies a large diagram is bounded and searchable.</summary>
    [Fact]
    public async Task Diagram_LargeSnapshot_IsBoundedAndSearchable()
    {
        var nodes = Enumerable.Range(0, 800)
            .Select(index => Node($"file-{index:D4}.txt", $"id-{index}"))
            .ToArray();
        var snapshot = Snapshot("C:\\Root", nodes);
        var store = new MemoryHistoryStore();
        store.Records.Add(Record(
            "large",
            "C:\\Root",
            RestructuringStatus.Previewed,
            snapshot,
            snapshot));
        using var viewModel = CreateViewModel(store);
        await viewModel.RefreshAsync();

        Assert.Equal(StructureLimits.MaximumVisibleNodes, viewModel.DiagramRows.Count);
        Assert.Contains("bounded", viewModel.ComparisonSummary, StringComparison.OrdinalIgnoreCase);

        viewModel.DiagramSearch = "file-0799";
        Assert.Equal("file-0799.txt", Assert.Single(viewModel.DiagramRows).RelativePath);
    }

    /// <summary>Verifies preview is non-mutating and apply requires the separate confirmation commands.</summary>
    [Fact]
    public async Task PreviewAndApply_RequireSeparateExplicitConfirmation()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("invoice.pdf", "invoice");
        var store = new MemoryHistoryStore();
        var snapshots = new FolderStructureSnapshotService();
        using var viewModel = new StructureHistoryViewModel(
            store,
            new FolderRestructuringService(
                snapshots,
                store,
                new Configuration()),
            snapshots,
            new StructureComparisonService())
        {
            RootPath = temporary.Path,
        };

        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.True(File.Exists(temporary.PathFor("invoice.pdf")));
        Assert.True(viewModel.RequestApplyCommand.CanExecute(null));
        Assert.False(viewModel.ConfirmApplyCommand.CanExecute(null));

        viewModel.RequestApplyCommand.Execute(null);
        Assert.True(viewModel.IsApplyConfirmationPending);
        await viewModel.ConfirmApplyCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsApplyConfirmationPending);
        Assert.True(File.Exists(temporary.PathFor("Documents", "invoice.pdf")));
        Assert.Equal(RestructuringStatus.Applied, Assert.Single(store.Records).Status);
    }

    /// <summary>Verifies clearing application history never deletes or moves a source file.</summary>
    [Fact]
    public async Task ClearHistory_RequiresConfirmationAndDoesNotChangeFiles()
    {
        using var temporary = new TemporaryDirectory();
        temporary.Write("keep.txt", "keep");
        var store = new MemoryHistoryStore();
        store.Records.Add(Record("one", temporary.Path, RestructuringStatus.Previewed));
        using var viewModel = CreateViewModel(store);
        await viewModel.RefreshAsync();

        viewModel.RequestClearHistoryCommand.Execute(null);
        Assert.True(viewModel.IsClearConfirmationPending);
        await viewModel.ConfirmClearHistoryCommand.ExecuteAsync(null);

        Assert.Empty(store.Records);
        Assert.True(File.Exists(temporary.PathFor("keep.txt")));
        Assert.Contains("No user file", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies an absent service graph presents a controlled empty state.</summary>
    [Fact]
    public async Task UnavailableServices_ReturnControlledEmptyState()
    {
        using var viewModel = new StructureHistoryViewModel();

        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.History);
        Assert.Contains("unavailable", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private static StructureHistoryViewModel CreateViewModel(MemoryHistoryStore store) =>
        new(
            store,
            null,
            null,
            new StructureComparisonService());

    private static RestructuringHistoryRecord Record(
        string id,
        string root,
        RestructuringStatus status,
        FolderStructureSnapshot? source = null,
        FolderStructureSnapshot? proposed = null)
    {
        source ??= Snapshot(root, Node("source.txt", "source"));
        proposed ??= source;
        return new RestructuringHistoryRecord(
            id,
            source.RootIdentity,
            Path.GetFullPath(root),
            source.StructureFingerprint,
            DateTimeOffset.UnixEpoch,
            status == RestructuringStatus.Previewed ? null : DateTimeOffset.UnixEpoch,
            source,
            proposed,
            status == RestructuringStatus.Applied ? proposed : null,
            status == RestructuringStatus.Previewed
                ? RestructuringApprovalState.NotRequested
                : RestructuringApprovalState.Approved,
            status,
            [],
            [],
            "Test structure history.",
            null,
            "test/1",
            false);
    }

    private static FolderStructureSnapshot Snapshot(
        string root,
        params StructureNode[] nodes) =>
        new(
            Path.GetFullPath(root),
            $"root:{root}",
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UnixEpoch,
            nodes);

    private static StructureNode Node(
        string path,
        string identity,
        bool isDirectory = false) =>
        new(path, isDirectory, isDirectory ? 0 : 1, DateTimeOffset.UnixEpoch, identity);

    private sealed class MemoryHistoryStore : IStructureHistoryStore
    {
        public List<RestructuringHistoryRecord> Records { get; } = [];

        public Task<IReadOnlyList<RestructuringHistoryRecord>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RestructuringHistoryRecord>>(
                Records.OrderByDescending(record => record.StartedAtUtc).ToArray());

        public Task UpsertAsync(
            RestructuringHistoryRecord record,
            CancellationToken cancellationToken)
        {
            Records.RemoveAll(candidate => candidate.OperationId == record.OperationId);
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            Records.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class Configuration : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new()
        {
            Features = new FeatureSettings { ShowAdvancedFeatures = true },
        };

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"opensorse-structure-ui-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string PathFor(params string[] parts) =>
            parts.Aggregate(Path, System.IO.Path.Combine);

        public void Write(string relativePath, string content)
        {
            var path = PathFor(relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
