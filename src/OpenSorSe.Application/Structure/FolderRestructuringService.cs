using OpenSorSe.Core.Configuration;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Application.Structure;

/// <summary>
/// Provides deterministic preview-first organization, successful-apply repeat protection,
/// and explicitly confirmed bounded in-root moves.
/// </summary>
public sealed class FolderRestructuringService : IFolderRestructuringService
{
    private const string AlgorithmVersion = "deterministic-extension-groups/1";
    private readonly IFolderStructureSnapshotService _snapshotService;
    private readonly IStructureHistoryStore _historyStore;
    private readonly IConfigurationService _configurationService;
    private readonly IChangePlanFactory _changePlanFactory;
    private readonly IChangePlanValidator _changePlanValidator;
    private readonly IChangePlanExecutionService _executionService;

    /// <summary>Initializes the service with bounded snapshot and history dependencies.</summary>
    public FolderRestructuringService(
        IFolderStructureSnapshotService snapshotService,
        IStructureHistoryStore historyStore,
        IConfigurationService configurationService)
    {
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        var fileSystem = new PhysicalFileSystemGateway();
        var planStore = new InMemoryChangePlanStore();
        var journal = new InMemoryOperationJournalStore();
        _changePlanValidator = new ChangePlanValidator(fileSystem);
        _changePlanFactory = new ChangePlanFactory(fileSystem, _changePlanValidator, planStore);
        _executionService = new ChangePlanExecutionService(
            fileSystem,
            _changePlanValidator,
            planStore,
            journal);
    }

    /// <summary>Initializes production restructuring over the shared v1.1 Change Plan safety services.</summary>
    public FolderRestructuringService(
        IFolderStructureSnapshotService snapshotService,
        IStructureHistoryStore historyStore,
        IConfigurationService configurationService,
        IChangePlanFactory changePlanFactory,
        IChangePlanValidator changePlanValidator,
        IChangePlanExecutionService executionService)
    {
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _historyStore = historyStore ?? throw new ArgumentNullException(nameof(historyStore));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _changePlanFactory = changePlanFactory ?? throw new ArgumentNullException(nameof(changePlanFactory));
        _changePlanValidator = changePlanValidator ?? throw new ArgumentNullException(nameof(changePlanValidator));
        _executionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
    }

    /// <inheritdoc />
    public async Task<RestructuringPreviewResult> PreviewAsync(
        string rootPath,
        bool explicitOverride,
        CancellationToken cancellationToken)
    {
        if (!_configurationService.Current.Features.ShowAdvancedFeatures)
        {
            return new RestructuringPreviewResult(
                false,
                RestructuringProtectionState.FirstRun,
                null,
                "Folder restructuring is unavailable while Advanced features are disabled.");
        }

        var source = await _snapshotService.CaptureAsync(rootPath, cancellationToken).ConfigureAwait(false);
        var history = await _historyStore.ListAsync(cancellationToken).ConfigureAwait(false);
        var latestForRoot = history
            .Where(record => string.Equals(record.RootIdentity, source.RootIdentity, StringComparison.Ordinal))
            .OrderByDescending(record => record.StartedAtUtc)
            .FirstOrDefault();
        var latestApplied = history
            .Where(record =>
                string.Equals(record.RootIdentity, source.RootIdentity, StringComparison.Ordinal) &&
                record.Status == RestructuringStatus.Applied &&
                record.AppliedSnapshot is not null)
            .OrderByDescending(record => record.CompletedAtUtc ?? record.StartedAtUtc)
            .FirstOrDefault();
        var protection = EvaluateProtection(source, latestForRoot, latestApplied);
        if (protection == RestructuringProtectionState.AlreadyOrganized && !explicitOverride)
        {
            return new RestructuringPreviewResult(
                false,
                protection,
                null,
                "This unchanged root was already reorganized successfully. Use Propose restructuring again to make an explicit override request.");
        }

        var incremental = protection == RestructuringProtectionState.NewFilesOnly && !explicitOverride;
        var candidates = SelectCandidates(source, latestApplied?.AppliedSnapshot, incremental);
        var moves = BuildMoves(source, candidates);
        if (moves.Count == 0)
        {
            return new RestructuringPreviewResult(
                false,
                protection,
                null,
                explicitOverride
                    ? "The explicit override found no safe root-level files that need a new folder proposal."
                    : "No safe root-level files need a restructuring proposal.");
        }

        var proposed = BuildProposedSnapshot(source, moves);
        var operationId = $"structure:{Guid.NewGuid():N}";
        var summary = incremental
            ? $"Incremental preview: {moves.Count} new file(s) can be reviewed."
            : $"Preview: {moves.Count} root-level file(s) can be reviewed.";
        var plan = new RestructuringPlan(
            operationId,
            source.RootPath,
            source,
            proposed,
            moves,
            incremental,
            explicitOverride,
            latestApplied?.OperationId,
            AlgorithmVersion,
            summary);
        await _historyStore.UpsertAsync(
            ToHistoryRecord(
                plan,
                RestructuringApprovalState.NotRequested,
                RestructuringStatus.Previewed,
                null,
                [],
                summary,
                null),
            cancellationToken).ConfigureAwait(false);
        return new RestructuringPreviewResult(true, protection, plan, summary);
    }

