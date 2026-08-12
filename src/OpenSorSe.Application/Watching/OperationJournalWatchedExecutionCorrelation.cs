#pragma warning disable CS1591

using System.Collections.Concurrent;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Application.Watching;

/// <summary>
/// Correlates watcher hints with verified paths recorded by OmniSorSe execution.
/// </summary>
/// <remarks>
/// Correlation prevents a successful OmniSorSe move/rename from recursively
/// producing the same suggestion. It is bounded and consume-on-match; failure
/// to read the journal fails open to normal reconciliation rather than hiding
/// an external filesystem change.
/// </remarks>
public sealed class OperationJournalWatchedExecutionCorrelation : IWatchedExecutionCorrelation
{
    private const int MaximumConsumedEventKeys = 10_000;
    private readonly IOperationJournalStore _journalStore;
    private readonly WatchedFolderPathPolicy _pathPolicy;
    private readonly ConcurrentDictionary<string, byte> _consumed = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _consumedOrder = new();
    private int _consumedCount;

    public OperationJournalWatchedExecutionCorrelation(
        IOperationJournalStore journalStore,
        WatchedFolderPathPolicy pathPolicy)
    {
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
    }

    public async Task<bool> IsOpenSorSeGeneratedAsync(
        WatchedFolderConfiguration configuration,
        WatchedFolderHint hint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(hint);
        if (hint.Kind == WatchedPathChangeKind.Overflow ||
            string.IsNullOrWhiteSpace(hint.Path) ||
            !_pathPolicy.IsWithinRoot(configuration.FolderPath, hint.Path))
        {
            return false;
        }

        var operations = await LoadOperationsAsync(cancellationToken).ConfigureAwait(false);
        return operations is not null && Matches(configuration, hint, operations);
    }

    public async Task<IReadOnlyList<bool>> ClassifyBatchAsync(
        WatchedFolderConfiguration configuration,
        IReadOnlyList<WatchedFolderHint> hints,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(hints);
        var operations = await LoadOperationsAsync(cancellationToken).ConfigureAwait(false);
        if (operations is null)
        {
            return Array.AsReadOnly(new bool[hints.Count]);
        }

        var results = new bool[hints.Count];
        for (var index = 0; index < hints.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results[index] = Matches(configuration, hints[index], operations);
        }

        return Array.AsReadOnly(results);
    }

    private async Task<IReadOnlyList<OperationJournalRecord>?> LoadOperationsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _journalStore.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private bool Matches(
        WatchedFolderConfiguration configuration,
        WatchedFolderHint hint,
        IReadOnlyList<OperationJournalRecord> operations)
    {
        if (hint.Kind == WatchedPathChangeKind.Overflow ||
            string.IsNullOrWhiteSpace(hint.Path) ||
            !_pathPolicy.IsWithinRoot(configuration.FolderPath, hint.Path))
        {
            return false;
        }

        foreach (var operation in operations
                     .Where(operation => _pathPolicy.Overlaps(operation.AffectedRootFolder, configuration.FolderPath))
                     .OrderByDescending(operation => operation.StartedAtUtc))
        {
            foreach (var action in operation.Actions)
            {
                if (!IsEligible(action))
                {
                    continue;
                }

                foreach (var candidatePath in CandidatePaths(action))
                {
                    if (!WatchedFolderPathPolicy.PathComparer.Equals(candidatePath, hint.Path))
                    {
                        continue;
                    }

                    var consumedKey = $"{operation.OperationId}|{action.ActionId}|{Path.GetFullPath(hint.Path)}";
                    if (_consumed.ContainsKey(consumedKey) ||
                        !MatchesCurrentEvidence(action))
                    {
                        continue;
                    }

                    if (_consumed.TryAdd(consumedKey, 0))
                    {
                        RememberConsumedKey(consumedKey);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void RememberConsumedKey(string key)
    {
        _consumedOrder.Enqueue(key);
        var count = Interlocked.Increment(ref _consumedCount);
        while (count > MaximumConsumedEventKeys && _consumedOrder.TryDequeue(out var oldest))
        {
            if (_consumed.TryRemove(oldest, out _))
            {
                count = Interlocked.Decrement(ref _consumedCount);
            }
            else
            {
                count = Volatile.Read(ref _consumedCount);
            }
        }
    }

    private static bool IsEligible(OperationJournalAction action) =>
        action.ExecutionResult is
            JournalActionResult.Pending or
            JournalActionResult.Succeeded or
            JournalActionResult.RolledBack ||
        action.RollbackResult == JournalRollbackResult.Succeeded ||
        action.UndoStatus == JournalUndoStatus.Succeeded;

    private static IEnumerable<string> CandidatePaths(OperationJournalAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.OriginalPath))
        {
            yield return action.OriginalPath;
        }

        if (!string.IsNullOrWhiteSpace(action.IntendedDestinationPath))
        {
            yield return action.IntendedDestinationPath;
        }

        if (!string.IsNullOrWhiteSpace(action.ActualResultingPath))
        {
            yield return action.ActualResultingPath;
        }
    }

    private static bool MatchesCurrentEvidence(OperationJournalAction action)
    {
        if (action.ActionType == ChangeActionType.CreateDirectory)
        {
            if (action.ExecutionResult == JournalActionResult.RolledBack ||
                action.RollbackResult == JournalRollbackResult.Succeeded ||
                action.UndoStatus == JournalUndoStatus.Succeeded)
            {
                return action.DirectoryCreatedByOpenSorSe &&
                       !Directory.Exists(action.IntendedDestinationPath);
            }

            return action.DirectoryCreatedByOpenSorSe && Directory.Exists(action.IntendedDestinationPath);
        }

        if ((action.ExecutionResult == JournalActionResult.RolledBack ||
             action.RollbackResult == JournalRollbackResult.Succeeded ||
             action.UndoStatus == JournalUndoStatus.Succeeded) &&
            action.OriginalPath is not null &&
            File.Exists(action.OriginalPath) &&
            !File.Exists(action.IntendedDestinationPath))
        {
            return MatchesIdentity(action.OriginalPath, action.PreExecutionIdentity);
        }

        var result = action.ActualResultingPath ?? action.IntendedDestinationPath;
        return File.Exists(result) &&
               (action.OriginalPath is null || !File.Exists(action.OriginalPath)) &&
               MatchesIdentity(result, action.PostExecutionIdentity ?? action.PreExecutionIdentity);
    }

    private static bool MatchesIdentity(string path, FileIdentitySnapshot? identity)
    {
        if (identity is null)
        {
            return true;
        }

        try
        {
            var info = new FileInfo(path);
            info.Refresh();
            return info.Length == identity.SizeInBytes &&
                   new DateTimeOffset(
                       DateTime.SpecifyKind(info.LastWriteTimeUtc, DateTimeKind.Utc)) == identity.LastWriteTimeUtc;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
