using OpenSorSe.Application.Catalog;
using OpenSorSe.Application.CatalogComparison;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Tags;
using OpenSorSe.Core.Platform;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies deterministic bounded historical comparison without live filesystem access.</summary>
public sealed class CatalogComparisonServiceTests
{
    private readonly CatalogComparisonService _service = new();

    /// <summary>Verifies all change kinds, fixed ordering, and aggregate counts.</summary>
    [Fact]
    public void Compare_MixedChanges_ClassifiesAndOrdersDeterministically()
    {
        var baseline = CreateEntry("baseline", [
            CreateFile("file:same-old", "/scope/same.txt"),
            CreateFile("file:modified-old", "/scope/modified.txt", size: 1),
            CreateFile("file:removed", "/scope/removed.txt"),
        ]);
        var current = CreateEntry("current", [
            CreateFile("file:added", "/scope/added.txt"),
            CreateFile("file:modified-new", "/scope/modified.txt", size: 2),
            CreateFile("file:same-new", "/scope/same.txt"),
        ]);

        var result = _service.Compare(baseline, current, CancellationToken.None);

        Assert.Equal(
            [CatalogComparisonChangeKind.Added, CatalogComparisonChangeKind.Removed, CatalogComparisonChangeKind.Modified, CatalogComparisonChangeKind.Unchanged],
            result.Changes.Select(change => change.Kind));
        Assert.Equal(["size"], result.Changes.Single(change => change.Kind == CatalogComparisonChangeKind.Modified).ChangedFields);
        Assert.Equal(new CatalogComparisonStatistics(3, 3, 1, 1, 1, 1, 0), result.Statistics);
        Assert.Equal(CatalogScopeMatch.Same, result.ScopeMatch);
        Assert.Throws<NotSupportedException>(() => ((IList<CatalogFileChange>)result.Changes).Add(result.Changes[0]));
    }

    /// <summary>Verifies every supported stored field and accepted normalized tags participate in modification detection.</summary>
    [Fact]
    public void Compare_AllMetadataAndTagFieldsChange_ReportsFixedFieldOrder()
    {
        var baselineFile = new ResultFile(
            "file:old", "/scope/file.txt", "file.txt", ".txt", 1, DateTimeOffset.UnixEpoch,
            FileCategory.Document, "Document", DuplicateStatus.Unique, null, false);
        var currentFile = new ResultFile(
            "file:new", "/scope/file.txt", "file.txt", ".pdf", 2, DateTimeOffset.UnixEpoch.AddDays(1),
            FileCategory.Archive, "Archive", DuplicateStatus.Duplicate, "group:new", true);
        var baseline = CreateEntry("baseline", [baselineFile], [CreateTag("tag:old", baselineFile.Id, "Finance", "finance")]);
        var current = CreateEntry("current", [currentFile], [
            CreateTag("tag:new-a", currentFile.Id, "Travel", "travel"),
            CreateTag("tag:new-b", currentFile.Id, "TRAVEL", "TRAVEL"),
        ]);

        var result = _service.Compare(baseline, current, CancellationToken.None);

        var change = Assert.Single(result.Changes);
        Assert.Equal(CatalogComparisonChangeKind.Modified, change.Kind);
        Assert.Equal(
            ["size", "last modified", "extension", "category", "classification", "duplicate status", "planned-operation preview", "tags"],
            change.ChangedFields);
        Assert.Equal(["finance"], change.BaselineTags);
        Assert.Equal(["travel"], change.CurrentTags);
    }

    /// <summary>Verifies Windows syntax is case/separator neutral while Unix path case remains significant.</summary>
    [Fact]
    public void Compare_CrossPlatformPathIdentity_UsesStoredPathSyntaxNotHostOperatingSystem()
    {
        var baseline = CreateEntry("baseline", [
            CreateFile("file:windows-old", "C:\\Scope\\File.txt"),
            CreateFile("file:unix-old", "/Data/file.txt"),
        ]);
        var current = CreateEntry("current", [
            CreateFile("file:windows-new", "c:/scope/file.txt"),
            CreateFile("file:unix-new", "/data/file.txt"),
        ]);

        var result = _service.Compare(baseline, current, CancellationToken.None);

        Assert.Single(result.Changes, change => change.Kind == CatalogComparisonChangeKind.Unchanged);
        Assert.Single(result.Changes, change => change.Kind == CatalogComparisonChangeKind.Added);
        Assert.Single(result.Changes, change => change.Kind == CatalogComparisonChangeKind.Removed);
    }

