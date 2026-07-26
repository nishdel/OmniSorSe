using OpenSorSe.Application.Models;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application;

/// <summary>
/// Creates immutable, UI-safe projections of completed processing sessions without accessing the filesystem.
/// </summary>
public sealed class ResultsSnapshotProjector : IResultsSnapshotProjector
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes the projector with a UTC time source for deterministic tests.
    /// </summary>
    /// <param name="timeProvider">The source for the projection timestamp.</param>
    public ResultsSnapshotProjector(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ResultsSnapshot Project(ProcessingSessionResult sessionResult)
    {
        ArgumentNullException.ThrowIfNull(sessionResult);
        ValidateCompletedSession(sessionResult);

        var processing = sessionResult.Processing!;
        var sourceFiles = processing.Duplicates?.Files ??
                          processing.Classification?.Files ??
                          processing.Hashing?.Files ??
                          processing.Metadata?.Files ??
                          processing.Scan.Files;
        ValidateCollection(sourceFiles, "The completed result contains an invalid file collection.", nameof(sessionResult));
        ValidateCollection(processing.Scan.Directories, "The completed result contains an invalid directory collection.", nameof(sessionResult));

        var issues = new List<ResultIssue>();
        var filesByPath = new Dictionary<string, ResultFile>(StringComparer.Ordinal);
        var files = new List<ResultFile>(sourceFiles.Count);
        for (var index = 0; index < sourceFiles.Count; index++)
        {
            var sourceFile = sourceFiles[index] ?? throw new ArgumentException("The completed result contains a null file.", nameof(sessionResult));
            var resultFile = CreateResultFile(sourceFile, index, hasPlannedOperation: false);
            files.Add(resultFile);
            if (!filesByPath.TryAdd(sourceFile.FullPath, resultFile))
            {
                AddIssue(issues, "Results", ResultIssueSeverity.Warning, "Multiple scan results used the same path; some related review details may be unavailable.");
            }
        }

        var operations = MapOperations(
            processing.Conflicts,
            filesByPath,
            issues,
            processing.Workflow?.Analysis.RuleEvaluationEnabled is not false);
        var plannedPaths = operations
            .Where(operation => operation.SourceFileId is not null)
            .Select(operation => operation.SourceFileId!)
            .ToHashSet(StringComparer.Ordinal);
        files = files.Select(file => file with { HasPlannedOperation = plannedPaths.Contains(file.Id) }).ToList();
        var filesById = files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        filesByPath = files
            .GroupBy(file => file.FullPath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var directories = processing.Scan.Directories
            .Select(directory => new ResultDirectory(directory.FullPath, GetDisplayName(directory.FullPath)))
            .ToList();

        AddSourceIssues(processing, filesByPath, issues);
        var duplicateDataAvailable = processing.Duplicates is not null;
        var groups = duplicateDataAvailable
            ? MapDuplicateGroups(processing.Duplicates!, filesByPath, filesById, issues)
            : processing.Workflow?.Analysis.DuplicateAnalysisEnabled == false
                ? []
                : AddMissingDuplicateDataIssue(issues);

        var immutableIssues = ToReadOnly(issues);
        var statistics = new ResultsSnapshotStatistics(
            files.Count,
            directories.Count,
            groups.Count,
            groups.Sum(group => (long)group.MemberCount),
            operations.Count,
            immutableIssues.LongCount(issue => issue.Severity == ResultIssueSeverity.Warning),
            immutableIssues.LongCount(issue => issue.Severity == ResultIssueSeverity.Error));

        return new ResultsSnapshot(
            sessionResult.Session.Id,
            sessionResult.Session.StartedAtUtc,
            _timeProvider.GetUtcNow(),
            ToReadOnly(files),
            ToReadOnly(directories),
            ToReadOnly(groups),
            ToReadOnly(operations),
            immutableIssues,
            statistics,
            duplicateDataAvailable)
        {
            Workflow = processing.Workflow,
        };
    }

    private static void ValidateCompletedSession(ProcessingSessionResult sessionResult)
    {
        if (sessionResult.Session is null || sessionResult.Session.Status != ProcessingSessionStatus.Completed)
        {
            throw new ArgumentException("Results can be projected only from a completed processing session.", nameof(sessionResult));
        }

        if (sessionResult.Processing is null || sessionResult.Processing.Status != ProcessingStatus.Completed)
        {
            throw new ArgumentException("Results can be projected only from completed processing output.", nameof(sessionResult));
        }

        if (sessionResult.Processing.Scan is null)
        {
            throw new ArgumentException("The completed processing result must include scan output.", nameof(sessionResult));
        }
    }

    private static void ValidateCollection<T>(IReadOnlyList<T>? items, string message, string parameterName)
    {
        if (items is null)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private static ResultFile CreateResultFile(FileEntry sourceFile, int index, bool hasPlannedOperation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile.FullPath);
        var metadata = sourceFile.Metadata;
        var extension = NormalizeExtension(metadata?.Extension, sourceFile.FullPath);
        var classification = sourceFile.Classification?.Category;
        return new ResultFile(
            $"file:{index:D8}",
            sourceFile.FullPath,
            GetDisplayName(sourceFile.FullPath),
            extension,
            metadata?.SizeInBytes is >= 0 ? metadata.SizeInBytes : null,
            metadata?.LastWriteTimeUtc,
            classification,
            GetClassificationDisplay(classification),
            sourceFile.Duplicate?.Status ?? DuplicateStatus.Unknown,
            sourceFile.Duplicate?.Status == DuplicateStatus.Duplicate ? sourceFile.Duplicate.GroupId : null,
            hasPlannedOperation)
        {
            CreationTimeUtc = metadata?.CreationTimeUtc,
        };
    }

    private static List<ResultPlannedOperation> MapOperations(
        ConflictResolutionResult? conflicts,
        IReadOnlyDictionary<string, ResultFile> filesByPath,
        List<ResultIssue> issues,
        bool expected)
    {
        if (conflicts is null)
        {
            if (expected)
            {
                AddIssue(issues, "Planned operations", ResultIssueSeverity.Warning, "Planned-operation review was unavailable for this completed scan.");
            }

            return [];
        }

        ValidateCollection(conflicts.Operations, "The completed result contains an invalid planned-operation collection.", nameof(conflicts));
        var operations = new List<ResultPlannedOperation>(conflicts.Operations.Count);
        foreach (var operation in conflicts.Operations)
        {
            if (operation is null)
            {
                throw new ArgumentException("The completed result contains a null planned operation.", nameof(conflicts));
            }

            var sourceFileId = filesByPath.TryGetValue(operation.SourcePath, out var file) ? file.Id : null;
            if (sourceFileId is null)
            {
                AddIssue(issues, "Planned operations", ResultIssueSeverity.Warning, "A planned operation could not be linked to a scanned file.");
            }

            operations.Add(new ResultPlannedOperation(
                operation.OperationId,
                operation.Kind,
                sourceFileId,
                operation.DestinationPath,
                operation.SelectedRuleName));
        }

        return operations;
    }

    private static List<ResultDuplicateGroup> MapDuplicateGroups(
        DuplicateDetectionResult duplicates,
        IReadOnlyDictionary<string, ResultFile> filesByPath,
        IReadOnlyDictionary<string, ResultFile> filesById,
        List<ResultIssue> issues)
    {
        ValidateCollection(duplicates.Groups, "The completed result contains an invalid duplicate-group collection.", nameof(duplicates));
        var groups = new List<ResultDuplicateGroup>(duplicates.Groups.Count);
        for (var index = 0; index < duplicates.Groups.Count; index++)
        {
            var group = duplicates.Groups[index];
            if (group is null)
            {
                throw new ArgumentException("The completed result contains a null duplicate group.", nameof(duplicates));
            }

            var memberIds = new List<string>(group.Files.Count);
            var isComplete = true;
            foreach (var sourceFile in group.Files)
            {
                if (sourceFile is null || !filesByPath.TryGetValue(sourceFile.FullPath, out var resultFile))
                {
                    isComplete = false;
                    break;
                }

                memberIds.Add(resultFile.Id);
            }

            if (!isComplete || memberIds.Count != group.Files.Count || memberIds.Count < 2)
            {
                AddIssue(issues, "Exact duplicates", ResultIssueSeverity.Warning, "An exact duplicate group could not be shown because its scanned members were incomplete.");
                continue;
            }

            var sizes = memberIds.Select(memberId => filesById[memberId].SizeInBytes).ToArray();
            var commonSize = sizes.All(size => size is >= 0) && sizes.Distinct().Count() == 1
                ? sizes[0]
                : null;
            var reclaimable = commonSize is not null
                ? CalculateReclaimable(memberIds.Count, commonSize.Value)
                : null;
            groups.Add(new ResultDuplicateGroup(
                group.GroupId,
                index + 1,
                ToReadOnly(memberIds),
                memberIds.Count,
                commonSize,
                reclaimable));
        }

        return groups;
    }

    private static List<ResultDuplicateGroup> AddMissingDuplicateDataIssue(List<ResultIssue> issues)
    {
        AddIssue(issues, "Exact duplicates", ResultIssueSeverity.Warning, "Exact duplicate review was unavailable for this completed scan.");
        return [];
    }

    private static long? CalculateReclaimable(int memberCount, long commonSize)
    {
        try
        {
            return checked((long)(memberCount - 1) * commonSize);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static void AddSourceIssues(
        ProcessingResult processing,
        IReadOnlyDictionary<string, ResultFile> filesByPath,
        List<ResultIssue> issues)
    {
        AddIssues("Scanning", processing.Scan.Issues, issue => issue.Message, issue => issue.Path, filesByPath, issues);
        AddIssues("Metadata", processing.Metadata?.Issues, issue => issue.Message, issue => issue.FilePath, filesByPath, issues);
        AddIssues("Hashing", processing.Hashing?.Issues, issue => issue.Message, issue => issue.FilePath, filesByPath, issues);
        AddIssues("Classification", processing.Classification?.Issues, issue => issue.Message, issue => issue.FilePath, filesByPath, issues);
        AddIssues("Exact duplicates", processing.Duplicates?.Issues, issue => issue.Message, issue => issue.FilePath, filesByPath, issues);
        AddIssues("Planned operations", processing.Plan?.Issues, issue => issue.Message, issue => issue.FilePath, filesByPath, issues);
        AddIssues("Conflict resolution", processing.Conflicts?.Issues, issue => issue.Message, _ => null, filesByPath, issues);
    }

    private static void AddIssues<TIssue>(
        string stage,
        IReadOnlyList<TIssue>? sourceIssues,
        Func<TIssue, string> messageSelector,
        Func<TIssue, string?> pathSelector,
        IReadOnlyDictionary<string, ResultFile> filesByPath,
        List<ResultIssue> target)
    {
        if (sourceIssues is null)
        {
            return;
        }

        foreach (var issue in sourceIssues)
        {
            if (issue is null || string.IsNullOrWhiteSpace(messageSelector(issue)))
            {
                continue;
            }

            var path = pathSelector(issue);
            var fileId = path is not null && filesByPath.TryGetValue(path, out var file) ? file.Id : null;
            AddIssue(target, stage, ResultIssueSeverity.Warning, messageSelector(issue), fileId);
        }
    }

    private static void AddIssue(
        List<ResultIssue> target,
        string stage,
        ResultIssueSeverity severity,
        string message,
        string? associatedFileId = null)
    {
        if (target.Any(issue => issue.SourceStage == stage && issue.Message == message && issue.AssociatedFileId == associatedFileId))
        {
            return;
        }

        target.Add(new ResultIssue(stage, severity, message, associatedFileId));
    }

    private static string GetDisplayName(string path)
    {
        var fileName = Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(fileName) && !string.Equals(fileName, path, StringComparison.Ordinal))
        {
            return fileName;
        }

        var separatorIndex = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return separatorIndex >= 0 && separatorIndex < path.Length - 1 ? path[(separatorIndex + 1)..] : path;
    }

    private static string NormalizeExtension(string? metadataExtension, string path)
    {
        var extension = string.IsNullOrWhiteSpace(metadataExtension) ? Path.GetExtension(GetDisplayName(path)) : metadataExtension.Trim();
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }

        return $".{extension.TrimStart('.').ToLowerInvariant()}";
    }

    private static string GetClassificationDisplay(FileCategory? category) => category switch
    {
        null => "Unclassified",
        FileCategory.Unknown => "Unknown",
        _ => category.Value.ToString(),
    };

    private static IReadOnlyList<T> ToReadOnly<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
}
