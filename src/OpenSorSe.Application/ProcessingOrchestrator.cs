using Microsoft.Extensions.Logging;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Rules;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application;

/// <summary>Runs v0.1 pipeline stages sequentially while leaving domain behavior to each stage service.</summary>
public sealed class ProcessingOrchestrator : IProcessingOrchestrator
{
    private const string LoggerCategory = "ProcessingOrchestrator";
    private readonly IFileClassifier _classifier;
    private readonly IConflictResolver _conflictResolver;
    private readonly IDuplicateDetector _duplicateDetector;
    private readonly IErrorHandler _errorHandler;
    private readonly IFileHasher _hasher;
    private readonly ILogger _logger;
    private readonly IFileMetadataReader _metadataReader;
    private readonly IActionPlanner _planner;
    private readonly IRuleEngine _ruleEngine;
    private readonly IFileScanner _scanner;
    private readonly IContentIndexingService? _contentIndexingService;

    /// <summary>Initializes all stage dependencies required by the documented pipeline.</summary>
    /// <param name="scanner">The file discovery stage.</param>
    /// <param name="metadataReader">The metadata enrichment stage.</param>
    /// <param name="hasher">The hash enrichment stage.</param>
    /// <param name="classifier">The metadata classification stage.</param>
    /// <param name="duplicateDetector">The exact-hash duplicate detection stage.</param>
    /// <param name="ruleEngine">The pure rule evaluation stage.</param>
    /// <param name="planner">The pure action-planning stage.</param>
    /// <param name="conflictResolver">The lexical conflict-resolution stage.</param>
    /// <param name="loggingService">The centralized diagnostic logging service.</param>
    /// <param name="errorHandler">The handler for unexpected operation-level failures.</param>
    /// <param name="contentIndexingService">The optional failure-isolated local content extraction stage.</param>
    public ProcessingOrchestrator(IFileScanner scanner, IFileMetadataReader metadataReader, IFileHasher hasher, IFileClassifier classifier, IDuplicateDetector duplicateDetector, IRuleEngine ruleEngine, IActionPlanner planner, IConflictResolver conflictResolver, ILoggingService loggingService, IErrorHandler errorHandler, IContentIndexingService? contentIndexingService = null)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _duplicateDetector = duplicateDetector ?? throw new ArgumentNullException(nameof(duplicateDetector));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _conflictResolver = conflictResolver ?? throw new ArgumentNullException(nameof(conflictResolver));
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService))).CreateLogger(LoggerCategory);
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _contentIndexingService = contentIndexingService;
    }

    /// <inheritdoc />
    public async Task<ProcessingResult> ProcessAsync(ProcessingRequest request, IProgress<ProcessingProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ScanRequest);
        ArgumentNullException.ThrowIfNull(request.Rules);
        if (request.Rules.Any(rule => rule is null))
        {
            throw new ArgumentException("The rule collection cannot contain null entries.", nameof(request));
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ProcessingProgress(ProcessingProgressStage.Scanning));
            var scanProgress = new InlineScanProgress(progress);
            var scan = await _scanner.ScanAsync(request.ScanRequest, scanProgress, cancellationToken).ConfigureAwait(false);
            if (scan.Status == ScanStatus.Cancelled || cancellationToken.IsCancellationRequested)
            {
                progress?.Report(new ProcessingProgress(ProcessingProgressStage.Cancelled));
                return new ProcessingResult(ProcessingStatus.Cancelled, scan, null, null, null, null, null, null, null)
                {
                    Workflow = request.WorkflowConfiguration?.Snapshot,
                };
            }

            var workflow = request.WorkflowConfiguration;
            if (workflow is not null)
            {
                scan = ApplyWorkflowFileSelection(scan, workflow.Files);
            }

            IReadOnlyList<FileEntry> currentFiles = scan.Files;
            FileMetadataResult? metadata = null;
            if (workflow?.Extraction.MetadataEnabled is not false)
            {
                progress?.Report(new ProcessingProgress(ProcessingProgressStage.ReadingMetadata));
                metadata = await _metadataReader.ReadAsync(currentFiles, cancellationToken).ConfigureAwait(false);
                currentFiles = metadata.Files;
            }

            if (_contentIndexingService is not null &&
                (workflow is null || workflow.Extraction.TextEnabled || workflow.Extraction.OcrEnabled))
            {
                progress?.Report(new ProcessingProgress(ProcessingProgressStage.ExtractingContent));
                try
                {
                    await _contentIndexingService.IndexAsync(
                        currentFiles,
                        workflow is null
                            ? null
                            : new ContentIndexingOptions(
                                workflow.Extraction.MetadataEnabled,
                                workflow.Extraction.TextEnabled,
                                workflow.Extraction.OcrEnabled,
                                workflow.Extraction.OcrOnlyWhenTextUnavailable,
                                workflow.Extraction.OcrLanguage,
                                workflow.Extraction.MaximumPagesPerDocument,
                                workflow.Files.MaximumFileSizeBytes),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Local content extraction was unavailable; the primary scan pipeline will continue.");
                }
            }

            FileHashResult? hashing = null;
            if (workflow?.Analysis.DuplicateAnalysisEnabled is not false)
            {
                progress?.Report(new ProcessingProgress(ProcessingProgressStage.Hashing));
                hashing = await _hasher.HashAsync(currentFiles, cancellationToken).ConfigureAwait(false);
                currentFiles = hashing.Files;
            }

            FileClassificationResult? classification = null;
            if (workflow?.Analysis.ClassificationEnabled is not false)
            {
                progress?.Report(new ProcessingProgress(ProcessingProgressStage.Classifying));
                classification = await _classifier.ClassifyAsync(
                    currentFiles,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                currentFiles = classification.Files;
            }

            DuplicateDetectionResult? duplicates = null;
            if (workflow?.Analysis.DuplicateAnalysisEnabled is not false)
            {
                progress?.Report(new ProcessingProgress(ProcessingProgressStage.DetectingDuplicates));
                duplicates = await _duplicateDetector.DetectAsync(currentFiles, cancellationToken).ConfigureAwait(false);
                currentFiles = duplicates.Files;
            }

            RuleEvaluationResult? rules = null;
            ActionPlanResult? plan = null;
            ConflictResolutionResult? conflicts = null;
            if (workflow?.Analysis.RuleEvaluationEnabled is not false)
            {
                progress?.Report(new ProcessingProgress(ProcessingProgressStage.EvaluatingRules));
                rules = await _ruleEngine.EvaluateAsync(currentFiles, request.Rules, cancellationToken).ConfigureAwait(false);
                progress?.Report(new ProcessingProgress(ProcessingProgressStage.PlanningActions));
                plan = await _planner.PlanAsync(rules.Decisions, cancellationToken).ConfigureAwait(false);
                progress?.Report(new ProcessingProgress(ProcessingProgressStage.ResolvingConflicts));
                conflicts = await _conflictResolver.ResolveAsync(
                    plan.Operations,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new ProcessingProgress(ProcessingProgressStage.Completed));
            return new ProcessingResult(
                ProcessingStatus.Completed,
                scan,
                metadata,
                hashing,
                classification,
                duplicates,
                rules,
                plan,
                conflicts)
            {
                Workflow = workflow?.Snapshot,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Processing pipeline stopped because an unexpected stage failure occurred.");
            _errorHandler.Report(new ApplicationError(LoggerCategory, "The processing pipeline could not be completed.", ApplicationErrorSeverity.Error, exception));
            throw;
        }
    }

    private static ScanResult ApplyWorkflowFileSelection(
        ScanResult scan,
        WorkflowFileSelectionOptions options)
    {
        var files = scan.Files
            .Where(file => MatchesWorkflowFileSelection(file, options))
            .ToArray();
        if (files.Length == scan.Files.Count)
        {
            return scan;
        }

        return scan with
        {
            Files = Array.AsReadOnly(files),
            Statistics = scan.Statistics with { FilesDiscovered = files.Length },
        };
    }

    private static bool MatchesWorkflowFileSelection(
        FileEntry file,
        WorkflowFileSelectionOptions options)
    {
        var extension = Path.GetExtension(file.FullPath);
        if ((options.IncludedFileTypes.Count > 0 &&
             !options.IncludedFileTypes.Contains(extension, StringComparer.OrdinalIgnoreCase)) ||
            options.ExcludedFileTypes.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var attributes = file.Metadata?.Attributes ?? File.GetAttributes(file.FullPath);
            var size = file.Metadata?.SizeInBytes is >= 0
                ? file.Metadata.SizeInBytes.Value
                : new FileInfo(file.FullPath).Length;
            return size <= options.MaximumFileSizeBytes &&
                   (options.IncludeHiddenFiles ||
                    !attributes.HasFlag(FileAttributes.Hidden));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                IOException or
                NotSupportedException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            return false;
        }
    }

    private sealed class InlineScanProgress(IProgress<ProcessingProgress>? progress) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => progress?.Report(new ProcessingProgress(ProcessingProgressStage.Scanning, value));
    }
}
