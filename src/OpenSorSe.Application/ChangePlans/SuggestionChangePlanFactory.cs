#pragma warning disable CS1591

using OpenSorSe.Application.AI;
using OpenSorSe.Application.Models;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;
using OpenSorSe.Rules.Models;

namespace OpenSorSe.Application.ChangePlans;

/// <summary>Converts validated application suggestions into non-mutating Change Plans.</summary>
public interface ISuggestionChangePlanFactory
{
    Task<ChangePlan> CreateRenamePlanAsync(
        ResultFile file,
        AiFileRenameSuggestion suggestion,
        string reviewedFileName,
        string? sourceScanId,
        CancellationToken cancellationToken);

    Task<ChangePlan> CreateFolderStructurePlanAsync(
        IReadOnlyList<ResultFile> files,
        AiFolderStructurePlan suggestion,
        string? sourceScanId,
        CancellationToken cancellationToken);

    Task<ChangePlan> CreateRulePlanAsync(
        ResultsSnapshot snapshot,
        CancellationToken cancellationToken);
}

/// <summary>
/// Creates a reviewable, non-destructive plan for moving unwanted exact copies out of normal scan results.
/// </summary>
public interface IDuplicateRemovalPlanFactory
{
    /// <summary>
    /// Creates a Change Plan that preserves at least one known member and moves selected copies into the
    /// scan root's application-owned recovery area. The method never executes filesystem changes.
    /// </summary>
    Task<ChangePlan> CreateDuplicateRemovalPlanAsync(
        IReadOnlyList<ResultFile> filesToRemove,
        IReadOnlyList<ResultFile> filesToKeep,
        string? sourceScanId,
        CancellationToken cancellationToken);
}

/// <summary>Keeps AI and rule output upstream of the generic plan validator and executor.</summary>
public sealed class SuggestionChangePlanFactory : ISuggestionChangePlanFactory, IDuplicateRemovalPlanFactory
{
    private readonly IChangePlanFactory _factory;