    /// <summary>Verifies scope equality ignores root order and legacy/mismatched scope remains explicit.</summary>
    [Fact]
    public void Compare_SourceScopes_ReportsSameDifferentAndUnknown()
    {
        var baseline = CreateEntry("baseline", []) with { SourceRoots = ["C:\\Two", "C:\\One"] };
        var same = CreateEntry("same", []) with { SourceRoots = ["c:/one/", "c:/two"] };
        var different = CreateEntry("different", []) with { SourceRoots = ["C:\\Other"] };
        var legacy = CreateEntry("legacy", []) with { SourceRoots = [] };

        Assert.Equal(CatalogScopeMatch.Same, _service.Compare(baseline, same, CancellationToken.None).ScopeMatch);
        Assert.Equal(CatalogScopeMatch.Different, _service.Compare(baseline, different, CancellationToken.None).ScopeMatch);
        Assert.Equal(CatalogScopeMatch.Unknown, _service.Compare(baseline, legacy, CancellationToken.None).ScopeMatch);
    }

    /// <summary>Verifies duplicate stored identities use the stable first record and report ignored ambiguity.</summary>
    [Fact]
    public void Compare_DuplicatePathIdentity_SelectsStableFirstAndReportsCount()
    {
        var baseline = CreateEntry("baseline", [
            CreateFile("file:b", "C:\\Scope\\same.txt", size: 99),
            CreateFile("file:a", "c:/scope/same.txt", size: 1),
        ]);
        var current = CreateEntry("current", [CreateFile("file:current", "C:\\SCOPE\\SAME.TXT", size: 1)]);

        var result = _service.Compare(baseline, current, CancellationToken.None);

        Assert.Equal(CatalogComparisonChangeKind.Unchanged, Assert.Single(result.Changes).Kind);
        Assert.Equal(1, result.Statistics.IgnoredDuplicateRecordCount);
    }

    /// <summary>Verifies maximum supported disjoint snapshots remain bounded to the documented union.</summary>
    [Fact]
    public void Compare_MaximumDisjointSnapshots_ProducesBoundedCompleteUnion()
    {
        var baseline = CreateEntry("baseline", Enumerable.Range(0, CatalogComparisonLimits.MaximumFilesPerSnapshot)
            .Select(index => CreateFile($"baseline:{index}", $"/baseline/{index:D4}.txt")).ToArray());
        var current = CreateEntry("current", Enumerable.Range(0, CatalogComparisonLimits.MaximumFilesPerSnapshot)
            .Select(index => CreateFile($"current:{index}", $"/current/{index:D4}.txt")).ToArray());

        var result = _service.Compare(baseline, current, CancellationToken.None);

        Assert.Equal(CatalogComparisonLimits.MaximumChangeCount, result.Changes.Count);
        Assert.Equal(CatalogComparisonLimits.MaximumFilesPerSnapshot, result.Statistics.AddedCount);
        Assert.Equal(CatalogComparisonLimits.MaximumFilesPerSnapshot, result.Statistics.RemovedCount);
    }

    /// <summary>Verifies oversize inputs and pre-cancellation fail before producing partial output.</summary>
    [Fact]
    public void Compare_OversizeOrCancelled_RejectsBeforeComparison()
    {
        var oversized = CreateEntry("oversized", Enumerable.Range(0, CatalogComparisonLimits.MaximumFilesPerSnapshot + 1)
            .Select(index => CreateFile($"file:{index}", $"/scope/{index}.txt")).ToArray());
        var empty = CreateEntry("empty", []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<CatalogCapacityExceededException>(() => _service.Compare(oversized, empty, CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => _service.Compare(empty, empty, cancellation.Token));
    }

    /// <summary>Verifies callers cannot bypass persisted per-file tag capacity at the pure comparison boundary.</summary>
    [Fact]
    public void Compare_ExcessiveTagsForOneFile_RejectsBeforeComparison()
    {
        var file = CreateFile("file:one", "/scope/one.txt");
        var tags = Enumerable.Range(0, UserTagLimits.MaximumAcceptedTagsPerFile + 1)
            .Select(index => CreateTag($"tag:{index}", file.Id, $"Tag {index}", $"tag-{index}"))
            .ToArray();
        var invalid = CreateEntry("invalid", [file], tags);

        Assert.Throws<InvalidDataException>(() => _service.Compare(invalid, CreateEntry("empty", []), CancellationToken.None));
    }

    private static CatalogEntry CreateEntry(
        string id,
        IReadOnlyList<ResultFile> files,
        IReadOnlyList<TagAssociation>? tags = null) => new(
            $"catalog:{id}",
            DateTimeOffset.UnixEpoch,
            new ResultsSnapshot(
                $"session:{id}",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                files,
                [],
                [],
                [],
                [],
                new ResultsSnapshotStatistics(files.Count, 0, 0, 0, 0, 0, 0),
                true),
            tags ?? [])
        {
            DisplayName = id,
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

    private static TagAssociation CreateTag(string id, string fileId, string display, string normalized) => new(
        id,
        fileId,
        display,
        normalized,
        "User",
        TagSource.UserApproved,
        TagAcceptanceState.Accepted,
        null,
        DateTimeOffset.UnixEpoch);
}
