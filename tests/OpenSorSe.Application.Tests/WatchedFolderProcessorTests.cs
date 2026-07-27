#pragma warning disable CS1591

using OpenSorSe.Application.Models;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Application.Watching;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Executor.Models;
using OpenSorSe.Rules;
using OpenSorSe.Scanner;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

public sealed class WatchedFolderProcessorTests
{
    [Fact]
    public async Task FullThenTargetedScan_ReprocessesOnlyChangedFileAndPreservesUnchangedAnalysis()
    {
        using var context = CreateContext();
        var firstPath = await context.WriteAsync("first.txt", "one");
        var secondPath = await context.WriteAsync("second.txt", "two");

        var initial = await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        var firstInitial = initial.Catalogue.Files.Single(file => file.FullPath == firstPath);
        var secondInitial = initial.Catalogue.Files.Single(file => file.FullPath == secondPath);
        context.Time.Advance(TimeSpan.FromMinutes(5));
        await File.WriteAllTextAsync(firstPath, "one changed");
        File.SetLastWriteTimeUtc(firstPath, DateTime.UtcNow.AddMinutes(1));

        var incremental = await context.ProcessAsync(HintBatch(
            new WatchedFolderHint(
                context.Configuration.Id,
                WatchedPathChangeKind.FileModified,
                firstPath,
                null,
                DateTimeOffset.UtcNow)));
        var firstUpdated = incremental.Catalogue.Files.Single(file => file.FullPath == firstPath);
        var secondUpdated = incremental.Catalogue.Files.Single(file => file.FullPath == secondPath);

        Assert.Equal(1, incremental.Summary.Updated);
        Assert.Equal(WatchedItemReprocessReason.ContentChanged, firstUpdated.LastReprocessReason);
        Assert.NotEqual(firstInitial.ContentHash, firstUpdated.ContentHash);
        Assert.Equal(secondInitial.ContentHash, secondUpdated.ContentHash);
        Assert.Equal(secondInitial.AnalysedAtUtc, secondUpdated.AnalysedAtUtc);
        Assert.Single(context.Suggestions.AffectedHistory.Last());
        Assert.Equal(firstUpdated.StableId, context.Suggestions.AffectedHistory.Last()[0].Id);
    }

    [Fact]
    public async Task IncrementalProfileBehavior_ReanalysesUnchangedAndCanRetainMissingCatalogueItems()
    {
        var behavior = new WorkflowScanBehavior(
            true,
            ReanalyseChangedContentOnly: false,
            ReconcileMissingItems: false,
            PreserveUnchangedAnalysis: true);
        using var context = CreateContext(configuration => configuration with
        {
            EffectiveWorkflow = EffectiveWorkflow(behavior),
        });
        var keptPath = await context.WriteAsync("kept.txt", "keep");
        var removedPath = await context.WriteAsync("removed.txt", "remove");
        var initial = await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        var keptInitial = initial.Catalogue.Files.Single(file => file.FullPath == keptPath);
        context.Time.Advance(TimeSpan.FromMinutes(5));
        File.Delete(removedPath);

        var reconciled = await context.ProcessAsync(
            FullBatch(WatchedScanReason.UserFullReconciliation));

        Assert.Equal(0, reconciled.Summary.Removed);
        Assert.Equal(2, reconciled.Catalogue.Files.Count);
        Assert.True(
            reconciled.Catalogue.Files.Single(file => file.FullPath == keptPath).AnalysedAtUtc >
            keptInitial.AnalysedAtUtc);
        Assert.Contains(reconciled.Catalogue.Files, file => file.FullPath == removedPath);
    }