    /// <inheritdoc />
    public async Task<RestructuringApplyResult> ApplyAsync(
        RestructuringPlan plan,
        string confirmedOperationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!_configurationService.Current.Features.ShowAdvancedFeatures)
        {
            const string disabledMessage =
                "Folder restructuring is unavailable while Advanced features are disabled.";
            var disabledRecord = ToHistoryRecord(
                plan,
                RestructuringApprovalState.Rejected,
                RestructuringStatus.Failed,
                null,
                [],
                disabledMessage,
                DateTimeOffset.UtcNow);
            return new RestructuringApplyResult(
                false,
                RestructuringStatus.Failed,
                disabledRecord,
                disabledMessage);
        }

        var outcomes = new List<RestructuringItemOutcome>();
        FolderStructureSnapshot? appliedSnapshot = null;
        var approval = string.Equals(
            plan.OperationId,
            confirmedOperationId,
            StringComparison.Ordinal)
            ? RestructuringApprovalState.Approved
            : RestructuringApprovalState.Rejected;
        if (approval == RestructuringApprovalState.Rejected)
        {
            return await RecordTerminalAsync(
                plan,
                approval,
                RestructuringStatus.Failed,
                null,
                outcomes,
                "Apply was rejected because the confirmation did not match the exact preview.",
                CancellationToken.None).ConfigureAwait(false);
        }

