#pragma warning disable CS1591

using OpenSorSe.Executor.Models;

namespace OpenSorSe.Executor;

/// <summary>Captures proposal-time filesystem identities and creates non-mutating plans.</summary>
public sealed class ChangePlanFactory : IChangePlanFactory
{
    private readonly IFileSystemGateway _fileSystem;
    private readonly IChangePlanValidator _validator;
    private readonly IChangePlanStore _store;

    public ChangePlanFactory(
        IFileSystemGateway fileSystem,
        IChangePlanValidator validator,
        IChangePlanStore store)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<ChangePlan> CreateAsync(
        ChangePlanCreationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Actions);
        if (request.Actions.Count is < 1 or > ChangePlanSchema.MaximumActions)
        {
            throw new ArgumentException("A Change Plan must contain a supported number of actions.", nameof(request));
        }

        var root = _fileSystem.NormalizePath(request.RootPath);
        var planId = $"plan:{Guid.NewGuid():N}";
        var proposals = AddRequiredDirectoryProposals(root, request.Actions);
        var actions = new List<ProposedChangeAction>(proposals.Count);
        for (var index = 0; index < proposals.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var proposal = proposals[index];
            var source = string.IsNullOrWhiteSpace(proposal.SourcePath)
                ? null
                : _fileSystem.NormalizePath(proposal.SourcePath);
            var destination = Path.IsPathRooted(proposal.DestinationPath)
                ? _fileSystem.NormalizePath(proposal.DestinationPath)
                : _fileSystem.NormalizePath(Path.Combine(root, proposal.DestinationPath));
            FileIdentitySnapshot? identity = null;
            if (proposal.ActionType != ChangeActionType.CreateDirectory && source is not null)
            {
                identity = await _fileSystem.CaptureFileIdentityAsync(
                    source,
                    includeHash: !string.IsNullOrWhiteSpace(proposal.ContentHash),
                    cancellationToken).ConfigureAwait(false);
                if (identity is null &&
                    proposal.SourceSizeInBytes is long suppliedSize &&
                    proposal.SourceLastWriteTimeUtc is DateTimeOffset suppliedTime)
                {
                    identity = new FileIdentitySnapshot(
                        proposal.SourceFileIdentity ??
                        FormattableString.Invariant($"source:{suppliedSize}:{suppliedTime.UtcTicks}"),
                        suppliedSize,
                        suppliedTime.ToUniversalTime(),
                        DateTimeOffset.UnixEpoch,
                        proposal.ContentHash);
                }
            }

            actions.Add(new ProposedChangeAction(
                $"action:{Guid.NewGuid():N}",
                planId,
                proposal.ActionType,
                source,
                destination,
                source is null ? null : Path.GetFileName(source),
                proposal.ActionType == ChangeActionType.RenameFile ? Path.GetFileName(destination) : null,
                identity,
                proposal.SuggestionSource,
                Bounded(proposal.Reason, 1_024),
                ChangeValidationState.NotValidated,
                ChangeApprovalState.Pending,
                index + 1,
                [],
                [],
                false,
                BoundedOrNull(proposal.AiModel, 256),
                BoundedOrNull(proposal.AiRequestCorrelationId, 256))
            {
                WorkflowProvenance = CloneProvenance(proposal.WorkflowProvenance),
            });
        }