    [Fact]
    public async Task TargetedBatch_CreateDuplicateHints_AddsOneVerifiedFile()
    {
        using var context = CreateContext();
        await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        var path = await context.WriteAsync("new.txt", "new");
        var detected = DateTimeOffset.UtcNow;
        var batch = new WatchedChangeBatch(
            "batch:duplicate",
            context.Configuration.Id,
            WatchedScanReason.WatcherBatch,
            detected,
            detected,
            [
                new WatchedFolderHint(context.Configuration.Id, WatchedPathChangeKind.FileCreated, path, null, detected),
                new WatchedFolderHint(context.Configuration.Id, WatchedPathChangeKind.FileModified, path, null, detected),
                new WatchedFolderHint(context.Configuration.Id, WatchedPathChangeKind.FileCreated, path, null, detected),
            ],
            false);

        var result = await context.ProcessAsync(batch);

        Assert.Equal(1, result.Summary.Added);
        Assert.Single(result.Catalogue.Files);
        Assert.Equal(path, result.Catalogue.Files[0].FullPath);
    }

    [Fact]
    public async Task TargetedDelete_RemovesOnlyTheVerifiedMissingCatalogueEntry()
    {
        using var context = CreateContext();
        var removedPath = await context.WriteAsync("removed.txt", "gone");
        var keptPath = await context.WriteAsync("kept.txt", "keep");
        await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        File.Delete(removedPath);

        var result = await context.ProcessAsync(HintBatch(new WatchedFolderHint(
            context.Configuration.Id,
            WatchedPathChangeKind.FileDeleted,
            removedPath,
            null,
            DateTimeOffset.UtcNow)));

        Assert.Equal(1, result.Summary.Removed);
        Assert.Equal(keptPath, Assert.Single(result.Catalogue.Files).FullPath);
    }

    [Fact]
    public async Task OutOfOrderCreateAndDeleteHints_UseCurrentFilesystemTruth()
    {
        using var context = CreateContext();
        await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        var path = await context.WriteAsync("final.txt", "present");
        var detected = DateTimeOffset.UtcNow;

        var result = await context.ProcessAsync(HintBatch(
            new WatchedFolderHint(
                context.Configuration.Id,
                WatchedPathChangeKind.FileCreated,
                path,
                null,
                detected.AddSeconds(1)),
            new WatchedFolderHint(
                context.Configuration.Id,
                WatchedPathChangeKind.FileDeleted,
                path,
                null,
                detected)));

        Assert.Equal(1, result.Summary.Added);
        Assert.Equal(path, Assert.Single(result.Catalogue.Files).FullPath);
    }

    [Fact]
    public async Task Reconciliation_RenameMoveAndDirectoryMove_PreserveStableFileIdentity()
    {
        using var context = CreateContext();
        var originalDirectory = Directory.CreateDirectory(Path.Combine(context.Root, "Original")).FullName;
        var original = Path.Combine(originalDirectory, "report.txt");
        await File.WriteAllTextAsync(original, "report");
        var initial = await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        var stableId = Assert.Single(initial.Catalogue.Files).StableId;
        var movedDirectory = Path.Combine(context.Root, "Moved");
        Directory.Move(originalDirectory, movedDirectory);
        var moved = Path.Combine(movedDirectory, "renamed.txt");
        File.Move(Path.Combine(movedDirectory, "report.txt"), moved);

        var result = await context.ProcessAsync(FullBatch(WatchedScanReason.UserFullReconciliation));

        var file = Assert.Single(result.Catalogue.Files);
        Assert.Equal(stableId, file.StableId);
        Assert.Equal(moved, file.FullPath);
        Assert.Equal(1, result.Summary.RenamedOrMoved);
        Assert.Equal(WatchedItemReprocessReason.RenamedOrMoved, file.LastReprocessReason);
    }

    [Fact]
    public async Task Reconciliation_ExternalDeletion_RemovesOnlyCatalogueEntry()
    {
        using var context = CreateContext();
        var path = await context.WriteAsync("delete.txt", "data");
        await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        File.Delete(path);

        var result = await context.ProcessAsync(FullBatch(WatchedScanReason.UserFullReconciliation));

        Assert.Equal(1, result.Summary.Removed);
        Assert.Empty(result.Catalogue.Files);
        Assert.False(File.Exists(path));
        Assert.True(Directory.Exists(context.Root));
    }

