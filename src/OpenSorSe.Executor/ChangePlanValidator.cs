#pragma warning disable CS1591

using OpenSorSe.Executor.Models;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Executor;

/// <summary>Performs complete, non-mutating validation at creation, review, and pre-execution.</summary>
public sealed class ChangePlanValidator : IChangePlanValidator
{
    private readonly IFileSystemGateway _fileSystem;
    private readonly IPathSemantics _pathSemantics;
    private readonly IFileSystemCapabilities? _fileSystemCapabilities;

    public ChangePlanValidator(IFileSystemGateway fileSystem)
        : this(fileSystem, PlatformServices.CurrentPathSemantics, null)
    {
    }

    public ChangePlanValidator(
        IFileSystemGateway fileSystem,
        IPathSemantics pathSemantics,
        IFileSystemCapabilities? fileSystemCapabilities = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathSemantics = pathSemantics ?? throw new ArgumentNullException(nameof(pathSemantics));
        _fileSystemCapabilities = fileSystemCapabilities;
    }

    /// <inheritdoc />
    public async Task<ChangePlanValidationResult> ValidateAsync(
        ChangePlan plan,
        ChangePlanValidationPhase phase,
        CancellationToken cancellationToken)
    {
        ValidatePlanShape(plan);
        var root = _fileSystem.NormalizePath(plan.RootPath);
        var candidates = plan.Actions
            .Where(action => action.ApprovalState != ChangeApprovalState.Rejected)
            .ToArray();
        var normalizedCandidates = candidates.Select(action => new
        {
            Action = action,
            Source = TryNormalizePath(action.SourcePath),
            Destination = TryNormalizePath(action.DestinationPath),
        }).ToArray();
        var duplicateSources = candidates
            .Select(action => TryNormalizePath(action.SourcePath))
            .Where(path => path is not null)
            .Cast<string>()
            .GroupBy(path => path, _pathSemantics.Comparer)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(_pathSemantics.Comparer);
        var duplicateDestinations = normalizedCandidates
            .Where(item => item.Destination is not null)
            .GroupBy(item => item.Destination!, _pathSemantics.Comparer)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(_pathSemantics.Comparer);
        var sourcePaths = normalizedCandidates
            .Where(item => item.Source is not null)
            .Select(item => item.Source!)
            .ToHashSet(_pathSemantics.Comparer);
        var plannedDirectories = normalizedCandidates
            .Where(item =>
                item.Action.ActionType == ChangeActionType.CreateDirectory &&
                item.Destination is not null)
            .Select(item => item.Destination!)
            .ToHashSet(_pathSemantics.Comparer);

        var validated = new List<ProposedChangeAction>(plan.Actions.Count);
        foreach (var action in plan.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (action.ApprovalState == ChangeApprovalState.Rejected)
            {
                validated.Add(action with
                {
                    ValidationState = ChangeValidationState.NotValidated,
                    Warnings = [],
                    Conflicts = [],
                });
                continue;
            }

            var conflicts = new List<ChangeConflict>();
            var warnings = new List<string>();
            await ValidateActionAsync(
                action,
                root,
                duplicateSources,
                duplicateDestinations,
                sourcePaths,
                plannedDirectories,
                conflicts,
                warnings,
                phase,
                cancellationToken).ConfigureAwait(false);
            var state = StateFor(conflicts, warnings);
            validated.Add(action with
            {
                ValidationState = state,
                Warnings = Array.AsReadOnly(warnings.ToArray()),
                Conflicts = Array.AsReadOnly(conflicts.ToArray()),
            });
        }

        var active = validated.Where(action => action.ApprovalState != ChangeApprovalState.Rejected).ToArray();
        var stale = active.Count(action => action.ValidationState == ChangeValidationState.Stale);
        var invalid = active.Count(action => action.ValidationState == ChangeValidationState.Invalid);
        var conflict = active.Count(action => action.ValidationState == ChangeValidationState.Conflict);
        var warning = active.Count(action => action.ValidationState == ChangeValidationState.Warning);
        var valid = active.Count(action => action.ValidationState == ChangeValidationState.Valid);
        var approved = active.Where(action => action.ApprovalState == ChangeApprovalState.Approved).ToArray();
        var canApply = approved.Length > 0 &&
                       approved.All(action =>
                           action.ValidationState is ChangeValidationState.Valid or ChangeValidationState.Warning &&
                           action.Conflicts.All(item => !item.IsBlocking));
        var sourceStale = stale > 0;
        var status = sourceStale
            ? ChangePlanStatus.Invalidated
            : invalid + conflict > 0
                ? ChangePlanStatus.ValidationFailed
                : approved.Length > 0 && approved.Length == active.Length
                    ? ChangePlanStatus.Approved
                    : ChangePlanStatus.AwaitingReview;
        var updated = plan with
        {
            RootPath = root,
            Actions = Array.AsReadOnly(validated.ToArray()),
            Status = status,
            ValidatedAtUtc = DateTimeOffset.UtcNow,
            IsSourceScanStale = sourceStale,
        };
        var summary =
            $"{valid} valid, {warning} with warnings, {invalid} invalid, {conflict} conflicting, and {stale} stale action(s).";
        return new ChangePlanValidationResult(updated, canApply, valid, warning, invalid, conflict, stale, summary);
    }

