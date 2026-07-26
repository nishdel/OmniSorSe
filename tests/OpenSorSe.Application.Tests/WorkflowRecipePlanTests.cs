#pragma warning disable CS1591

using OpenSorSe.Application.Models;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

public sealed class WorkflowRecipePlanTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.WorkflowPlan.Tests",
        Guid.NewGuid().ToString("N"));

    public WorkflowRecipePlanTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RecipeOutput_UsesExistingReviewOnlyChangePlanWithCompleteProvenance()
    {
        var engine = new WorkflowTemplateEngine();
        var validator = new WorkflowValidator(engine);
        var library = new WorkflowLibraryService(
            new JsonWorkflowLibraryStore(
                Path.Combine(_workspace, "library.json"),
                validator,
                new LoggingService()),
            validator,
            new EmptyUsageInspector());
        var recipe = await library.DuplicateRecipeAsync(
            BuiltInWorkflowIds.GeneralDocumentRecipe,
            "Reviewed filing",
            CancellationToken.None);
        var profile = await library.CreateProfileAsync(
            BuiltInWorkflowLibrary.Profiles[0] with
            {
                Id = "profile:reviewed-filing",
                Name = "Reviewed filing",
                IsBuiltIn = false,
                Origin = new WorkflowProfileOrigin(WorkflowOriginKind.UserCreated),
                SortingRecipeIds = [recipe.Id],
            },
            CancellationToken.None);
        var resolver = new WorkflowConfigurationResolver(
            library,
            new FixedConfiguration(new ApplicationSettings()));
        var resolved = (await resolver.ResolveForManualScanAsync(
            profile.Id,
            null,
            CancellationToken.None)).Configuration!;
        var source = Path.Combine(_workspace, "report.pdf");
        await File.WriteAllTextAsync(source, "unchanged");
        var info = new FileInfo(source);
        var file = new ResultFile(
            "file:1",
            source,
            info.Name,
            ".pdf",
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            FileCategory.Document,
            "Document",
            DuplicateStatus.Unique,
            null,
            false);
        var store = new JsonChangePlanStore(
            Path.Combine(_workspace, "plans.json"),
            new LoggingService());
        var fileSystem = new PhysicalFileSystemGateway();
        var planService = new WorkflowRecipePlanService(
            engine,
            new ChangePlanFactory(
                fileSystem,
                new ChangePlanValidator(fileSystem),
                store));

        var result = await planService.CreatePlanAsync(
            resolved,
            _workspace,
            "scan:workflow",
            [file],
            CancellationToken.None);

        var plan = Assert.IsType<ChangePlan>(result.Plan);
        Assert.Equal(ChangePlanStatus.AwaitingReview, plan.Status);
        Assert.All(plan.Actions, action => Assert.Equal(ChangeApprovalState.Pending, action.ApprovalState));
        Assert.All(plan.Actions, action =>
        {
            var provenance = Assert.IsType<ChangeWorkflowProvenance>(action.WorkflowProvenance);
            Assert.Equal(profile.Id, provenance.ProfileId);
            Assert.Equal(recipe.Id, provenance.RecipeId);
            Assert.Equal(recipe.Revision, provenance.RecipeRevision);
            Assert.False(provenance.IsAiAssisted);
            Assert.NotEmpty(provenance.EvidenceSources);
        });
        Assert.True(File.Exists(source));
        Assert.False(Directory.Exists(Path.Combine(_workspace, "Documents")));
        var persistedPlans = await store.ListAsync(CancellationToken.None);
        var persisted = Assert.Single(persistedPlans);
        Assert.All(persisted.Actions, action =>
            Assert.NotNull(action.WorkflowProvenance));
    }

    [Fact]
    public async Task MultipleMatchingRecipes_UsePriorityThenStableIdAndReportOrdering()
    {
        var baseRecipe = BuiltInWorkflowLibrary.Recipes[0];
        var low = baseRecipe with { Id = "recipe:z", Name = "Low", Priority = 1 };
        var high = baseRecipe with
        {
            Id = "recipe:a",
            Name = "High",
            Priority = 10,
            DestinationTemplate = "High",
        };
        var profile = BuiltInWorkflowLibrary.Profiles[0] with
        {
            Id = "profile:priority",
            Name = "Priority",
            IsBuiltIn = false,
            SortingRecipeIds = [low.Id, high.Id],
        };
        var snapshot = Snapshot(profile, [low, high]);
        var configuration = new ResolvedWorkflowConfiguration(
            profile,
            [low, high],
            profile.Files,
            profile.Extraction,
            profile.Analysis,
            profile.Ai with { Enabled = false },
            profile.UncertaintyPolicy,
            profile.ChangePlans,
            profile.Notifications,
            profile.FullScan,
            snapshot);
        var source = Path.Combine(_workspace, "sample.pdf");
        await File.WriteAllTextAsync(source, "data");
        var info = new FileInfo(source);
        var file = new ResultFile(
            "file:priority",
            source,
            info.Name,
            ".pdf",
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            FileCategory.Document,
            "Document",
            DuplicateStatus.Unique,
            null,
            false);
        var fileSystem = new PhysicalFileSystemGateway();
        var service = new WorkflowRecipePlanService(
            new WorkflowTemplateEngine(),
            new ChangePlanFactory(
                fileSystem,
                new ChangePlanValidator(fileSystem),
                new RecordingChangePlanStore()));

        var result = await service.CreatePlanAsync(
            configuration,
            _workspace,
            "scan:priority",
            [file],
            CancellationToken.None);

        Assert.Contains(result.Warnings, warning =>
            warning.Contains("matched 2 recipes", StringComparison.OrdinalIgnoreCase));
        Assert.All(result.Plan!.Actions, action =>
            Assert.Equal(high.Id, action.WorkflowProvenance!.RecipeId));
    }

    private static WorkflowConfigurationSnapshot Snapshot(
        WorkflowProfile profile,
        IReadOnlyList<SortingRecipe> recipes) =>
        new(
            profile.Id,
            profile.Name,
            profile.Revision,
            profile.ModifiedAtUtc,
            Array.AsReadOnly(recipes.Select(recipe =>
                new WorkflowRecipeSnapshot(
                    recipe.Id,
                    recipe.Name,
                    recipe.Revision,
                    recipe.ModifiedAtUtc,
                    recipe.Priority)).ToArray()),
            profile.Files,
            profile.Extraction,
            profile.Analysis,
            profile.Ai,
            profile.UncertaintyPolicy,
            profile.ChangePlans,
            profile.Notifications,
            profile.FullScan,
            "test",
            DateTimeOffset.UnixEpoch);

    private sealed class RecordingChangePlanStore : IChangePlanStore
    {
        public List<ChangePlan> Plans { get; } = [];
        public Task<IReadOnlyList<ChangePlan>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChangePlan>>(Plans.ToArray());
        public Task<ChangePlan?> GetAsync(string planId, CancellationToken cancellationToken) =>
            Task.FromResult(Plans.FirstOrDefault(plan => plan.PlanId == planId));
        public Task UpsertAsync(ChangePlan plan, CancellationToken cancellationToken)
        {
            Plans.RemoveAll(item => item.PlanId == plan.PlanId);
            Plans.Add(plan);
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyUsageInspector : IWorkflowUsageInspector
    {
        public Task<WorkflowUsageInfo> InspectAsync(string itemId, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowUsageInfo(itemId, [], [], 0));
    }

    private sealed class FixedConfiguration(ApplicationSettings settings) : IConfigurationService
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
}
