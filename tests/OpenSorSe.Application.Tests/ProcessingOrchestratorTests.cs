using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Application;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Rules;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies sequential orchestration without duplicating stage responsibilities.</summary>
public sealed class ProcessingOrchestratorTests
{
    /// <summary>Verifies all supported stages execute in documented order and return their outputs.</summary>
    [Fact]
    public async Task ProcessAsync_RunsAllStagesInDocumentedOrder()
    {
        var calls = new List<string>();
        var orchestrator = Create(calls, ScanStatus.Completed);
        var progress = new List<ProcessingProgress>();

        var result = await orchestrator.ProcessAsync(Request(), new InlineProgress<ProcessingProgress>(progress.Add));

        Assert.Equal(["scan", "metadata", "hash", "classify", "duplicates", "rules", "plan", "conflicts"], calls);
        Assert.Equal(ProcessingStatus.Completed, result.Status);
        Assert.NotNull(result.Conflicts);
        Assert.Equal(ProcessingProgressStage.Completed, progress[^1].Stage);
    }

    /// <summary>Verifies a cancelled scanner result prevents every later stage from starting.</summary>
    [Fact]
    public async Task ProcessAsync_CancelledScan_ReturnsPartialResultWithoutLaterStages()
    {
        var calls = new List<string>();
        var orchestrator = Create(calls, ScanStatus.Cancelled);

        var result = await orchestrator.ProcessAsync(Request());

        Assert.Equal(["scan"], calls);
        Assert.Equal(ProcessingStatus.Cancelled, result.Status);
        Assert.Null(result.Metadata);
        Assert.Null(result.Conflicts);
    }

    /// <summary>Verifies cancellation before orchestration prevents any stage from starting.</summary>
    [Fact]
    public async Task ProcessAsync_PreCancelled_ThrowsWithoutStartingStages()
    {
        var calls = new List<string>();
        var orchestrator = Create(calls, ScanStatus.Completed);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => orchestrator.ProcessAsync(Request(), cancellationToken: cancellation.Token));

