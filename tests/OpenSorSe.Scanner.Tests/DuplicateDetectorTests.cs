using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Scanner.Tests;

/// <summary>
/// Verifies exact SHA-256 duplicate detection without filesystem access.
/// </summary>
public sealed class DuplicateDetectorTests
{
    /// <summary>
    /// Verifies two equal hashes produce one ordered duplicate group.
    /// </summary>
    [Fact]
    public async Task DetectAsync_TwoEqualHashes_CreateOneGroup()
    {
        var hash = Hash('a');
        var result = await CreateDetector().DetectAsync([Entry("first", hash), Entry("second", hash)]);

        var group = Assert.Single(result.Groups);
        Assert.Equal($"sha256:{hash.Value}", group.GroupId);
        Assert.Equal("SHA-256", group.Algorithm);
        Assert.Equal(hash.Value, group.HashValue);
        Assert.Equal(["first", "second"], group.Files.Select(file => file.FullPath));
        Assert.All(result.Files, file => Assert.Equal(DuplicateStatus.Duplicate, file.Duplicate!.Status));
    }

    /// <summary>
    /// Verifies repeated equal hashes preserve all duplicate input entries in one group.
    /// </summary>
    [Fact]
    public async Task DetectAsync_ThreeEqualHashes_PreservesAllEntries()
    {
        var hash = Hash('a');
        var result = await CreateDetector().DetectAsync([Entry("one", hash), Entry("two", hash), Entry("three", hash)]);

        Assert.Equal(3L, result.Statistics.FilesDuplicate);
        Assert.Equal(["one", "two", "three"], Assert.Single(result.Groups).Files.Select(file => file.FullPath));
    }

    /// <summary>
    /// Verifies singleton and different valid hashes are classified as unique without groups.
    /// </summary>
    [Fact]
    public async Task DetectAsync_ValidSingletons_AreUnique()
    {
        var result = await CreateDetector().DetectAsync([Entry("one", Hash('a')), Entry("two", Hash('b'))]);

        Assert.Empty(result.Groups);
        Assert.All(result.Files, file =>
        {
            Assert.Equal(DuplicateStatus.Unique, file.Duplicate!.Status);
            Assert.Null(file.Duplicate.GroupId);
        });
    }

    /// <summary>
    /// Verifies duplicate, unique, and unknown entries can coexist with accurate statistics.
    /// </summary>
    [Fact]
    public async Task DetectAsync_MixedEntries_ReturnsAccurateStatistics()
    {
        var result = await CreateDetector().DetectAsync([Entry("a", Hash('a')), Entry("b", Hash('b')), Entry("c", Hash('a')), Entry("unknown")]);

        Assert.Equal(new DuplicateDetectionStatistics(4, 1, 2, 1, 1, 1), result.Statistics);
        Assert.Equal(DuplicateStatus.Unique, result.Files[1].Duplicate!.Status);
        Assert.Equal(DuplicateStatus.Unknown, result.Files[3].Duplicate!.Status);
        Assert.Equal(DuplicateDetectionIssueKind.HashUnavailable, Assert.Single(result.Issues).Kind);
    }