    private async Task ValidateActionAsync(
        ProposedChangeAction action,
        string root,
        IReadOnlySet<string> duplicateSources,
        IReadOnlySet<string> duplicateDestinations,
        IReadOnlySet<string> sourcePaths,
        IReadOnlySet<string> plannedDirectories,
        ICollection<ChangeConflict> conflicts,
        ICollection<string> warnings,
        ChangePlanValidationPhase phase,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(action.ActionType))
        {
            Add(ChangeConflictCategory.UnsupportedAction, "The action type is not supported.", true);
            return;
        }

        string destination;
        try
        {
            destination = _fileSystem.NormalizePath(action.DestinationPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Add(ChangeConflictCategory.DestinationInvalid, "The destination path is invalid.", true);
            return;
        }

        if (destination.Length > ChangePlanSchema.MaximumPathLength)
        {
            Add(ChangeConflictCategory.PathTooLong, "The destination path exceeds the supported path length.", true);
        }

        if (!_pathSemantics.IsWithinRoot(root, destination))
        {
            Add(ChangeConflictCategory.DestinationOutsideRoot, "The destination is outside the selected root folder.", true);
        }

        if (duplicateDestinations.Contains(destination))
        {
            Add(ChangeConflictCategory.DuplicateDestination, "More than one action targets this destination.", true);
        }

        if (action.ActionType == ChangeActionType.CreateDirectory)
        {
            if (action.SourcePath is not null)
            {
                Add(ChangeConflictCategory.InvalidAction, "A create-directory action cannot have a source path.", true);
            }

            if (_fileSystem.FileExists(destination))
            {
                Add(ChangeConflictCategory.DirectoryCollidesWithFile, "A file occupies the proposed directory path.", true);
            }
            else if (_fileSystem.DirectoryExists(destination))
            {
                warnings.Add("The directory already exists and will not be recreated.");
            }

            ValidateParent(destination, root, plannedDirectories, conflicts);
            return;
        }

        if (string.IsNullOrWhiteSpace(action.SourcePath))
        {
            Add(ChangeConflictCategory.InvalidAction, "The file action has no source path.", true);
            return;
        }

        string source;
        try
        {
            source = _fileSystem.NormalizePath(action.SourcePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Add(ChangeConflictCategory.InvalidAction, "The source path is invalid.", true);
            return;
        }

        if (!_pathSemantics.IsWithinRoot(root, source))
        {
            Add(ChangeConflictCategory.SourceOutsideRoot, "The source file is outside the selected root folder.", true);
        }

        if (duplicateSources.Contains(source))
        {
            Add(ChangeConflictCategory.ConflictingSourceActions, "The same source file has more than one action.", true);
        }

        var samePlatformPath = _pathSemantics.Comparer.Equals(source, destination);
        var exactSamePath = string.Equals(source, destination, StringComparison.Ordinal);
        if (exactSamePath)
        {
            Add(ChangeConflictCategory.InvalidAction, "The source and destination paths are identical.", true);
        }
        else if (samePlatformPath)
        {
            if (action.ActionType != ChangeActionType.RenameFile)
            {
                Add(ChangeConflictCategory.InvalidAction, "Only a rename may change path casing.", true);
            }
            else
            {
                conflicts.Add(new ChangeConflict(
                    ChangeConflictCategory.CaseOnlyRename,
                    "This case-only rename will use a verified temporary name.",
                    false));
                warnings.Add("Case-only rename requires a temporary intermediate path.");
            }
        }

        if (sourcePaths.Contains(destination) && !samePlatformPath)
        {
            Add(
                ChangeConflictCategory.ExecutionOrderConflict,
                "The destination is another action's source and cannot be ordered safely in this plan.",
                true);
        }

        if (action.ActionType == ChangeActionType.RenameFile &&
            !_pathSemantics.Comparer.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(destination)))
        {
            Add(ChangeConflictCategory.InvalidAction, "A rename must remain in the same directory.", true);
        }

        var fileName = Path.GetFileName(destination);
        if (!_pathSemantics.IsValidFileName(
                fileName,
                FileNamePortabilityMode.CurrentPlatform,
                out var fileNameReason))
        {
            Add(
                ChangeConflictCategory.InvalidFileName,
                $"The proposed filename is invalid under current-platform rules. {fileNameReason}",
                true);
        }

        if (!_fileSystem.FileExists(source))
        {
            var renamed = await FindRenamedSourceAsync(
                source,
                action.SourceIdentity,
                cancellationToken).ConfigureAwait(false);
            Add(
                renamed
                    ? ChangeConflictCategory.SourceRenamedExternally
                    : ChangeConflictCategory.SourceMissing,
                renamed
                    ? "The source appears to have been renamed outside OmniSorSe."
                    : "The source file no longer exists.",
                true);
            return;
        }

        if (_fileSystem.IsReparsePoint(source))
        {
            Add(ChangeConflictCategory.SourceTypeUnsupported, "Reparse-point source files are not supported.", true);
            return;
        }

        FileIdentitySnapshot? current;
        try
        {
            current = await _fileSystem.CaptureFileIdentityAsync(
                source,
                includeHash: action.SourceIdentity?.ContentHash is not null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            Add(ChangeConflictCategory.PermissionDenied, "Access to the source file was denied.", true);
            return;
        }
        catch (IOException)
        {
            Add(ChangeConflictCategory.IoFailure, "The source file could not be inspected.", true);
            return;
        }

        if (current is null)
        {
            Add(ChangeConflictCategory.SourceTypeUnsupported, "The source is not a supported regular file.", true);
            return;
        }

        if (action.SourceIdentity is { } expected)
        {
            if (current.SizeInBytes != expected.SizeInBytes ||
                current.LastWriteTimeUtc != expected.LastWriteTimeUtc)
            {
                Add(ChangeConflictCategory.SourceChanged, "The source changed after this plan was created.", true);
            }
            else if (expected.ContentHash is not null &&
                     !string.Equals(current.ContentHash, expected.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                Add(ChangeConflictCategory.SourceHashChanged, "The source content hash no longer matches.", true);
            }
        }

        try
        {
            if (!await _fileSystem.CanOpenExclusivelyAsync(source, cancellationToken).ConfigureAwait(false))
            {
                Add(ChangeConflictCategory.SourceLocked, "The source file is in use by another process.", true);
            }
        }
        catch (UnauthorizedAccessException)
        {
            Add(ChangeConflictCategory.PermissionDenied, "The source file cannot be opened with the required access.", true);
        }

        if (!samePlatformPath &&
            (_fileSystem.FileExists(destination) || _fileSystem.DirectoryExists(destination)))
        {
            Add(ChangeConflictCategory.DestinationOccupied, "The destination is already occupied. Overwrite is disabled.", true);
        }

        ValidateParent(destination, root, plannedDirectories, conflicts);
        if (phase == ChangePlanValidationPhase.PreExecution &&
            _fileSystemCapabilities is not null)
        {
            var existingParent = ExistingParent(destination, root);
            if (existingParent is not null &&
                !_fileSystemCapabilities.CanWriteDirectory(existingParent, out var permissionExplanation))
            {
                Add(
                    ChangeConflictCategory.PermissionDenied,
                    $"The destination directory is not safely writable. {permissionExplanation}",
                    true);
            }

            if (action.ActionType == ChangeActionType.MoveFile &&
                existingParent is not null &&
                !_fileSystemCapabilities.AreOnSameFileSystem(
                    source,
                    existingParent,
                    out var filesystemExplanation))
            {
                Add(
                    ChangeConflictCategory.InvalidAction,
                    $"Cross-filesystem moves are unavailable. {filesystemExplanation}",
                    true);
            }
        }

        return;

        void Add(ChangeConflictCategory category, string message, bool blocking) =>
            conflicts.Add(new ChangeConflict(category, message, blocking));
    }

    private void ValidateParent(
        string destination,
        string root,
        IReadOnlySet<string> plannedDirectories,
        ICollection<ChangeConflict> conflicts)
    {
        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent) || !_pathSemantics.IsWithinRoot(root, parent))
        {
            conflicts.Add(new ChangeConflict(
                ChangeConflictCategory.DestinationParentUnavailable,
                "The destination parent is invalid.",
                true));
            return;
        }

        var current = parent;
        while (!string.IsNullOrWhiteSpace(current) &&
               _pathSemantics.IsWithinRoot(root, current))
        {
            if (_fileSystem.FileExists(current))
            {
                conflicts.Add(new ChangeConflict(
                    ChangeConflictCategory.DirectoryCollidesWithFile,
                    "A file blocks a required destination directory.",
                    true));
                return;
            }

            if (_fileSystem.DirectoryExists(current))
            {
                if (_fileSystem.IsReparsePoint(current))
                {
                    conflicts.Add(new ChangeConflict(
                        ChangeConflictCategory.DestinationOutsideRoot,
                        "A destination parent is a filesystem link and cannot be verified within the selected root.",
                        true));
                }

                return;
            }

            if (!plannedDirectories.Contains(current))
            {
                conflicts.Add(new ChangeConflict(
                    ChangeConflictCategory.DestinationParentUnavailable,
                    "A required destination directory is missing from the Change Plan.",
                    true));
                return;
            }

            current = Path.GetDirectoryName(current);
        }
    }

