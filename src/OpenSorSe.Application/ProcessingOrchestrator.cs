using Microsoft.Extensions.Logging;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Rules;
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
                return new ProcessingResult(ProcessingStatus.Cancelled, scan, null, null, null, null, null, null, null);
            }

            progress?.Report(new ProcessingProgress(ProcessingProgressStage.ReadingMetadata));
            var metadata = await _metadataReader.ReadAsync(scan.Files, cancellationToken).ConfigureAwait(false);
            if (_contentIndexingService is not null)
            {
                progress?.Report(new ProcessingProgress(ProcessingProgressStage.ExtractingContent));
                try
                {
                    await _contentIndexingService.IndexAsync(metadata.Files, cancellationToken).ConfigureAwait(false);
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

            progress?.Report(new ProcessingProgress(ProcessingProgressStage.Hashing));
            var hashing = await _hasher.HashAsync(metadata.Files, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ProcessingProgress(ProcessingProgressStage.Classifying));
            var classification = await _classifier.ClassifyAsync(hashing.Files, cancellationToken: cancellationToken).ConfigureAwait(false);
            progress?.Report(new ProcessingProgress(ProcessingProgressStage.DetectingDuplicates));
            var duplicates = await _duplicateDetector.DetectAsync(classification.Files, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ProcessingProgress(ProcessingProgressStage.EvaluatingRules));
            var rules = await _ruleEngine.EvaluateAsync(duplicates.Files, request.Rules, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ProcessingProgress(ProcessingProgressStage.PlanningActions));
            var plan = await _planner.PlanAsync(rules.Decisions, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ProcessingProgress(ProcessingProgressStage.ResolvingConflicts));
            var conflicts = await _conflictResolver.ResolveAsync(plan.Operations, cancellationToken: cancellationToken).ConfigureAwait(false);
            progress?.Report(new ProcessingProgress(ProcessingProgressStage.Completed));
            return new ProcessingResult(ProcessingStatus.Completed, scan, metadata, hashing, classification, duplicates, rules, plan, conflicts);
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

    private sealed class InlineScanProgress(IProgress<ProcessingProgress>? progress) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => progress?.Report(new ProcessingProgress(ProcessingProgressStage.Scanning, value));
    }
}
