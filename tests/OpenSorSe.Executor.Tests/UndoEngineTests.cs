using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Executor.Models;
using Microsoft.Extensions.DependencyInjection;
using OpenSorSe.Core.DependencyInjection;

namespace OpenSorSe.Executor.Tests;

/// <summary>Verifies conservative explicit-record undo execution.</summary>
public sealed class UndoEngineTests
{
    /// <summary>Verifies empty input and pre-cancellation behavior.</summary>
    [Fact]
    public async Task UndoAsync_EmptyAndPreCancelled_AreDeterministic()
    {
        var empty = await Create().UndoAsync(Array.Empty<UndoRecord>());
        using var source = new CancellationTokenSource(); source.Cancel();
        var cancelled = await Create().UndoAsync(Array.Empty<UndoRecord>(), cancellationToken: source.Token);
        Assert.False(empty.WasCancelled); Assert.Empty(empty.Outcomes); Assert.Equal(new UndoExecutionStatistics(0, 0, 0, 0, 0, 0, 0, 0), empty.Statistics); Assert.True(cancelled.WasCancelled);
    }

    /// <summary>Verifies invalid collections are rejected before filesystem work.</summary>
    [Fact]
    public async Task UndoAsync_InvalidCollection_IsRejected()
    {
        IReadOnlyCollection<UndoRecord> nullRecords = null!;
        await Assert.ThrowsAsync<ArgumentNullException>(() => Create().UndoAsync(nullRecords));
        await Assert.ThrowsAsync<ArgumentException>(() => Create().UndoAsync(new List<UndoRecord> { null! }));
        await Assert.ThrowsAsync<ArgumentException>(() => Create().UndoAsync([Record("", UndoOperationKind.Move, "C:\\a", "C:\\b")]));
        await Assert.ThrowsAsync<ArgumentException>(() => Create().UndoAsync([Record("one", (UndoOperationKind)999, "C:\\a", "C:\\b")]));
        await Assert.ThrowsAsync<ArgumentException>(() => Create().UndoAsync([Record("one", UndoOperationKind.Move, "C:\\a", "C:\\b"), Record("one", UndoOperationKind.Copy, "C:\\c", "C:\\d")]));
    }

    /// <summary>Verifies all scalar validation boundaries reject before a later valid record can mutate files.</summary>
    [Theory]
    [MemberData(nameof(InvalidRecords))]
    public async Task UndoAsync_InvalidRecord_RejectsEntireRequest(UndoRecord invalid)
    {
        using var directory = new Temp(); var original = directory.File("original.txt", "original"); var result = Path.Combine(directory.Path, "result.txt"); File.Move(original, result);
        var valid = Record("valid", UndoOperationKind.Move, original, result);
        await Assert.ThrowsAsync<ArgumentException>(() => Create().UndoAsync([invalid, valid]));
        Assert.True(File.Exists(result)); Assert.False(File.Exists(original));
    }

    /// <summary>Verifies Move, Rename, and Copy undo behavior preserves the original copy source.</summary>
    [Fact]
    public async Task UndoAsync_SupportedKinds_ReverseOnlyApprovedPaths()
    {
        using var directory = new Temp(); var original = directory.File("original.txt", "original"); var moved = Path.Combine(directory.Path, "moved.txt"); File.Move(original, moved);
        var renameOriginal = Path.Combine(directory.Path, "rename.txt"); var renamed = directory.File("renamed.txt", "rename"); var copyOriginal = directory.File("copy-original.txt", "source"); var copied = directory.File("copied.txt", "copy");
        var result = await Create().UndoAsync([Record("move", UndoOperationKind.Move, original, moved), Record("rename", UndoOperationKind.Rename, renameOriginal, renamed), Record("copy", UndoOperationKind.Copy, copyOriginal, copied)]);
        Assert.All(result.Outcomes, outcome => Assert.Equal(UndoExecutionStatus.Succeeded, outcome.Status)); Assert.True(File.Exists(original)); Assert.True(File.Exists(renameOriginal)); Assert.False(File.Exists(copied)); Assert.True(File.Exists(copyOriginal));
    }

    /// <summary>Verifies occupied originals, unavailable results, and directories are safely rejected.</summary>
    [Fact]
    public async Task UndoAsync_UnsafeRecords_ReturnStructuredFailures()
    {
        using var directory = new Temp(); var occupied = directory.File("occupied.txt", "occupied"); var result = directory.File("result.txt", "result");
        var outcomes = await Create().UndoAsync([Record("occupied", UndoOperationKind.Move, occupied, result), Record("missing", UndoOperationKind.Copy, Path.Combine(directory.Path, "source.txt"), Path.Combine(directory.Path, "missing.txt")), Record("directory", UndoOperationKind.Copy, Path.Combine(directory.Path, "source2.txt"), directory.Path)]);
        Assert.Equal([UndoExecutionIssueKind.OriginalPathOccupied, UndoExecutionIssueKind.ResultUnavailable, UndoExecutionIssueKind.ResultTypeUnsupported], outcomes.Outcomes.Select(outcome => outcome.Issue!.Kind)); Assert.True(File.Exists(occupied));
    }

