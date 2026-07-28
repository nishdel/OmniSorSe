using OpenSorSe.Application.Models;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies pure, deterministic in-memory result-query behavior.
/// </summary>
public sealed class ResultsQueryEngineTests
{
    /// <summary>
    /// Verifies text search uses ordinal case-insensitive display values without filesystem access.
    /// </summary>
    [Fact]
    public void Evaluate_TextSearchMatchesNamePathExtensionAndClassification()
    {
        var snapshot = CreateSnapshot(
            CreateFile("file:0", "C:\\Reports\\Invoice.TXT", ".txt", "Document"),
            CreateFile("file:1", "C:\\Code\\build.cs", ".cs", "Code"));

        var byName = ResultsQueryEngine.Evaluate(snapshot, ResultsQuery.Default with { Text = "invoice" });
        var byPath = ResultsQueryEngine.Evaluate(snapshot, ResultsQuery.Default with { Text = "CODE" });
        var byExtension = ResultsQueryEngine.Evaluate(snapshot, ResultsQuery.Default with { Text = ".TXT" });
        var byClassification = ResultsQueryEngine.Evaluate(snapshot, ResultsQuery.Default with { Text = "document" });

        Assert.Equal("file:0", Assert.Single(byName.Page.Items).Id);
        Assert.Equal("file:1", Assert.Single(byPath.Page.Items).Id);
        Assert.Equal("file:0", Assert.Single(byExtension.Page.Items).Id);
        Assert.Equal("file:0", Assert.Single(byClassification.Page.Items).Id);
    }

    /// <summary>
    /// Verifies invalid input is reset to bounded safe defaults and missing values sort after known values ascending.
    /// </summary>
    [Fact]
    public void Evaluate_NormalizesInvalidQueryAndSortsDeterministically()
    {
        var snapshot = CreateSnapshot(
            CreateFile("file:2", "C:\\two.txt", ".txt", "Document", size: null),
            CreateFile("file:1", "C:\\one.txt", ".txt", "Document", size: 10),
            CreateFile("file:0", "C:\\zero.txt", ".txt", "Document", size: 10));
        var invalid = new ResultsQuery(null, (ResultDuplicateFilter)999, ".TXT", (FileCategory)999, (ResultPlannedOperationFilter)999, (ResultsSortField)999, (SortDirection)999, -3, 9999);

        var normalized = ResultsQueryEngine.Evaluate(snapshot, invalid);
        var sorted = ResultsQueryEngine.Evaluate(snapshot, ResultsQuery.Default with { SortField = ResultsSortField.Size });
        var descending = ResultsQueryEngine.Evaluate(snapshot, ResultsQuery.Default with { SortField = ResultsSortField.Size, SortDirection = SortDirection.Descending });

        Assert.Equal(200, normalized.Query.PageSize);
        Assert.Equal(0, normalized.Query.PageIndex);
        Assert.Equal(ResultDuplicateFilter.All, normalized.Query.DuplicateFilter);
        Assert.Equal(["file:1", "file:0", "file:2"], sorted.Page.Items.Select(file => file.Id));
        Assert.Equal(["file:2", "file:1", "file:0"], descending.Page.Items.Select(file => file.Id));
    }

    /// <summary>
    /// Verifies opaque group filtering and page bounds never expose an unbounded result page.
    /// </summary>
    [Fact]
    public void Evaluate_GroupFilterAndPaging_ReturnOnlyRequestedBoundedRows()
    {
        var files = Enumerable.Range(0, 600)
            .Select(index => CreateFile($"file:{index}", $"C:\\items\\{index:D4}.bin", ".bin", "Data", groupId: index < 2 ? "group:opaque" : null))
            .ToArray();
        var snapshot = CreateSnapshot(files);

        var page = ResultsQueryEngine.Evaluate(snapshot, ResultsQuery.Default with { PageSize = 50, PageIndex = 99 });
        var group = ResultsQueryEngine.Evaluate(snapshot, ResultsQuery.Default with { DuplicateGroupId = "group:opaque" });

        Assert.Equal(11, page.Page.PageIndex);
        Assert.Equal(50, page.Page.Items.Count);
        Assert.Equal(2, group.Page.TotalItemCount);
        Assert.All(group.Page.Items, file => Assert.Equal("group:opaque", file.DuplicateGroupId));
    }