    public SuggestionChangePlanFactory(IChangePlanFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <inheritdoc />
    public Task<ChangePlan> CreateRenamePlanAsync(
        ResultFile file,
        AiFileRenameSuggestion suggestion,
        string reviewedFileName,
        string? sourceScanId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(suggestion);
        var parent = Path.GetDirectoryName(file.FullPath)
            ?? throw new ArgumentException("The selected file has no parent folder.", nameof(file));
        var destination = Path.Combine(parent, reviewedFileName);
        return _factory.CreateAsync(
            new ChangePlanCreationRequest(
                parent,
                sourceScanId,
                [
                    new ChangeActionProposal(
                        ChangeActionType.RenameFile,
                        file.FullPath,
                        destination,
                        ChangeSuggestionSource.Ai,
                        suggestion.Reason,
                        1,
                        file.Id,
                        file.SizeInBytes,
                        file.LastWriteTimeUtc,
                        AiModel: suggestion.Model,
                        AiRequestCorrelationId: suggestion.SuggestionId),
                ]),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ChangePlan> CreateFolderStructurePlanAsync(
        IReadOnlyList<ResultFile> files,
        AiFolderStructurePlan suggestion,
        string? sourceScanId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(suggestion);
        if (files.Count == 0)
        {
            throw new ArgumentException("At least one known file is required.", nameof(files));
        }

        var byId = files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        if (suggestion.Items.Count != files.Count ||
            suggestion.Items.Any(item => !byId.ContainsKey(item.FileId)) ||
            suggestion.Items.Select(item => item.FileId).Distinct(StringComparer.Ordinal).Count() != files.Count)
        {
            throw new ArgumentException("The folder suggestion does not map each known file exactly once.", nameof(suggestion));
        }

        var root = CommonRoot(files.Select(file => file.FullPath));
        var actions = suggestion.Items.Select((item, index) =>
        {
            var file = byId[item.FileId];
            var destination = Path.Combine(root, item.DestinationFolder, file.DisplayFileName);
            return new ChangeActionProposal(
                ChangeActionType.MoveFile,
                file.FullPath,
                destination,
                ChangeSuggestionSource.Ai,
                suggestion.Reason,
                index + 1,
                file.Id,
                file.SizeInBytes,
                file.LastWriteTimeUtc,
                AiModel: suggestion.Model,
                AiRequestCorrelationId: suggestion.PlanId);
        }).ToArray();
        return _factory.CreateAsync(
            new ChangePlanCreationRequest(root, sourceScanId, actions),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ChangePlan> CreateRulePlanAsync(
        ResultsSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var files = snapshot.Files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        var supported = snapshot.PlannedOperations
            .Where(operation =>
                operation.SourceFileId is not null &&
                operation.DestinationPath is not null &&
                operation.Kind is PlannedOperationKind.Move or PlannedOperationKind.Rename &&
                files.ContainsKey(operation.SourceFileId))
            .ToArray();
        if (supported.Length == 0)
        {
            throw new ArgumentException("The snapshot contains no supported rule proposals.", nameof(snapshot));
        }

        var sourceFiles = supported.Select(operation => files[operation.SourceFileId!]).ToArray();
        var root = CommonRoot(sourceFiles.Select(file => file.FullPath));
        var actions = supported.Select((operation, index) =>
        {
            var file = files[operation.SourceFileId!];
            return new ChangeActionProposal(
                operation.Kind == PlannedOperationKind.Rename
                    ? ChangeActionType.RenameFile
                    : ChangeActionType.MoveFile,
                file.FullPath,
                operation.DestinationPath!,
                ChangeSuggestionSource.DeterministicRule,
                operation.RuleDisplayName ?? "Deterministic sorting rule.",
                index + 1,
                file.Id,
                file.SizeInBytes,
                file.LastWriteTimeUtc);
        }).ToArray();
        return _factory.CreateAsync(
            new ChangePlanCreationRequest(root, snapshot.SessionId, actions),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ChangePlan> CreateDuplicateRemovalPlanAsync(
        IReadOnlyList<ResultFile> filesToRemove,
        IReadOnlyList<ResultFile> filesToKeep,
        string? sourceScanId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filesToRemove);
        ArgumentNullException.ThrowIfNull(filesToKeep);
        if (filesToRemove.Count == 0)
        {
            throw new ArgumentException("Select at least one unwanted duplicate copy.", nameof(filesToRemove));
        }

        if (filesToKeep.Count == 0)
        {
            throw new ArgumentException("At least one known duplicate copy must remain.", nameof(filesToKeep));
        }

        var allFiles = filesToRemove.Concat(filesToKeep).ToArray();
        if (allFiles.Any(file => file is null) ||
            allFiles.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count() != allFiles.Length)
        {
            throw new ArgumentException("Duplicate removal requires distinct known files from one result group.");
        }

        var groupIds = allFiles.Select(file => file.DuplicateGroupId).Distinct(StringComparer.Ordinal).ToArray();
        if (groupIds.Length != 1 || string.IsNullOrWhiteSpace(groupIds[0]))
        {
            throw new ArgumentException("Duplicate removal is limited to one verified exact-copy group.");
        }

        var root = CommonRoot(allFiles.Select(file => file.FullPath));
        var recoveryRoot = Path.Combine(root, ".opensorse", "duplicate-recovery", SafePathSegment(groupIds[0]!));
        var actions = filesToRemove.Select((file, index) => new ChangeActionProposal(
            ChangeActionType.MoveFile,
            file.FullPath,
            Path.Combine(recoveryRoot, SafePathSegment(file.Id), file.DisplayFileName),
            ChangeSuggestionSource.DuplicateAnalysis,
            $"The user selected this exact copy for safe removal; {filesToKeep.Count} known identical copy or copies will remain.",
            index + 1,
            file.Id,
            file.SizeInBytes,
            file.LastWriteTimeUtc)).ToArray();
        return _factory.CreateAsync(
            new ChangePlanCreationRequest(
                root,
                sourceScanId,
                actions,
                [
                    "Duplicate removal moves selected copies into the hidden .opensorse/duplicate-recovery area; it does not permanently delete them or immediately reclaim disk space.",
                    "Successful moves are recorded in the Operation Journal and can be undone while normal conflict checks still pass.",
                ]),
            cancellationToken);
    }

    private static string SafePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = value
            .Take(80)
            .Select(character => invalid.Contains(character) || char.IsControl(character) || character is '/' or '\\'
                ? '-'
                : character)
            .ToArray();
        var segment = new string(characters).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(segment) || segment is "." or ".."
            ? "copy"
            : segment;
    }

    internal static string CommonRoot(IEnumerable<string> filePaths)
    {
        var directories = filePaths
            .Select(path => Path.GetDirectoryName(Path.GetFullPath(path))
                            ?? throw new ArgumentException("A source path has no parent folder.", nameof(filePaths)))
            .Distinct(ChangePlanFactory.PathComparer)
            .ToArray();
        if (directories.Length == 0)
        {
            throw new ArgumentException("No source paths were supplied.", nameof(filePaths));
        }

        var root = directories[0];
        while (directories.Any(directory => !ChangePlanFactory.IsWithinRoot(root, directory)))
        {
            root = Path.GetDirectoryName(root)
                   ?? throw new ArgumentException("The sources do not share a safe selected root.", nameof(filePaths));
        }

        if (ChangePlanFactory.PathComparer.Equals(root, Path.GetPathRoot(root)))
        {
            throw new ArgumentException(
                "The selected files span more than one safe organization root.",
                nameof(filePaths));
        }

        return root;
    }
}