        Assert.Empty(calls);
    }

    /// <summary>Verifies content extraction is ordered after scanner metadata and a local failure cannot break scanning.</summary>
    [Fact]
    public async Task ProcessAsync_ContentIndexerFails_ContinuesPrimaryPipeline()
    {
        var calls = new List<string>();
        var orchestrator = new ProcessingOrchestrator(
            new Scanner(calls, ScanStatus.Completed),
            new Metadata(calls),
            new Hasher(calls),
            new Classifier(calls),
            new Duplicates(calls),
            new Rules(calls),
            new Planner(calls),
            new Conflicts(calls),
            new Logging(),
            new Errors(),
            new ContentIndexer(calls, shouldFail: true));
        var progress = new List<ProcessingProgress>();

        var result = await orchestrator.ProcessAsync(
            Request(),
            new InlineProgress<ProcessingProgress>(progress.Add));

        Assert.Equal(
            ["scan", "metadata", "content", "hash", "classify", "duplicates", "rules", "plan", "conflicts"],
            calls);
        Assert.Equal(ProcessingStatus.Completed, result.Status);
        Assert.Contains(progress, item => item.Stage == ProcessingProgressStage.ExtractingContent);
    }

    private static ProcessingRequest Request() => new(new ScanRequest(["C:\\Root"], ScanOptions.Default), []);

    private static ProcessingOrchestrator Create(List<string> calls, ScanStatus status) => new(
        new Scanner(calls, status), new Metadata(calls), new Hasher(calls), new Classifier(calls), new Duplicates(calls), new Rules(calls), new Planner(calls), new Conflicts(calls), new Logging(), new Errors());

    private static ScanResult Scan(ScanStatus status) => new([], [], new ScanStatistics(0, 0, 0), [], status, TimeSpan.Zero);
    private static FileMetadataResult MetadataResult() => new([], new FileMetadataStatistics(0, 0, 0), []);
    private static FileHashResult HashResult() => new([], new FileHashStatistics(0, 0, 0, 0), []);
    private static FileClassificationResult ClassificationResult() => new([], new FileClassificationStatistics(0, 0, 0, 0), []);
    private static DuplicateDetectionResult DuplicateResult() => new([], [], new DuplicateDetectionStatistics(0, 0, 0, 0, 0, 0), []);
    private static RuleEvaluationResult RuleResult() => new([], new RuleEvaluationStatistics(0, 0, 0, 0, 0));
    private static ActionPlanResult PlanResult() => new([], new ActionPlanningStatistics(0, 0, 0, 0, 0, 0, 0, 0, 0), []);
    private static ConflictResolutionResult ConflictResult() => new([], new ConflictResolutionStatistics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), []);

    private sealed class Scanner(List<string> calls, ScanStatus status) : IFileScanner
    {
        public Task<ScanResult> ScanAsync(ScanRequest request, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) { calls.Add("scan"); return Task.FromResult(Scan(status)); }
    }
    private sealed class Metadata(List<string> calls) : IFileMetadataReader
    {
        public Task<FileMetadataResult> ReadAsync(IReadOnlyCollection<FileEntry> files, CancellationToken cancellationToken = default) { calls.Add("metadata"); return Task.FromResult(MetadataResult()); }
    }
    private sealed class Hasher(List<string> calls) : IFileHasher
    {
        public Task<FileHashResult> HashAsync(IReadOnlyCollection<FileEntry> files, CancellationToken cancellationToken = default) { calls.Add("hash"); return Task.FromResult(HashResult()); }
    }
    private sealed class Classifier(List<string> calls) : IFileClassifier
    {
        public Task<FileClassificationResult> ClassifyAsync(IReadOnlyCollection<FileEntry> files, FileClassificationOptions? options = null, CancellationToken cancellationToken = default) { calls.Add("classify"); return Task.FromResult(ClassificationResult()); }
    }
    private sealed class Duplicates(List<string> calls) : IDuplicateDetector
    {
        public Task<DuplicateDetectionResult> DetectAsync(IReadOnlyCollection<FileEntry> files, CancellationToken cancellationToken = default) { calls.Add("duplicates"); return Task.FromResult(DuplicateResult()); }
    }
    private sealed class Rules(List<string> calls) : IRuleEngine
    {
        public Task<RuleEvaluationResult> EvaluateAsync(IReadOnlyCollection<FileEntry> files, IReadOnlyList<FileRule> rules, CancellationToken cancellationToken = default) { calls.Add("rules"); return Task.FromResult(RuleResult()); }
    }
    private sealed class Planner(List<string> calls) : IActionPlanner
    {
        public Task<ActionPlanResult> PlanAsync(IReadOnlyCollection<RuleDecision> decisions, CancellationToken cancellationToken = default) { calls.Add("plan"); return Task.FromResult(PlanResult()); }
    }
    private sealed class Conflicts(List<string> calls) : IConflictResolver
    {
        public Task<ConflictResolutionResult> ResolveAsync(IReadOnlyCollection<PlannedOperation> operations, ConflictResolutionOptions? options = null, CancellationToken cancellationToken = default) { calls.Add("conflicts"); return Task.FromResult(ConflictResult()); }
    }
    private sealed class ContentIndexer(List<string> calls, bool shouldFail) : IContentIndexingService
    {
        public Task<ContentIndexingSummary> IndexAsync(
            IReadOnlyCollection<FileEntry> files,
            CancellationToken cancellationToken)
        {
            calls.Add("content");
            return shouldFail
                ? Task.FromException<ContentIndexingSummary>(new InvalidOperationException("Controlled test failure"))
                : Task.FromResult(new ContentIndexingSummary(files.Count, 0, 0, 0, 0, 0));
        }
    }
    private sealed class Logging : ILoggingService
    {
        public void Initialize(LogLevel minimumLevel) { }
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void Dispose() { }
    }
    private sealed class Errors : IErrorHandler
    {
        public event EventHandler<ApplicationError>? ErrorReported;
        public void Report(ApplicationError applicationError) => ErrorReported?.Invoke(this, applicationError);
    }
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