    private string? ExistingParent(string destination, string root)
    {
        var current = Path.GetDirectoryName(destination);
        while (!string.IsNullOrWhiteSpace(current) &&
               _pathSemantics.IsWithinRoot(root, current))
        {
            if (_fileSystem.DirectoryExists(current))
            {
                return current;
            }

            if (_fileSystem.FileExists(current))
            {
                return null;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private async Task<bool> FindRenamedSourceAsync(
        string originalPath,
        FileIdentitySnapshot? expected,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(originalPath);
        if (expected is null || string.IsNullOrWhiteSpace(parent) || !_fileSystem.DirectoryExists(parent))
        {
            return false;
        }

        try
        {
            foreach (var candidate in Directory.EnumerateFiles(parent).Take(500))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var identity = await _fileSystem.CaptureFileIdentityAsync(
                    candidate,
                    includeHash: expected.ContentHash is not null,
                    cancellationToken).ConfigureAwait(false);
                if (identity is not null && SameIdentity(expected, identity))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private string? TryNormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return _fileSystem.NormalizePath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
    }

    internal static bool SameIdentity(FileIdentitySnapshot expected, FileIdentitySnapshot actual) =>
        expected.SizeInBytes == actual.SizeInBytes &&
        expected.LastWriteTimeUtc == actual.LastWriteTimeUtc &&
        expected.CreationTimeUtc == actual.CreationTimeUtc &&
        string.Equals(expected.Identity, actual.Identity, StringComparison.Ordinal) &&
        (expected.ContentHash is null ||
         string.Equals(expected.ContentHash, actual.ContentHash, StringComparison.OrdinalIgnoreCase));

    private static ChangeValidationState StateFor(
        IReadOnlyCollection<ChangeConflict> conflicts,
        IReadOnlyCollection<string> warnings)
    {
        if (conflicts.Any(conflict => conflict.Category is
                ChangeConflictCategory.SourceMissing or
                ChangeConflictCategory.SourceRenamedExternally or
                ChangeConflictCategory.SourceChanged or
                ChangeConflictCategory.SourceHashChanged or
                ChangeConflictCategory.ScanStale))
        {
            return ChangeValidationState.Stale;
        }

        if (conflicts.Any(conflict => conflict.IsBlocking))
        {
            return conflicts.Any(conflict => conflict.Category is
                    ChangeConflictCategory.InvalidAction or
                    ChangeConflictCategory.UnsupportedAction or
                    ChangeConflictCategory.DestinationInvalid or
                    ChangeConflictCategory.InvalidFileName or
                    ChangeConflictCategory.PathTooLong)
                ? ChangeValidationState.Invalid
                : ChangeValidationState.Conflict;
        }

        return warnings.Count > 0 || conflicts.Count > 0
            ? ChangeValidationState.Warning
            : ChangeValidationState.Valid;
    }

    private static void ValidatePlanShape(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.SchemaVersion != ChangePlanSchema.CurrentVersion ||
            string.IsNullOrWhiteSpace(plan.PlanId) ||
            plan.CreatedAtUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(plan.RootPath) ||
            !Path.IsPathRooted(plan.RootPath) ||
            plan.Actions is null ||
            plan.Actions.Count is < 1 or > ChangePlanSchema.MaximumActions ||
            plan.Actions.Any(action =>
                action is null ||
                string.IsNullOrWhiteSpace(action.ActionId) ||
                !string.Equals(action.PlanId, plan.PlanId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(action.DestinationPath) ||
                action.ExecutionOrder < 0 ||
                action.Warnings is null ||
                action.Conflicts is null) ||
            plan.Actions.Select(action => action.ActionId).Distinct(StringComparer.Ordinal).Count() != plan.Actions.Count)
        {
            throw new ArgumentException("The Change Plan is invalid or unsupported.", nameof(plan));
        }
    }
}