    [Fact]
    public async Task Reconciliation_NoChanges_DoesNotRehashOrChangeAnalysisTimestamp()
    {
        using var context = CreateContext();
        await context.WriteAsync("same.txt", "same");
        var initial = await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        context.Time.Advance(TimeSpan.FromHours(1));

        var second = await context.ProcessAsync(FullBatch(WatchedScanReason.UserFullReconciliation));

        Assert.Equal(0, second.Summary.ChangedCount);
        Assert.Equal(
            Assert.Single(initial.Catalogue.Files).AnalysedAtUtc,
            Assert.Single(second.Catalogue.Files).AnalysedAtUtc);
        Assert.Empty(context.Suggestions.AffectedHistory.Last());
    }

    [Fact]
    public async Task MetadataOnlyChange_PreservesContentDerivedAnalysis()
    {
        using var context = CreateContext();
        var path = await context.WriteAsync("attributes.txt", "unchanged content");
        var initial = await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        var before = Assert.Single(initial.Catalogue.Files);
        context.Time.Advance(TimeSpan.FromMinutes(2));
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

        var result = await context.ProcessAsync(HintBatch(new WatchedFolderHint(
            context.Configuration.Id,
            WatchedPathChangeKind.FileModified,
            path,
            null,
            DateTimeOffset.UtcNow)));

        var after = Assert.Single(result.Catalogue.Files);
        Assert.Equal(1, result.Summary.Updated);
        Assert.Equal(WatchedItemReprocessReason.MetadataChanged, after.LastReprocessReason);
        Assert.Equal(before.ContentHash, after.ContentHash);
        Assert.Equal(before.Category, after.Category);
        Assert.NotEqual(before.AnalysedAtUtc, after.AnalysedAtUtc);
        File.SetAttributes(path, FileAttributes.Normal);
    }

    [Fact]
    public async Task DuplicateGroups_UpdateFromRetainedAndChangedHashes()
    {
        using var context = CreateContext();
        var firstPath = await context.WriteAsync("first.txt", "same");
        await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        var secondPath = await context.WriteAsync("second.txt", "same");
        var withDuplicate = await context.ProcessAsync(HintBatch(new WatchedFolderHint(
            context.Configuration.Id,
            WatchedPathChangeKind.FileCreated,
            secondPath,
            null,
            DateTimeOffset.UtcNow)));

        Assert.All(withDuplicate.Catalogue.Files, file =>
            Assert.Equal(DuplicateStatus.Duplicate, file.DuplicateStatus));
        Assert.Single(withDuplicate.Catalogue.Files.Select(file => file.DuplicateGroupId).Distinct());

        await File.WriteAllTextAsync(secondPath, "different");
        File.SetLastWriteTimeUtc(secondPath, DateTime.UtcNow.AddMinutes(1));
        var updated = await context.ProcessAsync(HintBatch(new WatchedFolderHint(
            context.Configuration.Id,
            WatchedPathChangeKind.FileModified,
            secondPath,
            null,
            DateTimeOffset.UtcNow)));

        Assert.Equal([firstPath, secondPath], updated.Catalogue.Files.Select(file => file.FullPath).Order().ToArray());
        Assert.All(updated.Catalogue.Files, file => Assert.Equal(DuplicateStatus.Unique, file.DuplicateStatus));
    }

