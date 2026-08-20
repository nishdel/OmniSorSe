#pragma warning disable CS1591

using OpenSorSe.Application.Workflows;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Desktop.Tests;

public sealed class ReviewedOrganizationViewModelTests
{
    [Fact]
    public async Task OpenAndPreview_UsesExplicitStableIdsAndPublishesBoundedAccessibleState()
    {
        var recipe = BuiltInWorkflowLibrary.Recipes.Single(item =>
            item.Id == BuiltInWorkflowIds.TrustedClassificationRecipe);
        var service = new OrganizationService(recipe, CreateProposal(recipe, 125));
        using var viewModel = new ReviewedOrganizationViewModel(service, new RecipeLibrary(recipe));
        var selectedIds = Enumerable.Range(1, 125).Select(index => $"file:{index}").ToArray();

        await viewModel.OpenAsync(new OrganizationSelectionContext(
            OrganizationSelectionOrigin.SavedView,
            "Saved View Invoices",
            selectedIds));
        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.Equal(selectedIds, Assert.Single(service.Requests).SelectedFileIds);
        Assert.Equal(100, viewModel.VisibleRows.Count);
        Assert.True(viewModel.HasProposal);
        Assert.True(viewModel.HasSensitivePathWarning);
        Assert.Contains("125", viewModel.PreviewRangeText, StringComparison.Ordinal);
        Assert.Contains("visible in backups", viewModel.PrivacyWarningText, StringComparison.Ordinal);
        Assert.True(viewModel.ReviewChangesCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditAfterPreview_InvalidatesProposalUntilRepreview()
    {
        var recipe = BuiltInWorkflowLibrary.Recipes.Single(item =>
            item.Id == BuiltInWorkflowIds.TrustedClassificationRecipe);
        var service = new OrganizationService(recipe, CreateProposal(recipe, 1));
        using var viewModel = new ReviewedOrganizationViewModel(service, new RecipeLibrary(recipe));
        await viewModel.OpenAsync(new OrganizationSelectionContext(
            OrganizationSelectionOrigin.Files,
            "Files",
            ["file:1"]));
        await viewModel.PreviewCommand.ExecuteAsync(null);

        viewModel.DestinationPattern = "{documentType}";

        Assert.False(viewModel.HasProposal);
        Assert.False(viewModel.ReviewChangesCommand.CanExecute(null));
        Assert.Contains("Preview again", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewChanges_RaisesExistingChangePlanOnlyAfterExplicitCommand()
    {
        var recipe = BuiltInWorkflowLibrary.Recipes.Single(item =>
            item.Id == BuiltInWorkflowIds.TrustedClassificationRecipe);
        var service = new OrganizationService(recipe, CreateProposal(recipe, 1));
        using var viewModel = new ReviewedOrganizationViewModel(service, new RecipeLibrary(recipe));
        ChangePlan? emitted = null;
        viewModel.ChangePlanCreated += (_, plan) => emitted = plan;
        await viewModel.OpenAsync(new OrganizationSelectionContext(
            OrganizationSelectionOrigin.Search,
            "Search",
            ["file:1"]));
        await viewModel.PreviewCommand.ExecuteAsync(null);

        Assert.Null(emitted);
        await viewModel.ReviewChangesCommand.ExecuteAsync(null);

        Assert.NotNull(emitted);
        Assert.Equal(1, service.CreateCalls);
        Assert.Contains("No file operation has run", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelPreview_CancelsBoundedWorkAndRestoresCommandState()
    {
        var recipe = BuiltInWorkflowLibrary.Recipes.Single(item =>
            item.Id == BuiltInWorkflowIds.TrustedClassificationRecipe);
        var service = new CancellingOrganizationService();
        using var viewModel = new ReviewedOrganizationViewModel(service, new RecipeLibrary(recipe));
        await viewModel.OpenAsync(new OrganizationSelectionContext(
            OrganizationSelectionOrigin.Files,
            "Files",
            ["file:1"]));

        var running = viewModel.PreviewCommand.ExecuteAsync(null);
        await service.Started.Task;
        viewModel.CancelPreviewCommand.Execute(null);
        await running;

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasProposal);
        Assert.Contains("cancelled", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private static OrganizationProposalSet CreateProposal(SortingRecipe recipe, int count)
    {
        var rows = Enumerable.Range(1, count)
            .Select(index => new OrganizationProposalRow(
                $"file:{index}",
                $"C:\\Library\\old-{index}.pdf",
                $"new-{index}.pdf",
                "Finance\\Invoice",
                $"C:\\Library\\Finance\\Invoice\\new-{index}.pdf",
                OrganizationProposalReadiness.Reliable,
                [new OrganizationEvidenceMapping("{theme}", "Finance", "accepted Theme Smart Tag", true)],
                [],
                [],
                [],
                [],
                10,
                DateTimeOffset.UnixEpoch))
            .ToArray();
        return new OrganizationProposalSet(
            "preview:1",
            recipe,
            "C:\\Library",
            "source:1",
            Array.AsReadOnly(rows.Select(row => row.FileId).ToArray()),
            Array.AsReadOnly(rows),
            [new OrganizationEvidenceCoverage("theme", "Theme", count, count)],
            count,
            0,
            [],
            true,
            "fingerprint");
    }

    private sealed class OrganizationService(
        SortingRecipe recipe,
        OrganizationProposalSet proposal) : IReviewedOrganizationService
    {
        public List<OrganizationPreviewRequest> Requests { get; } = [];
        public int CreateCalls { get; private set; }

        public Task<OrganizationProposalSet> PreviewAsync(
            OrganizationPreviewRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var selected = request.SelectedFileIds.Count == proposal.SelectedFileIds.Count
                ? proposal
                : CreateProposal(recipe, request.SelectedFileIds.Count);
            return Task.FromResult(selected with
            {
                Recipe = request.Recipe,
                SelectedFileIds = request.SelectedFileIds,
            });
        }

        public Task<ChangePlan> CreateChangePlanAsync(
            OrganizationProposalSet proposalSet,
            string sourceContextId,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(new ChangePlan(
                ChangePlanSchema.CurrentVersion,
                "plan:organization",
                DateTimeOffset.UnixEpoch,
                sourceContextId,
                proposalSet.OrganizationRoot,
                ChangePlanStatus.AwaitingReview,
                [],
                [],
                null,
                false));
        }
    }

    private sealed class CancellingOrganizationService : IReviewedOrganizationService
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<OrganizationProposalSet> PreviewAsync(
            OrganizationPreviewRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }

        public Task<ChangePlan> CreateChangePlanAsync(
            OrganizationProposalSet proposal,
            string sourceContextId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecipeLibrary(SortingRecipe recipe) : IWorkflowLibraryService
    {
        public string? RecoveryMessage => null;
        public string? PreservedCorruptCopyPath => null;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<SortingRecipe>> ListRecipesAsync(bool includeArchived, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SortingRecipe>>([recipe]);
        public Task<IReadOnlyList<WorkflowProfile>> ListProfilesAsync(bool includeArchived, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorkflowProfile>>([]);
        public Task<WorkflowProfile?> GetProfileAsync(string id, CancellationToken cancellationToken) => Task.FromResult<WorkflowProfile?>(null);
        public Task<SortingRecipe?> GetRecipeAsync(string id, CancellationToken cancellationToken) => Task.FromResult<SortingRecipe?>(recipe.Id == id ? recipe : null);
        public Task<WorkflowProfile> CreateProfileAsync(WorkflowProfile profile, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkflowProfile> DuplicateProfileAsync(string id, string newName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkflowProfile> UpdateProfileAsync(WorkflowProfile profile, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkflowProfile> SetProfileArchivedAsync(string id, bool archived, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkflowProfile> SetProfileEnabledAsync(string id, bool enabled, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SortingRecipe> CreateRecipeAsync(SortingRecipe value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SortingRecipe> DuplicateRecipeAsync(string id, string newName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SortingRecipe> UpdateRecipeAsync(SortingRecipe value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SortingRecipe> SetRecipeArchivedAsync(string id, bool archived, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteRecipeAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkflowUsageInfo> GetUsageAsync(string itemId, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowUsageInfo(itemId, [], [], 0));
        public void RecordDiagnostic(WorkflowDiagnosticKind kind, string summary, string? itemId = null) { }
        public IReadOnlyList<WorkflowDiagnostic> GetDiagnostics() => [];
    }
}
