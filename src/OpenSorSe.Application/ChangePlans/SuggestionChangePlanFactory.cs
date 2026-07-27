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

/// <summary>Keeps AI and rule output upstream of the generic plan validator and executor.</summary>
public sealed class SuggestionChangePlanFactory : ISuggestionChangePlanFactory
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