    [Fact]
    public async Task TemporaryReplacementWorkflow_UpdatesExistingPathWithoutDuplicateCatalogueEntry()
    {
        using var context = CreateContext();
        var path = await context.WriteAsync("document.txt", "old");
        var initial = await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        var oldStableId = Assert.Single(initial.Catalogue.Files).StableId;
        var temporary = await context.WriteAsync("document.tmp", "new content");
        File.Delete(path);
        File.Move(temporary, path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var result = await context.ProcessAsync(HintBatch(
            new WatchedFolderHint(
                context.Configuration.Id,
                WatchedPathChangeKind.FileCreated,
                path,
                temporary,
                DateTimeOffset.UtcNow)));

        var file = Assert.Single(result.Catalogue.Files);
        Assert.Equal(oldStableId, file.StableId);
        Assert.Equal(1, result.Summary.Updated);
        Assert.Equal(0, result.Summary.Added);
    }

    [Fact]
    public async Task IgnorePolicy_PreventsTemporaryHiddenOversizedAndOutsideFilesFromAnalysis()
    {
        using var context = CreateContext(configuration => configuration with
        {
            IgnorePatterns = ["*.secret"],
            MaximumFileSizeBytes = 4,
        });
        await context.WriteAsync("keep.txt", "1234");
        await context.WriteAsync("skip.tmp", "1");
        await context.WriteAsync("skip.secret", "1");
        await context.WriteAsync("large.txt", "12345");
        var outside = Path.Combine(context.Workspace, "outside.txt");
        await File.WriteAllTextAsync(outside, "x");

        var result = await context.ProcessAsync(new WatchedChangeBatch(
            "batch:outside",
            context.Configuration.Id,
            WatchedScanReason.UserFullReconciliation,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new WatchedFolderHint(context.Configuration.Id, WatchedPathChangeKind.FileCreated, outside, null, DateTimeOffset.UtcNow)],
            true));

        Assert.Single(result.Catalogue.Files);
        Assert.EndsWith("keep.txt", result.Catalogue.Files[0].FullPath, StringComparison.Ordinal);
        Assert.All(context.Suggestions.AffectedHistory.SelectMany(files => files), file =>
            Assert.DoesNotContain("skip", file.FullPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnstableFile_IsDeferredWithInvalidatedDerivedDataAndIncompleteSummary()
    {
        using var context = CreateContext(stability: new AlwaysUnstableStabilityChecker());
        await context.WriteAsync("copying.txt", "partial");

        var result = await context.ProcessAsync(FullBatch(WatchedScanReason.UserFullReconciliation));

        var file = Assert.Single(result.Catalogue.Files);
        Assert.Equal(1, result.Summary.Deferred);
        Assert.Equal(1, result.Summary.Unresolved);
        Assert.False(result.Summary.IsComplete);
        Assert.Null(file.ContentHash);
        Assert.Null(file.Category);
        Assert.Equal(WatchedItemReprocessReason.DeferredUntilStable, file.LastReprocessReason);
    }

    [Fact]
    public async Task OptionalAiFailure_DoesNotFailDeterministicCatalogueUpdate()
    {
        using var context = CreateContext(
            configuration: configuration => configuration with { AiAnalysisEnabled = true },
            suggestionResult: new WatchedSuggestionResult([], true, true, ["AI unavailable."]));
        var path = await context.WriteAsync("new.txt", "data");

        var result = await context.ProcessAsync(FullBatch(WatchedScanReason.UserFullReconciliation));

        Assert.True(result.AiAttempted);
        Assert.True(result.AiFailed);
        Assert.Contains("AI unavailable.", result.Warnings);
        var file = Assert.Single(result.Catalogue.Files);
        Assert.Equal(path, file.FullPath);
        Assert.Equal(WatchedAiAnalysisState.Failed, file.AiAnalysisState);
        Assert.NotNull(file.AiLastAttemptUtc);
        Assert.NotNull(await context.CatalogueStore.GetAsync(
            context.Configuration.CatalogueId,
            CancellationToken.None));
    }

    [Fact]
    public async Task AiRetry_SendsOnlyFailedOrPendingItemsAndDoesNotRepeatCompletedAnalysis()
    {
        using var context = CreateContext(
            configuration: configuration => configuration with { AiAnalysisEnabled = true });
        await context.WriteAsync("completed.txt", "one");
        await context.WriteAsync("failed.txt", "two");
        await context.WriteAsync("not-requested.txt", "three");
        var initial = await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        var adjustedFiles = initial.Catalogue.Files.Select(file => file with
        {
            AiAnalysisState = Path.GetFileName(file.FullPath) switch
            {
                "completed.txt" => WatchedAiAnalysisState.Completed,
                "failed.txt" => WatchedAiAnalysisState.Failed,
                _ => WatchedAiAnalysisState.NotRequested,
            },
        }).ToArray();
        await context.CatalogueStore.UpsertAsync(
            initial.Catalogue with { Files = Array.AsReadOnly(adjustedFiles) },
            CancellationToken.None);

        await context.ProcessAsync(new WatchedChangeBatch(
            "batch:ai-retry",
            context.Configuration.Id,
            WatchedScanReason.AiRetry,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [],
            false));

        var affected = Assert.Single(context.Suggestions.AffectedHistory.Last());
        Assert.Equal("failed.txt", Path.GetFileName(affected.FullPath));
    }

    [Fact]
    public async Task DeferredFile_IsRetriedOnNextReconciliationEvenWhenMetadataIsUnchanged()
    {
        var stability = new FirstUnstableThenStableStabilityChecker();
        using var context = CreateContext(stability: stability);
        await context.WriteAsync("copying.txt", "complete after first observation");
        var first = await context.ProcessAsync(FullBatch(WatchedScanReason.UserFullReconciliation));
        Assert.Equal(
            WatchedItemReprocessReason.DeferredUntilStable,
            Assert.Single(first.Catalogue.Files).LastReprocessReason);

        var second = await context.ProcessAsync(FullBatch(WatchedScanReason.UserFullReconciliation));

        var file = Assert.Single(second.Catalogue.Files);
        Assert.NotNull(file.ContentHash);
        Assert.NotEqual(WatchedItemReprocessReason.DeferredUntilStable, file.LastReprocessReason);
        Assert.Equal(0, second.Summary.Unresolved);
        Assert.Single(context.Suggestions.AffectedHistory.Last());
    }

    [Fact]
    public async Task SuggestionPlan_IsReturnedReviewableAndDoesNotMutateFile()
    {
        var plan = new ChangePlan(
            ChangePlanSchema.CurrentVersion,
            "plan:watched",
            DateTimeOffset.UnixEpoch,
            "watched:watch:1:batch",
            Path.GetTempPath(),
            ChangePlanStatus.AwaitingReview,
            [],
            [],
            null,
            false);
        using var context = CreateContext(
            suggestionResult: new WatchedSuggestionResult([plan], false, false, []));
        var path = await context.WriteAsync("safe.txt", "unchanged");

        var result = await context.ProcessAsync(FullBatch(WatchedScanReason.UserFullReconciliation));

        Assert.Equal("unchanged", await File.ReadAllTextAsync(path));
        Assert.Equal(plan, Assert.Single(result.CreatedChangePlans));
        Assert.Equal(ChangePlanStatus.AwaitingReview, result.CreatedChangePlans[0].Status);
    }

    [Fact]
    public async Task OpenSorSeExecutionBatch_SuppressesSuggestionsButReconcilesCatalogue()
    {
        using var context = CreateContext();
        var oldPath = await context.WriteAsync("old.txt", "same");
        await context.ProcessAsync(FullBatch(WatchedScanReason.StartupOfflineReconciliation));
        var newPath = Path.Combine(context.Root, "new.txt");
        File.Move(oldPath, newPath);
        var detected = DateTimeOffset.UtcNow;

        var result = await context.ProcessAsync(new WatchedChangeBatch(
            "batch:self",
            context.Configuration.Id,
            WatchedScanReason.OpenSorSeExecution,
            detected,
            detected,
            [new WatchedFolderHint(context.Configuration.Id, WatchedPathChangeKind.FileRenamed, newPath, oldPath, detected)],
            false,
            true));

        Assert.Equal(newPath, Assert.Single(result.Catalogue.Files).FullPath);
        Assert.Empty(result.CreatedChangePlans);
        Assert.True(context.Suggestions.SuppressHistory.Last());
    }

    [Fact]
    public async Task PreCancelledScan_DoesNotCreateCatalogueOrResults()
    {
        using var context = CreateContext();
        await context.WriteAsync("file.txt", "data");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            context.Processor.ProcessAsync(
                context.Configuration,
                FullBatch(WatchedScanReason.UserFullReconciliation),
                cancellation.Token));

        Assert.Null(await context.CatalogueStore.GetAsync(context.Configuration.CatalogueId, CancellationToken.None));
    }

    private static ProcessorContext CreateContext(
        Func<WatchedFolderConfiguration, WatchedFolderConfiguration>? configuration = null,
        IFileStabilityChecker? stability = null,
        WatchedSuggestionResult? suggestionResult = null)
    {
        var workspace = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            $"opensorse-watched-processor-{Guid.NewGuid():N}"));
        var root = Directory.CreateDirectory(Path.Combine(workspace, "root")).FullName;
        var logging = new LoggingService();
        var errors = new ErrorHandler(logging);
        var pathPolicy = new WatchedFolderPathPolicy();
        var fileSystem = new PhysicalWatchedFileSystem(pathPolicy, logging);
        var catalogueStore = new JsonWatchedFolderCatalogueStore(
            Path.Combine(workspace, "data", "watched-catalogues.json"),
            logging);
        var suggestions = new RecordingSuggestionService(
            suggestionResult ?? new WatchedSuggestionResult([], false, false, []));
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch.AddDays(1));
        var watchedConfiguration = new WatchedFolderConfiguration(
            "watch:1",
            root,
            "Root",
            true,
            true,
            [],
            [],
            "default",
            null,
            true,
            false,
            new WatchedFolderNotificationPreferences(),
            TimeSpan.FromMilliseconds(250),
            null,
            null,
            WatchedFolderStatus.Watching,
            "catalogue:1");
        watchedConfiguration = configuration?.Invoke(watchedConfiguration) ?? watchedConfiguration;
        var processor = new WatchedFolderProcessor(
            catalogueStore,
            fileSystem,
            stability ?? new ImmediateStabilityChecker(fileSystem),
            new FileMetadataReader(logging, errors),
            new FileHasher(logging, errors),
            new FileClassifier(logging, errors),
            new DuplicateDetector(logging, errors),
            new RuleEngine(logging, errors),
            new ActionPlanner(logging, errors),
            new ConflictResolver(logging, errors),
            new SessionWatchedSortingRecipeResolver(),
            suggestions,
            pathPolicy,
            logging,
            contentIndexingService: null,
            timeProvider: time);
        return new ProcessorContext(
            workspace,
            root,
            watchedConfiguration,
            catalogueStore,
            suggestions,
            time,
            processor,
            logging);
    }

    private static ResolvedWorkflowConfiguration EffectiveWorkflow(
        WorkflowScanBehavior scanBehavior)
    {
        var profile = BuiltInWorkflowLibrary.Profiles[0] with
        {
            IncrementalScan = scanBehavior,
            SortingRecipeIds = [],
        };
        var snapshot = new WorkflowConfigurationSnapshot(
            profile.Id,
            profile.Name,
            profile.Revision,
            profile.ModifiedAtUtc,
            [],
            profile.Files,
            profile.Extraction,
            profile.Analysis,
            profile.Ai,
            profile.UncertaintyPolicy,
            profile.ChangePlans,
            profile.Notifications,
            scanBehavior,
            "watch:1",
            DateTimeOffset.UnixEpoch);
        return new ResolvedWorkflowConfiguration(
            profile,
            [],
            profile.Files,
            profile.Extraction,
            profile.Analysis,
            profile.Ai,
            profile.UncertaintyPolicy,
            profile.ChangePlans,
            profile.Notifications,
            scanBehavior,
            snapshot);
    }

    private static WatchedChangeBatch FullBatch(WatchedScanReason reason) => new(
        $"batch:{Guid.NewGuid():N}",
        "watch:1",
        reason,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        [],
        true);

    private static WatchedChangeBatch HintBatch(params WatchedFolderHint[] hints) => new(
        $"batch:{Guid.NewGuid():N}",
        "watch:1",
        WatchedScanReason.WatcherBatch,
        hints.Min(hint => hint.DetectedAtUtc),
        hints.Max(hint => hint.DetectedAtUtc),
        hints,
        false);

    private sealed class ProcessorContext : IDisposable
    {
        private readonly LoggingService _logging;

        public ProcessorContext(
            string workspace,
            string root,
            WatchedFolderConfiguration configuration,
            JsonWatchedFolderCatalogueStore catalogueStore,
            RecordingSuggestionService suggestions,
            MutableTimeProvider time,
            WatchedFolderProcessor processor,
            LoggingService logging)
        {
            Workspace = workspace;
            Root = root;
            Configuration = configuration;
            CatalogueStore = catalogueStore;
            Suggestions = suggestions;
            Time = time;
            Processor = processor;
            _logging = logging;
        }

        public string Workspace { get; }
        public string Root { get; }
        public WatchedFolderConfiguration Configuration { get; }
        public JsonWatchedFolderCatalogueStore CatalogueStore { get; }
        public RecordingSuggestionService Suggestions { get; }
        public MutableTimeProvider Time { get; }
        public WatchedFolderProcessor Processor { get; }

        public async Task<string> WriteAsync(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
            return Path.GetFullPath(path);
        }

        public Task<WatchedFolderProcessResult> ProcessAsync(WatchedChangeBatch batch) =>
            Processor.ProcessAsync(Configuration, batch, CancellationToken.None);

        public void Dispose()
        {
            _logging.Dispose();
            var fullPath = Path.GetFullPath(Workspace);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), fullPath, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
    }

    private sealed class ImmediateStabilityChecker(IWatchedFileSystem fileSystem) : IFileStabilityChecker
    {
        public async Task<FileStabilityResult> WaitForStableAsync(
            string path,
            TimeSpan observationPeriod,
            int maximumAttempts,
            CancellationToken cancellationToken)
        {
            var probe = await fileSystem.ProbeAsync(path, cancellationToken);
            return new FileStabilityResult(probe is not null, 1, "Stable for test.", probe);
        }
    }

    private sealed class AlwaysUnstableStabilityChecker : IFileStabilityChecker
    {
        public Task<FileStabilityResult> WaitForStableAsync(
            string path,
            TimeSpan observationPeriod,
            int maximumAttempts,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FileStabilityResult(false, maximumAttempts, "Still changing.", null));
    }

    private sealed class FirstUnstableThenStableStabilityChecker : IFileStabilityChecker
    {
        private bool _first = true;

        public Task<FileStabilityResult> WaitForStableAsync(
            string path,
            TimeSpan observationPeriod,
            int maximumAttempts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_first)
            {
                _first = false;
                return Task.FromResult(new FileStabilityResult(false, maximumAttempts, "Still changing.", null));
            }

            var info = new FileInfo(path);
            info.Refresh();
            var probe = new WatchedFileProbe(
                info.FullName,
                false,
                info.Length,
                new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                info.Attributes,
                "test-stable-id");
            return Task.FromResult(new FileStabilityResult(true, 1, "Stable.", probe));
        }
    }

    private sealed class RecordingSuggestionService(WatchedSuggestionResult result) : IWatchedSuggestionService
    {
        public List<IReadOnlyList<ResultFile>> AffectedHistory { get; } = [];
        public List<bool> SuppressHistory { get; } = [];

        public Task<WatchedSuggestionResult> CreateSuggestionsAsync(
            WatchedFolderConfiguration configuration,
            ResultsSnapshot snapshot,
            IReadOnlyList<ResultFile> affectedFiles,
            bool suppressSuggestions,
            CancellationToken cancellationToken)
        {
            AffectedHistory.Add(Array.AsReadOnly(affectedFiles.ToArray()));
            SuppressHistory.Add(suppressSuggestions);
            return Task.FromResult(suppressSuggestions
                ? new WatchedSuggestionResult([], false, false, [])
                : result);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan value) => current += value;
    }
}
