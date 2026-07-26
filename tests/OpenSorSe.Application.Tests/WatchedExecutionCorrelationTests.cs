#pragma warning disable CS1591

using OpenSorSe.Application.Watching;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Application.Tests;

public sealed class WatchedExecutionCorrelationTests
{
    [Fact]
    public async Task MatchingJournalIdentity_IsSuppressedOnceUsingFilesystemEvidence()
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var original = Path.Combine(root, "old.txt");
        var result = Path.Combine(root, "new.txt");
        await File.WriteAllTextAsync(result, "same");
        var identity = Identity(result);
        var journal = new InMemoryOperationJournalStore();
        await journal.UpsertAsync(
            Operation(original, result, identity),
            CancellationToken.None);
        var correlation = new OperationJournalWatchedExecutionCorrelation(
            journal,
            new WatchedFolderPathPolicy());
        var configuration = Configuration(root);
        var hint = new WatchedFolderHint(
            configuration.Id,
            WatchedPathChangeKind.FileRenamed,
            result,
            original,
            DateTimeOffset.UtcNow);

        try
        {
            Assert.True(await correlation.IsOpenSorSeGeneratedAsync(configuration, hint, CancellationToken.None));
            Assert.False(await correlation.IsOpenSorSeGeneratedAsync(configuration, hint, CancellationToken.None));
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task ChangedDestinationIdentity_IsNotMisclassifiedAsOpenSorSeGenerated()
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var original = Path.Combine(root, "old.txt");
        var result = Path.Combine(root, "new.txt");
        await File.WriteAllTextAsync(result, "before");
        var identity = Identity(result);
        var journal = new InMemoryOperationJournalStore();
        await journal.UpsertAsync(Operation(original, result, identity), CancellationToken.None);
        await File.WriteAllTextAsync(result, "externally modified content");
        File.SetLastWriteTimeUtc(result, DateTime.UtcNow.AddMinutes(1));
        var correlation = new OperationJournalWatchedExecutionCorrelation(
            journal,
            new WatchedFolderPathPolicy());
        var configuration = Configuration(root);

        try
        {
            var matched = await correlation.IsOpenSorSeGeneratedAsync(
                configuration,
                new WatchedFolderHint(
                    configuration.Id,
                    WatchedPathChangeKind.FileModified,
                    result,
                    null,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
            Assert.False(matched);
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task EventOutsideRoot_IsNeverCorrelated()
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var outside = Path.Combine(workspace, "outside.txt");
        await File.WriteAllTextAsync(outside, "outside");
        var journal = new InMemoryOperationJournalStore();
        await journal.UpsertAsync(
            Operation(Path.Combine(root, "old.txt"), outside, Identity(outside)),
            CancellationToken.None);
        var correlation = new OperationJournalWatchedExecutionCorrelation(
            journal,
            new WatchedFolderPathPolicy());
        var configuration = Configuration(root);

        try
        {
            Assert.False(await correlation.IsOpenSorSeGeneratedAsync(
                configuration,
                new WatchedFolderHint(
                    configuration.Id,
                    WatchedPathChangeKind.FileCreated,
                    outside,
                    null,
                    DateTimeOffset.UtcNow),
                CancellationToken.None));
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    [Fact]
    public async Task SuccessfulRollbackEvent_IsCorrelatedAgainstRestoredIdentity()
    {
        var workspace = CreateWorkspace();
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var original = Path.Combine(root, "restored.txt");
        var attemptedResult = Path.Combine(root, "attempted.txt");
        await File.WriteAllTextAsync(original, "restored");
        var journal = new InMemoryOperationJournalStore();
        await journal.UpsertAsync(
            Operation(
                original,
                attemptedResult,
                Identity(original),
                JournalActionResult.RolledBack,
                JournalRollbackResult.Succeeded),
            CancellationToken.None);
        var correlation = new OperationJournalWatchedExecutionCorrelation(
            journal,
            new WatchedFolderPathPolicy());
        var configuration = Configuration(root);

        try
        {
            Assert.True(await correlation.IsOpenSorSeGeneratedAsync(
                configuration,
                new WatchedFolderHint(
                    configuration.Id,
                    WatchedPathChangeKind.FileRenamed,
                    original,
                    attemptedResult,
                    DateTimeOffset.UtcNow),
                CancellationToken.None));
        }
        finally
        {
            DeleteWorkspace(workspace);
        }
    }

    private static OperationJournalRecord Operation(
        string original,
        string result,
        FileIdentitySnapshot identity,
        JournalActionResult executionResult = JournalActionResult.Succeeded,
        JournalRollbackResult rollbackResult = JournalRollbackResult.NotRequired) => new(
        OperationJournalSchema.CurrentVersion,
        "operation:1",
        "plan:1",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        "1.2.0",
        OperationStatus.Succeeded,
        "Review Changes",
        Path.GetDirectoryName(original)!,
        [
            new OperationJournalAction(
                "action:1",
                ChangeActionType.RenameFile,
                ChangeSuggestionSource.DeterministicRule,
                original,
                result,
                result,
                identity,
                identity,
                ChangeValidationState.Valid,
                executionResult,
                false,
                ChangeConflictCategory.None,
                null,
                [],
                rollbackResult == JournalRollbackResult.Succeeded,
                rollbackResult,
                executionResult == JournalActionResult.Succeeded,
                JournalUndoStatus.Available,
                null,
                null,
                null,
                null,
                false),
        ],
        false,
        "Succeeded.");

    private static FileIdentitySnapshot Identity(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        return new FileIdentitySnapshot(
            "test",
            info.Length,
            new DateTimeOffset(DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(info.CreationTimeUtc, DateTimeKind.Utc)),
            null);
    }

    private static WatchedFolderConfiguration Configuration(string root) => new(
        "watch:1",
        root,
        "Root",
        true,
        true,
        [],
        [],
        "default",
        null,
        true,
        false,
        new WatchedFolderNotificationPreferences(),
        TimeSpan.FromSeconds(2),
        null,
        null,
        WatchedFolderStatus.Watching,
        "catalogue:1");

    private static string CreateWorkspace()
    {
        var path = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"opensorse-watched-correlation-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteWorkspace(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), fullPath, StringComparison.OrdinalIgnoreCase);
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}