    /// <summary>Verifies missing parents, cross-directory rename, and sibling paths are never modified.</summary>
    [Fact]
    public async Task UndoAsync_PathSafety_DoesNotCreateOrDeleteUnrelatedPaths()
    {
        using var directory = new Temp(); var result = directory.File("result.txt", "result"); var sibling = directory.File("result-copy.txt", "sibling"); var missing = Path.Combine(directory.Path, "missing", "original.txt");
        var other = Directory.CreateDirectory(Path.Combine(directory.Path, "other")).FullName;
        var response = await Create().UndoAsync([Record("parent", UndoOperationKind.Move, missing, result), Record("rename", UndoOperationKind.Rename, Path.Combine(other, "original.txt"), result), Record("copy", UndoOperationKind.Copy, Path.Combine(directory.Path, "original.txt"), Path.Combine(directory.Path, "missing-copy.txt"))]);
        Assert.Equal(UndoExecutionIssueKind.OriginalParentUnavailable, response.Outcomes[0].Issue!.Kind);
        Assert.Equal(UndoExecutionIssueKind.PathMismatch, response.Outcomes[1].Issue!.Kind);
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "missing"))); Assert.True(File.Exists(sibling)); Assert.True(File.Exists(result));
    }

    /// <summary>Verifies symbolic-link results are rejected when the environment permits link creation.</summary>
    [Fact]
    public async Task UndoAsync_ReparseResult_IsRejectedWhenSupported()
    {
        using var directory = new Temp(); var target = directory.File("target.txt", "target"); var link = Path.Combine(directory.Path, "link.txt");
        try { File.CreateSymbolicLink(link, target); } catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }
        var response = await Create().UndoAsync([Record("link", UndoOperationKind.Copy, Path.Combine(directory.Path, "original.txt"), link)]);
        Assert.Equal(UndoExecutionIssueKind.ResultTypeUnsupported, Assert.Single(response.Outcomes).Issue!.Kind); Assert.True(File.Exists(target)); Assert.True(File.Exists(link));
    }

    /// <summary>Verifies pre-cancellation bypasses validation, mutation, and error reporting.</summary>
    [Fact]
    public async Task UndoAsync_PreCancelled_ReturnsEmptyCancelledResult()
    {
        using var source = new CancellationTokenSource(); source.Cancel(); var progress = new UndoProgress(); var errors = new Errors();
        var result = await new UndoEngine(new Log(), errors).UndoAsync([Record("", UndoOperationKind.Move, "relative", "relative")], progress, source.Token);
        Assert.True(result.WasCancelled); Assert.Empty(result.Outcomes); Assert.Equal(0L, result.Statistics.RecordsAttempted); Assert.Equal(UndoExecutionProgressStage.Cancelled, progress.Items[^1].Stage); Assert.False(errors.Reported);
    }

    /// <summary>Verifies cancellation between records preserves the completed undo only.</summary>
    [Fact]
    public async Task UndoAsync_CancellationBetweenRecords_PreservesCompletedOutcome()
    {
        using var directory = new Temp(); var firstOriginal = directory.File("first.txt", "one"); var firstResult = Path.Combine(directory.Path, "first-result.txt"); File.Move(firstOriginal, firstResult); var secondOriginal = directory.File("second.txt", "two"); var secondResult = Path.Combine(directory.Path, "second-result.txt"); File.Move(secondOriginal, secondResult);
        using var cancellation = new CancellationTokenSource(); var progress = new CancellingUndoProgress(cancellation);
        var result = await Create().UndoAsync([Record("first", UndoOperationKind.Move, firstOriginal, firstResult), Record("second", UndoOperationKind.Move, secondOriginal, secondResult)], progress, cancellation.Token);
        Assert.True(result.WasCancelled); Assert.Single(result.Outcomes); Assert.True(File.Exists(firstOriginal)); Assert.True(File.Exists(secondResult)); Assert.Equal(1L, result.Statistics.RecordsAttempted); Assert.Equal(UndoExecutionProgressStage.Cancelled, progress.LastStage);
    }

    /// <summary>Verifies normal progress lifecycle and counters for empty, successful, and failed undo execution.</summary>
    [Fact]
    public async Task UndoAsync_ReportsProgressLifecycle()
    {
        var emptyProgress = new UndoProgress(); await Create().UndoAsync(Array.Empty<UndoRecord>(), emptyProgress); Assert.Equal([UndoExecutionProgressStage.Starting, UndoExecutionProgressStage.Completed], emptyProgress.Items.Select(item => item.Stage));
        using var directory = new Temp(); var original = directory.File("original.txt", "data"); var resultPath = Path.Combine(directory.Path, "result.txt"); File.Move(original, resultPath); var progress = new UndoProgress(); var result = await Create().UndoAsync([Record("move", UndoOperationKind.Move, original, resultPath)], progress);
        Assert.Contains(progress.Items, item => item.Stage == UndoExecutionProgressStage.Executing && item.CurrentUndoRecordId == "move" && item.CurrentUndoKind == UndoOperationKind.Move); Assert.Equal(UndoExecutionProgressStage.Completed, progress.Items[^1].Stage); Assert.Equal(result.Statistics.RecordsSucceeded, progress.Items[^1].RecordsSucceeded);
    }

    /// <summary>Verifies caller order is preserved and recoverable failures do not stop later records.</summary>
    [Fact]
    public async Task UndoAsync_PreservesOrderAndContinuesAfterFailure()
    {
        using var directory = new Temp(); var original = directory.File("original.txt", "one"); var result = Path.Combine(directory.Path, "result.txt"); File.Move(original, result); var missing = Path.Combine(directory.Path, "missing.txt");
        var records = new[] { Record("missing", UndoOperationKind.Copy, Path.Combine(directory.Path, "source.txt"), missing), Record("move", UndoOperationKind.Move, original, result) };
        var response = await Create().UndoAsync(records);
        Assert.Equal(["missing", "move"], response.Outcomes.Select(outcome => outcome.UndoRecordId)); Assert.Equal(UndoExecutionStatus.Failed, response.Outcomes[0].Status); Assert.Equal(UndoExecutionStatus.Succeeded, response.Outcomes[1].Status);
    }

    /// <summary>Verifies every undo statistic and successful kind counter for a mixed execution.</summary>
    [Fact]
    public async Task UndoAsync_MixedRecords_ProducesAccurateStatistics()
    {
        using var directory = new Temp(); var moveOriginal = directory.File("move.txt", "m"); var moveResult = Path.Combine(directory.Path, "moved.txt"); File.Move(moveOriginal, moveResult); var renameOriginal = Path.Combine(directory.Path, "rename.txt"); var renameResult = directory.File("renamed.txt", "r"); var copyOriginal = directory.File("copy-original.txt", "o"); var copyResult = directory.File("copy-result.txt", "c");
        var response = await Create().UndoAsync([Record("move", UndoOperationKind.Move, moveOriginal, moveResult), Record("rename", UndoOperationKind.Rename, renameOriginal, renameResult), Record("copy", UndoOperationKind.Copy, copyOriginal, copyResult), Record("fail", UndoOperationKind.Copy, copyOriginal, Path.Combine(directory.Path, "missing.txt"))]);
        Assert.Equal(new UndoExecutionStatistics(4, 4, 3, 1, 1, 1, 1, 1), response.Statistics);
        Assert.Equal(1, response.Outcomes.Count(outcome => outcome.Issue is not null));
    }

    /// <summary>Verifies input records remain immutable while outcomes are created.</summary>
    [Fact]
    public async Task UndoAsync_DoesNotMutateInputRecords()
    {
        using var directory = new Temp(); var original = directory.File("original.txt", "data"); var result = Path.Combine(directory.Path, "result.txt"); File.Move(original, result); var record = Record("id", UndoOperationKind.Move, original, result); var before = record; var records = new List<UndoRecord> { record };
        await Create().UndoAsync(records);
        Assert.Equal(before, record); Assert.Equal([record], records); Assert.Equal("id", record.UndoRecordId); Assert.Equal(result, record.ResultPath);
    }

    /// <summary>Verifies semantically identical records with distinct IDs remain independently ordered attempts.</summary>
    [Fact]
    public async Task UndoAsync_DistinctDuplicateOperations_RemainSeparateAttempts()
    {
        using var directory = new Temp(); var original = directory.File("original.txt", "data"); var result = Path.Combine(directory.Path, "result.txt"); File.Move(original, result);
        var records = new[] { Record("first", UndoOperationKind.Move, original, result), Record("second", UndoOperationKind.Move, original, result) };
        var response = await Create().UndoAsync(records);
        Assert.Equal(["first", "second"], response.Outcomes.Select(outcome => outcome.UndoRecordId)); Assert.Equal(UndoExecutionStatus.Succeeded, response.Outcomes[0].Status); Assert.Equal(UndoExecutionStatus.Failed, response.Outcomes[1].Status); Assert.Equal(2L, response.Statistics.RecordsAttempted);
    }

    /// <summary>Verifies cancellation statistics retain the received collection size while counting only attempted records.</summary>
    [Fact]
    public async Task UndoAsync_CancellationBetweenRecords_ReportsReceivedAndAttemptedCounts()
    {
        using var directory = new Temp(); var firstOriginal = directory.File("first.txt", "one"); var firstResult = Path.Combine(directory.Path, "first-result.txt"); File.Move(firstOriginal, firstResult); var secondOriginal = directory.File("second.txt", "two"); var secondResult = Path.Combine(directory.Path, "second-result.txt"); File.Move(secondOriginal, secondResult);
        using var cancellation = new CancellationTokenSource();
        var response = await Create().UndoAsync([Record("first", UndoOperationKind.Move, firstOriginal, firstResult), Record("second", UndoOperationKind.Move, secondOriginal, secondResult)], new CancellingUndoProgress(cancellation), cancellation.Token);
        Assert.Equal(2L, response.Statistics.RecordsReceived); Assert.Equal(1L, response.Statistics.RecordsAttempted); Assert.Equal(1L, response.Statistics.RecordsSucceeded); Assert.Equal(0L, response.Statistics.RecordsFailed); Assert.True(response.WasCancelled); Assert.Single(response.Outcomes); Assert.True(File.Exists(secondResult));
    }

    /// <summary>Verifies the Undo Engine resolves with only Core diagnostics dependencies.</summary>
    [Fact]
    public void DependencyInjection_ResolvesUndoEngine()
    {
        var services = new ServiceCollection(); services.AddOpenSorSeCore(new OpenSorSeCoreOptions { ConfigurationFilePath = Path.Combine(Path.GetTempPath(), "opensorse-test-settings.json") }); services.AddSingleton<IUndoEngine, UndoEngine>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var engine = provider.GetRequiredService<IUndoEngine>();
        Assert.IsType<UndoEngine>(engine); Assert.NotNull(provider.GetRequiredService<ILoggingService>()); Assert.NotNull(provider.GetRequiredService<IErrorHandler>());
    }

    /// <summary>Verifies the Undo Engine has no constructor dependency on history, persistence, event, AI, or UI services.</summary>
    [Fact]
    public void UndoEngine_UsesOnlyDocumentedDiagnosticDependencies()
    {
        var constructor = Assert.Single(typeof(UndoEngine).GetConstructors());
        Assert.Equal([typeof(ILoggingService), typeof(IErrorHandler)], constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    /// <summary>Supplies every collection-level validation defect.</summary>
    public static IEnumerable<object[]> InvalidRecords()
    {
        yield return [Record("", UndoOperationKind.Move, "C:\\a", "C:\\b")];
        yield return [new UndoRecord("one", "", UndoOperationKind.Move, "C:\\a", "C:\\b", DateTimeOffset.UtcNow)];
        yield return [Record("one", (UndoOperationKind)999, "C:\\a", "C:\\b")];
        yield return [Record("one", UndoOperationKind.Move, "relative", "C:\\b")];
        yield return [Record("one", UndoOperationKind.Move, "C:\\a", "relative")];
        yield return [Record("one", UndoOperationKind.Move, "C:\\a", "C:\\folder\\..\\a")];
        yield return [new UndoRecord("one", "operation", UndoOperationKind.Move, "C:\\a", "C:\\b", DateTimeOffset.Now)];
    }

    private static UndoEngine Create() => new(new Log(), new Errors());
    private static UndoRecord Record(string id, UndoOperationKind kind, string original, string result) => new(id, "operation", kind, original, result, DateTimeOffset.UtcNow);
    private sealed class Temp : IDisposable { public Temp() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OpenSorSe.Undo.{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); } public string Path { get; } public string File(string name, string text) { var path = System.IO.Path.Combine(Path, name); System.IO.File.WriteAllText(path, text); return path; } public void Dispose() => Directory.Delete(Path, true); }
    private sealed class Log : ILoggingService { public ILogger CreateLogger(string categoryName) => NullLogger.Instance; public void Dispose() { } public void Initialize(LogLevel minimumLevel) { } }
    private sealed class Errors : IErrorHandler { public bool Reported { get; private set; } public event EventHandler<ApplicationError>? ErrorReported; public void Report(ApplicationError applicationError) { Reported = true; ErrorReported?.Invoke(this, applicationError); } }
    private sealed class UndoProgress : IProgress<UndoExecutionProgress> { public List<UndoExecutionProgress> Items { get; } = []; public void Report(UndoExecutionProgress value) => Items.Add(value); }
    private sealed class CancellingUndoProgress(CancellationTokenSource source) : IProgress<UndoExecutionProgress> { public UndoExecutionProgressStage LastStage { get; private set; } public void Report(UndoExecutionProgress value) { LastStage = value.Stage; if (value.Stage == UndoExecutionProgressStage.Completed && value.RecordsSucceeded == 1) source.Cancel(); } }
}
