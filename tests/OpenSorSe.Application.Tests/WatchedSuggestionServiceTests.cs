#pragma warning disable CS1591

using OpenSorSe.Application.AI;
using OpenSorSe.Application.ChangePlans;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Watching;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Executor.Models;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

public sealed class WatchedSuggestionServiceTests
{
    [Fact]
    public async Task SessionRecipeResolver_UsesOnlyExplicitCurrentSessionRecipe()
    {
        var resolver = new SessionWatchedSortingRecipeResolver();
        var rule = new FileRule(
            "rule:txt",
            "Move text",
            10,
            [new RuleCondition(RuleConditionKind.ExtensionEquals, ".txt")],
            new RuleAction(RuleActionKind.Move, "Documents"));
        resolver.SetCurrentRules([rule]);

        Assert.Equal(rule, Assert.Single(await resolver.ResolveAsync("current", CancellationToken.None)));
        Assert.Empty(await resolver.ResolveAsync("named-but-not-persisted", CancellationToken.None));
        Assert.Empty(await resolver.ResolveAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task FolderAiDisabled_DoesNotCallProviderOrCreateAiPlan()
    {
        var ai = new RecordingAiService();
        var factory = new RecordingPlanFactory();
        var service = CreateService(ai, factory, AiSettingsEnabled());
        var snapshot = Snapshot(1);

        var result = await service.CreateSuggestionsAsync(
            Configuration(aiEnabled: false),
            snapshot,
            snapshot.Files,
            false,
            CancellationToken.None);

        Assert.False(result.AiAttempted);
        Assert.Equal(0, ai.FolderCalls);
        Assert.Empty(result.Plans);
    }

    [Fact]
    public async Task GlobalAiDisabled_DoesNotCallProviderAndExplainsGate()
    {
        var ai = new RecordingAiService();
        var factory = new RecordingPlanFactory();
        var service = CreateService(ai, factory, new AiSettings());
        var snapshot = Snapshot(1);

        var result = await service.CreateSuggestionsAsync(
            Configuration(aiEnabled: true),
            snapshot,
            snapshot.Files,
            false,
            CancellationToken.None);

        Assert.False(result.AiAttempted);
        Assert.Equal(0, ai.FolderCalls);
        Assert.Contains(result.Warnings, warning => warning.Contains("global AI switch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnabledFolderAi_BatchesToPromptLimitAndCreatesReviewOnlyPlans()
    {
        var ai = new RecordingAiService();
        var factory = new RecordingPlanFactory();
        var service = CreateService(ai, factory, AiSettingsEnabled());
        var snapshot = Snapshot(25);

        var result = await service.CreateSuggestionsAsync(
            Configuration(aiEnabled: true),
            snapshot,
            snapshot.Files,
            false,
            CancellationToken.None);

        Assert.True(result.AiAttempted);
        Assert.False(result.AiFailed);
        Assert.Equal([12, 12, 1], ai.FolderBatchSizes);
        Assert.Equal(3, result.Plans.Count);
        Assert.Equal(25, result.CompletedAiFileIds.Count);
        Assert.Empty(result.FailedAiFileIds);
        Assert.All(result.Plans, plan => Assert.Equal(ChangePlanStatus.AwaitingReview, plan.Status));
        Assert.Equal(3, factory.FolderCalls);
    }

    [Fact]
    public async Task AiFailure_RemainsIsolatedFromDeterministicPlan()
    {
        var ai = new RecordingAiService
        {
            FolderResult = new AiFolderStructureResult(
                AiAvailabilityState.Unavailable,
                "Model unavailable.",
                null),
        };
        var factory = new RecordingPlanFactory();
        var service = CreateService(ai, factory, AiSettingsEnabled());
        var snapshot = Snapshot(1, includeRuleOperation: true);

        var result = await service.CreateSuggestionsAsync(
            Configuration(aiEnabled: true),
            snapshot,
            snapshot.Files,
            false,
            CancellationToken.None);

        Assert.True(result.AiAttempted);
        Assert.True(result.AiFailed);
        Assert.Single(result.Plans);
        Assert.Empty(result.CompletedAiFileIds);
        Assert.Single(result.FailedAiFileIds);
        Assert.Equal(1, factory.RuleCalls);
        Assert.Contains("Model unavailable.", result.Warnings);
    }

    [Fact]
    public async Task LargeAiBacklog_IsBoundedPerRunAndLeavesRemainderPending()
    {
        var ai = new RecordingAiService();
        var factory = new RecordingPlanFactory();
        var service = CreateService(ai, factory, AiSettingsEnabled());
        var snapshot = Snapshot(121);

        var result = await service.CreateSuggestionsAsync(
            Configuration(aiEnabled: true),
            snapshot,
            snapshot.Files,
            false,
            CancellationToken.None);

        Assert.Equal(10, ai.FolderCalls);
        Assert.Equal(120, result.CompletedAiFileIds.Count);
        Assert.Single(snapshot.Files.Select(file => file.Id).Except(result.CompletedAiFileIds, StringComparer.Ordinal));
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("1 AI item(s) remain pending", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenSorSeExecutionSuppression_SkipsRuleAndAiSuggestionCreation()
    {
        var ai = new RecordingAiService();
        var factory = new RecordingPlanFactory();
        var service = CreateService(ai, factory, AiSettingsEnabled());
        var snapshot = Snapshot(1, includeRuleOperation: true);

        var result = await service.CreateSuggestionsAsync(
            Configuration(aiEnabled: true),
            snapshot,
            snapshot.Files,
            true,
            CancellationToken.None);

        Assert.Empty(result.Plans);
        Assert.False(result.AiAttempted);
        Assert.Equal(0, factory.RuleCalls);
        Assert.Equal(0, ai.FolderCalls);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("approved OmniSorSe operation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationDuringAiBatch_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var ai = new RecordingAiService();
        var factory = new RecordingPlanFactory();
        var service = CreateService(ai, factory, AiSettingsEnabled());
        var snapshot = Snapshot(1);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.CreateSuggestionsAsync(
                Configuration(aiEnabled: true),
                snapshot,
                snapshot.Files,
                false,
                cancellation.Token));
    }

    private static WatchedSuggestionService CreateService(
        IAiSuggestionService ai,
        ISuggestionChangePlanFactory factory,
        AiSettings aiSettings) =>
        new(
            factory,
            ai,
            new FixedConfigurationService(new ApplicationSettings { Ai = aiSettings }));

    private static AiSettings AiSettingsEnabled() => new()
    {
        Enabled = true,
        FolderStructureSuggestionsEnabled = true,
        SelectedModel = "test-model",
    };

    private static WatchedFolderConfiguration Configuration(bool aiEnabled) => new(
        "watch:1",
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "watched-root")),
        "Root",
        true,
        true,
        [],
        [],
        "default",
        null,
        true,
        aiEnabled,
        new WatchedFolderNotificationPreferences(),
        TimeSpan.FromSeconds(2),
        null,
        null,
        WatchedFolderStatus.Watching,
        "catalogue:1");

    private static ResultsSnapshot Snapshot(int fileCount, bool includeRuleOperation = false)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "watched-root"));
        var files = Enumerable.Range(0, fileCount)
            .Select(index => new ResultFile(
                $"file:{index}",
                Path.Combine(root, $"file-{index}.txt"),
                $"file-{index}.txt",
                ".txt",
                1,
                DateTimeOffset.UnixEpoch,
                FileCategory.Document,
                "Document",
                DuplicateStatus.Unique,
                null,
                includeRuleOperation && index == 0))
            .ToArray();
        var operations = includeRuleOperation
            ? new[]
            {
                new ResultPlannedOperation(
                    "operation:1",
                    PlannedOperationKind.Move,
                    files[0].Id,
                    Path.Combine(root, "Sorted", files[0].DisplayFileName),
                    "Test rule"),
            }
            : [];
        return new ResultsSnapshot(
            "watched:watch:1:batch:1",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            Array.AsReadOnly(files),
            [new ResultDirectory(root, "watched-root")],
            [],
            Array.AsReadOnly(operations),
            [],
            new ResultsSnapshotStatistics(fileCount, 1, 0, 0, operations.Length, 0, 0),
            true);
    }

    private sealed class FixedConfigurationService(ApplicationSettings settings) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = settings;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAiService : IAiSuggestionService
    {
        public int FolderCalls { get; private set; }
        public List<int> FolderBatchSizes { get; } = [];
        public AiFolderStructureResult? FolderResult { get; init; }

        public Task<AiConnectionResult> TestConnectionAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiConnectionResult(AiAvailabilityState.ModelSelected, "Ready.", []));

        public Task<AiConnectionResult> DiscoverModelsAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken) =>
            TestConnectionAsync(settings, cancellationToken);

        public Task<AiFileRenameResult> GenerateFileRenameAsync(
            AiFileRenameRequest request,
            AiSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiFileRenameResult(AiAvailabilityState.NoSuggestion, "None.", null));

        public Task<AiFolderStructureResult> GenerateFolderStructureAsync(
            AiFolderStructureRequest request,
            AiSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FolderCalls++;
            FolderBatchSizes.Add(request.Files.Count);
            if (FolderResult is not null)
            {
                return Task.FromResult(FolderResult);
            }

            var plan = new AiFolderStructurePlan(
                $"ai-plan:{FolderCalls}",
                [new AiSuggestedFolder("folder:1", "Sorted", null, "Sorted", "Test", 1)],
                Array.AsReadOnly(request.Files.Select(file =>
                    new AiFolderStructurePlanItem(file.Id, file.DisplayFileName, "Sorted")).ToArray()),
                "Test",
                "Ollama",
                "test-model",
                DateTimeOffset.UtcNow);
            return Task.FromResult(new AiFolderStructureResult(
                AiAvailabilityState.ModelSelected,
                "Ready.",
                plan));
        }

        public Task<AiDecisionResult> RecordDecisionAsync(
            AiSuggestionDecision decision,
            AiSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiDecisionResult(AiAvailabilityState.ModelSelected, "Recorded."));

        public Task<AiDecisionResult> ResetDecisionHistoryAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiDecisionResult(AiAvailabilityState.ModelSelected, "Reset."));
    }

    private sealed class RecordingPlanFactory : ISuggestionChangePlanFactory
    {
        public int RuleCalls { get; private set; }
        public int FolderCalls { get; private set; }

        public Task<ChangePlan> CreateRenamePlanAsync(
            ResultFile file,
            AiFileRenameSuggestion suggestion,
            string reviewedFileName,
            string? sourceScanId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Plan($"rename:{file.Id}", sourceScanId));

        public Task<ChangePlan> CreateFolderStructurePlanAsync(
            IReadOnlyList<ResultFile> files,
            AiFolderStructurePlan suggestion,
            string? sourceScanId,
            CancellationToken cancellationToken)
        {
            FolderCalls++;
            return Task.FromResult(Plan($"folder:{FolderCalls}", sourceScanId));
        }

        public Task<ChangePlan> CreateRulePlanAsync(
            ResultsSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            RuleCalls++;
            return Task.FromResult(Plan($"rule:{RuleCalls}", snapshot.SessionId));
        }

        private static ChangePlan Plan(string id, string? sourceScanId) => new(
            ChangePlanSchema.CurrentVersion,
            id,
            DateTimeOffset.UtcNow,
            sourceScanId,
            Path.GetFullPath(Path.GetTempPath()),
            ChangePlanStatus.AwaitingReview,
            [],
            [],
            null,
            false);
    }
}