        try
        {
            ValidatePlan(plan);
            var current = await _snapshotService.CaptureAsync(plan.RootPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(current.RootIdentity, plan.SourceSnapshot.RootIdentity, StringComparison.Ordinal) ||
                !string.Equals(current.StructureFingerprint, plan.SourceSnapshot.StructureFingerprint, StringComparison.Ordinal))
            {
                return await RecordTerminalAsync(
                    plan,
                    approval,
                    RestructuringStatus.Failed,
                    null,
                    outcomes,
                    "The folder changed after preview. Create and review a fresh proposal before applying.",
                    CancellationToken.None).ConfigureAwait(false);
            }

            var resolved = ResolveMoves(plan);
            var changePlan = await _changePlanFactory.CreateAsync(
                new ChangePlanCreationRequest(
                    plan.RootPath,
                    plan.OperationId,
                    resolved.Select((item, index) => new ChangeActionProposal(
                        ChangeActionType.MoveFile,
                        item.Source,
                        item.Destination,
                        ChangeSuggestionSource.ExistingFolderStructureSuggestion,
                        plan.Summary,
                        index + 1)).ToArray()),
                cancellationToken).ConfigureAwait(false);
            changePlan = changePlan with
            {
                Actions = Array.AsReadOnly(changePlan.Actions
                    .Select(action => action with { ApprovalState = ChangeApprovalState.Approved })
                    .ToArray()),
                Status = ChangePlanStatus.Approved,
            };
            changePlan = (await _changePlanValidator.ValidateAsync(
                changePlan,
                ChangePlanValidationPhase.Review,
                cancellationToken).ConfigureAwait(false)).Plan;
            var execution = await _executionService.ExecuteAsync(
                changePlan,
                "Existing folder-structure suggestion",
                null,
                cancellationToken).ConfigureAwait(false);
            foreach (var action in execution.Operation.Actions.Where(action =>
                         action.ActionType == ChangeActionType.MoveFile))
            {
                var sourceRelative = Path.GetRelativePath(
                    plan.RootPath,
                    action.OriginalPath ?? plan.RootPath);
                var destinationRelative = Path.GetRelativePath(
                    plan.RootPath,
                    action.IntendedDestinationPath);
                outcomes.Add(new RestructuringItemOutcome(
                    sourceRelative,
                    destinationRelative,
                    action.ExecutionResult switch
                    {
                        JournalActionResult.Succeeded => RestructuringItemStatus.Succeeded,
                        JournalActionResult.RolledBack => RestructuringItemStatus.RolledBack,
                        JournalActionResult.RollbackFailed => RestructuringItemStatus.RollbackFailed,
                        JournalActionResult.Skipped => RestructuringItemStatus.Skipped,
                        _ => RestructuringItemStatus.Failed,
                    },
                    action.ErrorDetails ?? execution.Summary));
            }

            var restructuringStatus = execution.Operation.Status switch
            {
                OperationStatus.Succeeded => RestructuringStatus.Applied,
                OperationStatus.Cancelled => RestructuringStatus.Cancelled,
                OperationStatus.RollbackPartiallyFailed or OperationStatus.PartiallySucceeded =>
                    RestructuringStatus.PartiallyApplied,
                _ => RestructuringStatus.Failed,
            };
            if (restructuringStatus == RestructuringStatus.Applied)
            {
                appliedSnapshot = await _snapshotService.CaptureAsync(
                    plan.RootPath,
                    CancellationToken.None).ConfigureAwait(false);
            }

            return await RecordTerminalAsync(
                plan,
                approval,
                restructuringStatus,
                appliedSnapshot,
                outcomes,
                execution.Summary,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await RecordTerminalAsync(
                plan,
                approval,
                RestructuringStatus.Cancelled,
                null,
                outcomes,
                "Apply was cancelled at a safe action boundary. Review the shared Operation Journal for rollback details.",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException)
        {
            return await RecordTerminalAsync(
                plan,
                approval,
                RestructuringStatus.Failed,
                null,
                outcomes,
                $"Apply stopped safely before an unjournalled mutation: {SafeMessage(exception)}",
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<RestructuringApplyResult> RecordTerminalAsync(
        RestructuringPlan plan,
        RestructuringApprovalState approval,
        RestructuringStatus status,
        FolderStructureSnapshot? appliedSnapshot,
        IReadOnlyList<RestructuringItemOutcome> outcomes,
        string message,
        CancellationToken cancellationToken)
    {
        var record = ToHistoryRecord(
            plan,
            approval,
            status,
            appliedSnapshot,
            outcomes,
            message,
            DateTimeOffset.UtcNow);
        await _historyStore.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return new RestructuringApplyResult(status == RestructuringStatus.Applied, status, record, message);
    }

    private static RestructuringProtectionState EvaluateProtection(
        FolderStructureSnapshot current,
        RestructuringHistoryRecord? latestForRoot,
        RestructuringHistoryRecord? latestApplied)
    {
        if (latestApplied?.AppliedSnapshot is null)
        {
            return latestForRoot is null
                ? RestructuringProtectionState.FirstRun
                : RestructuringProtectionState.PreviousIncomplete;
        }

        var applied = latestApplied.AppliedSnapshot;
        if (string.Equals(
                current.StructureFingerprint,
                applied.StructureFingerprint,
                StringComparison.Ordinal))
        {
            return RestructuringProtectionState.AlreadyOrganized;
        }

        var currentFiles = current.Nodes
            .Where(node => !node.IsDirectory)
            .ToDictionary(node => node.RelativePath, FolderStructureSnapshotService.PathComparer);
        var appliedFiles = applied.Nodes.Where(node => !node.IsDirectory).ToArray();
        var allAppliedFilesUnchanged = appliedFiles.All(node =>
            currentFiles.TryGetValue(node.RelativePath, out var candidate) &&
            string.Equals(candidate.IdentityFingerprint, node.IdentityFingerprint, StringComparison.Ordinal));
        if (!allAppliedFilesUnchanged)
        {
            return RestructuringProtectionState.MateriallyChanged;
        }

        return currentFiles.Count > appliedFiles.Length
            ? RestructuringProtectionState.NewFilesOnly
            : RestructuringProtectionState.MateriallyChanged;
    }

    private static IReadOnlyList<StructureNode> SelectCandidates(
        FolderStructureSnapshot source,
        FolderStructureSnapshot? latestApplied,
        bool incremental)
    {
        IEnumerable<StructureNode> candidates = source.Nodes.Where(node =>
            !node.IsDirectory &&
            string.IsNullOrEmpty(Path.GetDirectoryName(node.RelativePath)));
        if (incremental && latestApplied is not null)
        {
            var known = latestApplied.Nodes
                .Where(node => !node.IsDirectory)
                .Select(node => $"{node.RelativePath}|{node.IdentityFingerprint}")
                .ToHashSet(FolderStructureSnapshotService.PathComparer);
            candidates = candidates.Where(node =>
                !known.Contains($"{node.RelativePath}|{node.IdentityFingerprint}"));
        }

        return Array.AsReadOnly(candidates
            .OrderBy(node => node.RelativePath, FolderStructureSnapshotService.PathComparer)
            .Take(StructureLimits.MaximumMovesPerOperation)
            .ToArray());
    }

    private static IReadOnlyList<RestructuringMove> BuildMoves(
        FolderStructureSnapshot source,
        IReadOnlyList<StructureNode> candidates)
    {
        var occupied = source.Nodes
            .Select(node => node.RelativePath)
            .ToHashSet(FolderStructureSnapshotService.PathComparer);
        var destinations = new HashSet<string>(FolderStructureSnapshotService.PathComparer);
        var moves = new List<RestructuringMove>();
        foreach (var node in candidates)
        {
            var category = CategoryForExtension(Path.GetExtension(node.RelativePath));
            var destination = Path.Combine(category, Path.GetFileName(node.RelativePath));
            if (occupied.Contains(destination) || !destinations.Add(destination))
            {
                continue;
            }

            moves.Add(new RestructuringMove(node.RelativePath, destination));
        }

        return Array.AsReadOnly(moves.ToArray());
    }

    private static FolderStructureSnapshot BuildProposedSnapshot(
        FolderStructureSnapshot source,
        IReadOnlyList<RestructuringMove> moves)
    {
        var destinations = moves.ToDictionary(
            move => move.SourceRelativePath,
            move => move.DestinationRelativePath,
            FolderStructureSnapshotService.PathComparer);
        var nodes = source.Nodes
            .Select(node =>
                !node.IsDirectory && destinations.TryGetValue(node.RelativePath, out var destination)
                    ? node with { RelativePath = destination }
                    : node)
            .ToList();
        foreach (var directory in moves
                     .Select(move => Path.GetDirectoryName(move.DestinationRelativePath))
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(FolderStructureSnapshotService.PathComparer))
        {
            if (nodes.Any(node =>
                    node.IsDirectory &&
                    FolderStructureSnapshotService.PathComparer.Equals(node.RelativePath, directory)))
            {
                continue;
            }

            nodes.Add(new StructureNode(
                directory!,
                true,
                0,
                source.CapturedAtUtc,
                FolderStructureSnapshotService.Fingerprint(
                    $"directory:{FolderStructureSnapshotService.NormalizeForIdentity(directory!)}")));
        }

        return StructureSnapshotFactory.Create(source.RootPath, nodes, DateTimeOffset.UtcNow);
    }

    private static void ValidatePlan(RestructuringPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.OperationId) ||
            string.IsNullOrWhiteSpace(plan.RootPath) ||
            !Path.IsPathRooted(plan.RootPath) ||
            plan.SourceSnapshot is null ||
            plan.ProposedSnapshot is null ||
            plan.Moves is null ||
            plan.Moves.Count is < 1 or > StructureLimits.MaximumMovesPerOperation ||
            !string.Equals(plan.AlgorithmVersion, AlgorithmVersion, StringComparison.Ordinal) ||
            !FolderStructureSnapshotService.PathComparer.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.RootPath)),
                plan.SourceSnapshot.RootPath))
        {
            throw new InvalidDataException("The restructuring preview is invalid or unsupported.");
        }
    }

    private static IReadOnlyList<(string Source, string Destination, RestructuringMove Move)> ResolveMoves(
        RestructuringPlan plan)
    {
        var sources = new HashSet<string>(FolderStructureSnapshotService.PathComparer);
        var destinations = new HashSet<string>(FolderStructureSnapshotService.PathComparer);
        var knownSourceFiles = plan.SourceSnapshot.Nodes
            .Where(node => !node.IsDirectory)
            .Select(node => node.RelativePath)
            .ToHashSet(FolderStructureSnapshotService.PathComparer);
        var resolved = new List<(string Source, string Destination, RestructuringMove Move)>();
        foreach (var move in plan.Moves)
        {
            if (move is null ||
                !JsonStructureHistoryStore.IsSafeRelativePath(move.SourceRelativePath) ||
                !JsonStructureHistoryStore.IsSafeRelativePath(move.DestinationRelativePath) ||
                !knownSourceFiles.Contains(move.SourceRelativePath))
            {
                throw new InvalidDataException("A proposed move references an unsafe or unknown source.");
            }

            var source = ResolveWithinRoot(plan.RootPath, move.SourceRelativePath);
            var destination = ResolveWithinRoot(plan.RootPath, move.DestinationRelativePath);
            if (!sources.Add(source) ||
                !destinations.Add(destination) ||
                !File.Exists(source) ||
                File.Exists(destination) ||
                Directory.Exists(destination) ||
                ContainsReparsePoint(plan.RootPath, Path.GetDirectoryName(destination)!))
            {
                throw new InvalidDataException("A proposed move conflicts with current filesystem state.");
            }

            resolved.Add((source, destination, move));
        }

        return Array.AsReadOnly(resolved.ToArray());
    }

    private static string ResolveWithinRoot(string rootPath, string relativePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = $"{root}{Path.DirectorySeparatorChar}";
        if (!resolved.StartsWith(prefix, PathComparison))
        {
            throw new InvalidDataException("A proposed path escapes the selected root.");
        }

        return resolved;
    }

    private static bool ContainsReparsePoint(string rootPath, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        while (!FolderStructureSnapshotService.PathComparer.Equals(current, root))
        {
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidDataException("A destination parent escapes the selected root.");
        }

        return false;
    }

    private static RestructuringHistoryRecord ToHistoryRecord(
        RestructuringPlan plan,
        RestructuringApprovalState approval,
        RestructuringStatus status,
        FolderStructureSnapshot? appliedSnapshot,
        IReadOnlyList<RestructuringItemOutcome> outcomes,
        string summary,
        DateTimeOffset? completedAtUtc)
    {
        var safeMoves = plan.Moves
            .Where(move =>
                move is not null &&
                JsonStructureHistoryStore.IsSafeRelativePath(move.SourceRelativePath) &&
                JsonStructureHistoryStore.IsSafeRelativePath(move.DestinationRelativePath))
            .Take(StructureLimits.MaximumMovesPerOperation)
            .ToArray();
        var safeOutcomes = outcomes
            .Where(outcome =>
                outcome is not null &&
                JsonStructureHistoryStore.IsSafeRelativePath(outcome.SourceRelativePath) &&
                JsonStructureHistoryStore.IsSafeRelativePath(outcome.DestinationRelativePath))
            .Take(StructureLimits.MaximumMovesPerOperation)
            .ToArray();
        return new RestructuringHistoryRecord(
            plan.OperationId,
            plan.SourceSnapshot.RootIdentity,
            plan.RootPath,
            plan.SourceSnapshot.StructureFingerprint,
            plan.SourceSnapshot.CapturedAtUtc,
            completedAtUtc?.ToUniversalTime(),
            plan.SourceSnapshot,
            plan.ProposedSnapshot,
            appliedSnapshot,
            approval,
            status,
            Array.AsReadOnly(safeMoves),
            Array.AsReadOnly(safeOutcomes),
            summary.Length <= StructureLimits.MaximumMessageLength
                ? summary
                : summary[..StructureLimits.MaximumMessageLength],
            plan.PreviousRecordId,
            plan.AlgorithmVersion,
            plan.IsExplicitOverride);
    }

    private static string CategoryForExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".pdf" or ".doc" or ".docx" or ".odt" or ".rtf" or ".txt" or ".md" => "Documents",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".webp" => "Images",
            ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "Audio",
            ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" => "Video",
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "Archives",
            ".csv" or ".tsv" or ".json" or ".xml" or ".xlsx" or ".xls" => "Data",
            _ => "Other",
        };

    private static string SafeMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Access was denied.",
        IOException => "A source or destination became unavailable or conflicted.",
        _ => exception.Message.Length <= 256 ? exception.Message : exception.Message[..256],
    };

    private static StringComparison PathComparison =>
        OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparison;
}