    /// <summary>
    /// Verifies invalid hash states map to exactly one deterministic issue per entry and do not stop later entries.
    /// </summary>
    [Theory]
    [InlineData(null, null, DuplicateDetectionIssueKind.HashUnavailable)]
    [InlineData(null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", DuplicateDetectionIssueKind.UnsupportedHashAlgorithm)]
    [InlineData("MD5", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", DuplicateDetectionIssueKind.UnsupportedHashAlgorithm)]
    [InlineData("SHA-256", "not-a-hash", DuplicateDetectionIssueKind.InvalidHashValue)]
    [InlineData("SHA-256", "", DuplicateDetectionIssueKind.InvalidHashValue)]
    public async Task DetectAsync_InvalidHashes_AreUnknownAndRecoverable(string? algorithm, string? value, DuplicateDetectionIssueKind expectedKind)
    {
        FileHash? hash = value is null ? null : new FileHash(algorithm!, value);
        var result = await CreateDetector().DetectAsync([Entry("invalid", hash), Entry("valid", Hash('a'))]);

        Assert.Equal(DuplicateStatus.Unknown, result.Files[0].Duplicate!.Status);
        Assert.Equal(expectedKind, Assert.Single(result.Issues).Kind);
        Assert.Equal(DuplicateStatus.Unique, result.Files[1].Duplicate!.Status);
    }

    /// <summary>
    /// Verifies hash algorithms and values are normalized case-insensitively for matching.
    /// </summary>
    [Fact]
    public async Task DetectAsync_NormalizesUppercaseHashAndAlgorithm()
    {
        var lower = new string('a', 64);
        var result = await CreateDetector().DetectAsync([Entry("upper", new FileHash("sha-256", lower.ToUpperInvariant())), Entry("lower", new FileHash("SHA-256", lower))]);

        Assert.Single(result.Groups);
        var firstDuplicate = Assert.IsType<DuplicateClassification>(result.Files[0].Duplicate);
        var secondDuplicate = Assert.IsType<DuplicateClassification>(result.Files[1].Duplicate);
        Assert.Equal($"sha256:{lower}", firstDuplicate.GroupId);
        Assert.Equal(firstDuplicate.GroupId, secondDuplicate.GroupId);
    }

    /// <summary>
    /// Verifies group ordering follows first appearance and group membership follows entry order.
    /// </summary>
    [Fact]
    public async Task DetectAsync_GroupsFollowFirstInputAppearance()
    {
        var result = await CreateDetector().DetectAsync([Entry("b1", Hash('b')), Entry("a1", Hash('a')), Entry("b2", Hash('b')), Entry("a2", Hash('a'))]);

        Assert.Equal([new string('b', 64), new string('a', 64)], result.Groups.Select(group => group.HashValue));
        Assert.Equal(["b1", "b2"], result.Groups[0].Files.Select(file => file.FullPath));
        Assert.Equal(["b1", "a1", "b2", "a2"], result.Files.Select(file => file.FullPath));
    }

    /// <summary>
    /// Verifies duplicate input entries and prior duplicate classifications are preserved and replaced immutably.
    /// </summary>
    [Fact]
    public async Task DetectAsync_PreservesEarlierPropertiesAndReplacesDuplicateClassification()
    {
        var metadata = new FileMetadata("file.txt", ".txt", 1, null, null, null, FileAttributes.Normal);
        var classification = new FileClassification(FileCategory.Document);
        var input = new FileEntry("not/a/filesystem/path", metadata, Hash('a'), classification, new DuplicateClassification(DuplicateStatus.Unique));
        var result = await CreateDetector().DetectAsync([input, input]);

        Assert.Equal([input.FullPath, input.FullPath], result.Files.Select(file => file.FullPath));
        Assert.All(result.Files, file =>
        {
            Assert.Same(metadata, file.Metadata);
            Assert.Same(input.Hash, file.Hash);
            Assert.Same(classification, file.Classification);
            Assert.Equal(DuplicateStatus.Duplicate, file.Duplicate!.Status);
        });
        Assert.Equal(DuplicateStatus.Unique, input.Duplicate!.Status);
    }

    /// <summary>
    /// Verifies empty input returns an empty, zero-statistics result.
    /// </summary>
    [Fact]
    public async Task DetectAsync_EmptyInput_ReturnsZeroStatistics()
    {
        var result = await CreateDetector().DetectAsync(Array.Empty<FileEntry>());

        Assert.Empty(result.Files);
        Assert.Empty(result.Groups);
        Assert.Empty(result.Issues);
        Assert.Equal(new DuplicateDetectionStatistics(0, 0, 0, 0, 0, 0), result.Statistics);
    }

    /// <summary>
    /// Verifies null collections and null entries are rejected before processing.
    /// </summary>
    [Fact]
    public async Task DetectAsync_InvalidInput_IsRejectedBeforeProcessing()
    {
        IReadOnlyCollection<FileEntry> nullCollection = null!;
        await Assert.ThrowsAsync<ArgumentNullException>(() => CreateDetector().DetectAsync(nullCollection));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateDetector().DetectAsync(new List<FileEntry> { null! }));
    }

    /// <summary>
    /// Verifies pre-cancellation throws deterministically without producing a result.
    /// </summary>
    [Fact]
    public async Task DetectAsync_PreCancelled_ThrowsOperationCanceledException()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateDetector().DetectAsync([Entry("file", Hash('a'))], source.Token));
    }

    /// <summary>
    /// Verifies supplied hashes are used without inspecting the filesystem or computing a replacement hash.
    /// </summary>
    [Fact]
    public async Task DetectAsync_UsesOnlySuppliedHashData()
    {
        var suppliedHash = Hash('c');
        var result = await CreateDetector().DetectAsync([Entry("<>invalid-path-one", suppliedHash), Entry("<>invalid-path-two", suppliedHash)]);

        Assert.Empty(result.Issues);
        Assert.All(result.Files, file => Assert.Same(suppliedHash, file.Hash));
        Assert.Single(result.Groups);
    }

    /// <summary>Verifies a large unique collection remains ordered and does not allocate duplicate groups unnecessarily.</summary>
    [Fact]
    public async Task DetectAsync_TwentyFiveThousandUniqueHashes_RemainsAccurateAndOrdered()
    {
        var files = Enumerable.Range(0, 25_000)
            .Select(index => Entry(
                $"entry-{index:D5}",
                new FileHash("SHA-256", index.ToString("x64"))))
            .ToArray();

        var result = await CreateDetector().DetectAsync(files);

        Assert.Equal(25_000, result.Files.Count);
        Assert.Empty(result.Groups);
        Assert.Empty(result.Issues);
        Assert.Equal(
            new DuplicateDetectionStatistics(25_000, 25_000, 0, 0, 0, 0),
            result.Statistics);
        Assert.Equal(files.Select(file => file.FullPath), result.Files.Select(file => file.FullPath));
        Assert.All(result.Files, file => Assert.Equal(DuplicateStatus.Unique, file.Duplicate!.Status));
    }

    /// <summary>Verifies cancellation is checked during preparation rather than only before a large scan.</summary>
    [Fact]
    public async Task DetectAsync_CancelledDuringPreparation_StopsWithoutReturningPartialResult()
    {
        using var cancellation = new CancellationTokenSource();
        var files = Enumerable.Range(0, 10_000)
            .Select(index => Entry(
                $"entry-{index:D5}",
                new FileHash("SHA-256", index.ToString("x64"))))
            .ToArray();
        IReadOnlyCollection<FileEntry> cancellingCollection =
            new CancellingCollection<FileEntry>(files, 256, cancellation);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateDetector().DetectAsync(cancellingCollection, cancellation.Token));
    }

    private static DuplicateDetector CreateDetector() => new(new TestLoggingService(), new TestErrorHandler());

    private static FileEntry Entry(string path, FileHash? hash = null) => new(path, Hash: hash);

    private static FileHash Hash(char character) => new("SHA-256", new string(character, 64));

    private sealed class TestLoggingService : ILoggingService
    {
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public void Dispose()
        {
        }

        public void Initialize(LogLevel minimumLevel)
        {
        }
    }

    private sealed class TestErrorHandler : IErrorHandler
    {
        public event EventHandler<ApplicationError>? ErrorReported;

        public void Report(ApplicationError applicationError) => ErrorReported?.Invoke(this, applicationError);
    }

    private sealed class CancellingCollection<T>(
        IReadOnlyList<T> inner,
        int cancelAtIndex,
        CancellationTokenSource cancellation) : IReadOnlyCollection<T>
    {
        private int _enumerationCount;

        public int Count => inner.Count;

        public IEnumerator<T> GetEnumerator()
        {
            var enumeration = Interlocked.Increment(ref _enumerationCount);
            for (var index = 0; index < inner.Count; index++)
            {
                if (enumeration >= 2 && index == cancelAtIndex)
                {
                    cancellation.Cancel();
                }

                yield return inner[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