    /// <summary>
    /// Verifies multi-token matching, accepted tag matching, relevance ranking, stable tie-breaking, and match explanations.
    /// </summary>
    [Fact]
    public void Evaluate_MetadataAwareRankedSearch_UsesAllTokensTagsAndStableOrdering()
    {
        var first = CreateFile("file:1", "C:\\Reports\\invoice-april.pdf", ".pdf", "Document");
        var second = CreateFile("file:2", "C:\\Archive\\invoice-april.pdf", ".pdf", "Document");
        var snapshot = CreateSnapshot(first, second);
        var tags = new Dictionary<string, IReadOnlyList<TagAssociation>>(StringComparer.Ordinal)
        {
            ["file:2"] = [new TagAssociation("tag:2:finance", "file:2", "Finance", "finance", "Suggested", TagSource.UserApproved, TagAcceptanceState.Accepted, null, DateTimeOffset.UnixEpoch)],
        };

        var result = ResultsQueryEngine.Evaluate(snapshot, ResultsQuery.Default with { Text = "invoice finance" }, tagsByFile: tags);

        Assert.Equal("file:2", Assert.Single(result.Page.Items).Id);
        Assert.Contains("tag match: Finance", result.Page.Matches["file:2"].Explanation, StringComparison.Ordinal);
        Assert.Equal(1, result.Page.TotalItemCount);
    }

    /// <summary>Verifies large in-memory result sets remain bounded, accurately ranked, and stable.</summary>
    [Fact]
    public void Evaluate_FiftyThousandFiles_ReturnsOnlyBoundedMatchingPage()
    {
        var files = Enumerable.Range(0, 50_000)
            .Select(index => CreateFile(
                $"file:{index:D5}",
                index % 10_000 == 0
                    ? $"C:\\Bulk\\needle-{index:D5}.txt"
                    : $"C:\\Bulk\\entry-{index:D5}.txt",
                ".txt",
                "Document",
                size: index))
            .ToArray();
        var snapshot = CreateSnapshot(files);

        var result = ResultsQueryEngine.Evaluate(
            snapshot,
            ResultsQuery.Default with { Text = "needle document", PageSize = 50 });

        Assert.Equal(5, result.Page.TotalItemCount);
        Assert.Equal(5, result.Page.Items.Count);
        Assert.Equal(
            ["file:00000", "file:10000", "file:20000", "file:30000", "file:40000"],
            result.Page.Items.Select(file => file.Id));
        Assert.All(result.Page.Items, file => Assert.Contains("needle", file.DisplayFileName));
    }

    /// <summary>Verifies cancellation is observed during a large local query and no partial page is returned.</summary>
    [Fact]
    public void Evaluate_CancelledDuringLargeQuery_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        var files = Enumerable.Range(0, 10_000)
            .Select(index => CreateFile(
                $"file:{index:D5}",
                $"C:\\Bulk\\entry-{index:D5}.txt",
                ".txt",
                "Document",
                size: index))
            .ToArray();
        IReadOnlyList<ResultFile> cancellingFiles =
            new CancellingReadOnlyList<ResultFile>(files, 256, cancellation);
        var snapshot = new ResultsSnapshot(
            "session:cancellation",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            cancellingFiles,
            Array.AsReadOnly(Array.Empty<ResultDirectory>()),
            Array.AsReadOnly(Array.Empty<ResultDuplicateGroup>()),
            Array.AsReadOnly(Array.Empty<ResultPlannedOperation>()),
            Array.AsReadOnly(Array.Empty<ResultIssue>()),
            new ResultsSnapshotStatistics(files.Length, 0, 0, 0, 0, 0, 0),
            true);

        Assert.Throws<OperationCanceledException>(() =>
            ResultsQueryEngine.Evaluate(
                snapshot,
                ResultsQuery.Default,
                cancellation.Token));
    }

    private static ResultsSnapshot CreateSnapshot(params ResultFile[] files) => new(
        "session:test",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        Array.AsReadOnly(files),
        Array.AsReadOnly(Array.Empty<ResultDirectory>()),
        Array.AsReadOnly(Array.Empty<ResultDuplicateGroup>()),
        Array.AsReadOnly(Array.Empty<ResultPlannedOperation>()),
        Array.AsReadOnly(Array.Empty<ResultIssue>()),
        new ResultsSnapshotStatistics(files.Length, 0, 0, 0, 0, 0, 0),
        true);

    private static ResultFile CreateFile(string id, string path, string extension, string classification, long? size = 1, string? groupId = null) =>
        new(
            id,
            path,
            Path.GetFileName(path),
            extension,
            size,
            null,
            classification == "Code" ? FileCategory.Code : classification == "Data" ? FileCategory.Data : FileCategory.Document,
            classification,
            groupId is null ? DuplicateStatus.Unique : DuplicateStatus.Duplicate,
            groupId,
            false);

    private sealed class CancellingReadOnlyList<T>(
        IReadOnlyList<T> inner,
        int cancelAtIndex,
        CancellationTokenSource cancellation) : IReadOnlyList<T>
    {
        public int Count => inner.Count;

        public T this[int index]
        {
            get
            {
                if (index == cancelAtIndex)
                {
                    cancellation.Cancel();
                }

                return inner[index];
            }
        }

        public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