        var draft = new ChangePlan(
            ChangePlanSchema.CurrentVersion,
            planId,
            DateTimeOffset.UtcNow,
            BoundedOrNull(request.SourceScanId, 256),
            root,
            ChangePlanStatus.AwaitingReview,
            Array.AsReadOnly(actions
                .OrderBy(action => action.ExecutionOrder)
                .ThenBy(action => action.ActionId, StringComparer.Ordinal)
                .ToArray()),
            Array.AsReadOnly((request.Warnings ?? []).Select(warning => Bounded(warning, 1_024)).ToArray()),
            null,
            false);
        var validated = await _validator.ValidateAsync(
            draft,
            ChangePlanValidationPhase.Creation,
            cancellationToken).ConfigureAwait(false);
        await _store.UpsertAsync(validated.Plan, cancellationToken).ConfigureAwait(false);
        return validated.Plan;
    }

    private IReadOnlyList<ChangeActionProposal> AddRequiredDirectoryProposals(
        string root,
        IReadOnlyList<ChangeActionProposal> supplied)
    {
        var result = supplied.ToList();
        var requestedDirectories = supplied
            .Where(action => action.ActionType == ChangeActionType.CreateDirectory)
            .Select(action => NormalizeDestination(root, action.DestinationPath))
            .ToHashSet(PathComparer);
        var inferred = new Dictionary<string, ChangeActionProposal>(PathComparer);
        foreach (var proposal in supplied.Where(action => action.ActionType != ChangeActionType.CreateDirectory))
        {
            var destination = NormalizeDestination(root, proposal.DestinationPath);
            var parent = Path.GetDirectoryName(destination);
            while (!string.IsNullOrWhiteSpace(parent) &&
                   IsWithinRoot(root, parent) &&
                   !PathComparer.Equals(parent, root))
            {
                if (_fileSystem.DirectoryExists(parent) || requestedDirectories.Contains(parent))
                {
                    break;
                }

                if (!inferred.TryGetValue(parent, out var existing) ||
                    existing.WorkflowProvenance is null && proposal.WorkflowProvenance is not null)
                {
                    inferred[parent] = proposal;
                }

                parent = Path.GetDirectoryName(parent);
            }
        }

        foreach (var item in inferred
                     .OrderBy(pair => pair.Key.Count(character => character == Path.DirectorySeparatorChar))
                     .ThenBy(pair => pair.Key, PathComparer))
        {
            result.Add(new ChangeActionProposal(
                ChangeActionType.CreateDirectory,
                null,
                item.Key,
                item.Value.SuggestionSource,
                $"Required destination folder for {item.Value.Reason}",
                0)
            {
                WorkflowProvenance = item.Value.WorkflowProvenance,
            });
        }

        return result
            .OrderBy(action => action.ActionType == ChangeActionType.CreateDirectory ? 0 : 1)
            .ThenBy(action => action.ExecutionOrder)
            .ToArray();
    }

    private string NormalizeDestination(string root, string destination) =>
        Path.IsPathRooted(destination)
            ? _fileSystem.NormalizePath(destination)
            : _fileSystem.NormalizePath(Path.Combine(root, destination));

    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool IsWithinRoot(string root, string path)
    {
        var rootWithSeparator = $"{Path.TrimEndingDirectorySeparator(root)}{Path.DirectorySeparatorChar}";
        return PathComparer.Equals(root, path) ||
               path.StartsWith(rootWithSeparator, PathComparison);
    }

    private static string Bounded(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "No reason was supplied." : value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string? BoundedOrNull(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static ChangeWorkflowProvenance? CloneProvenance(ChangeWorkflowProvenance? provenance)
    {
        if (provenance is null)
        {
            return null;
        }

        return provenance with
        {
            ValuesUsed = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                provenance.ValuesUsed.ToDictionary(
                    pair => Bounded(pair.Key, 128),
                    pair => Bounded(pair.Value, 512),
                    StringComparer.OrdinalIgnoreCase)),
            EvidenceSources = Array.AsReadOnly(provenance.EvidenceSources
                .Select(value => Bounded(value, 256))
                .Distinct(StringComparer.Ordinal)
                .ToArray()),
            Warnings = Array.AsReadOnly(provenance.Warnings
                .Select(value => Bounded(value, 1_024))
                .ToArray()),
            UnresolvedFields = Array.AsReadOnly(provenance.UnresolvedFields
                .Select(value => Bounded(value, 128))
                .ToArray()),
        };
    }
}
