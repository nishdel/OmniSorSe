#pragma warning disable CS1591

using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Logging;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

public sealed class WorkflowsViewModelTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.WorkflowsViewModel.Tests",
        Guid.NewGuid().ToString("N"));

    public WorkflowsViewModelTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshSearchAndFilters_ExposeBuiltInsRecipesAndArchivedItems()
    {
        var (viewModel, _) = CreateViewModel();
        await viewModel.RefreshAsync();

        Assert.Equal(5, viewModel.Profiles.Count);
        Assert.Equal(4, viewModel.Recipes.Count);
        Assert.All(viewModel.Profiles, row => Assert.True(row.Profile.IsBuiltIn));

        viewModel.SearchText = "photo";
        Assert.Single(viewModel.Profiles);
        Assert.Equal("Photos", viewModel.Profiles[0].Name);
        Assert.Single(viewModel.Recipes);

        viewModel.SearchText = string.Empty;
        viewModel.OnlyAiEnabled = true;
        Assert.All(viewModel.Profiles, row => Assert.True(row.Profile.Ai.Enabled));
        viewModel.OnlyAiEnabled = false;
        viewModel.OnlyDuplicateEnabled = true;
        Assert.All(viewModel.Profiles, row => Assert.True(row.Profile.Analysis.DuplicateAnalysisEnabled));
        viewModel.OnlyDuplicateEnabled = false;
        viewModel.OnlyUserCreated = true;
        Assert.Empty(viewModel.Profiles);
        Assert.False(viewModel.OnlyBuiltIn);
    }

    [Fact]
    public async Task CreateDuplicateArchiveRestoreAndAssignment_UsePersistentLibrary()
    {
        var (viewModel, library) = CreateViewModel();
        await viewModel.RefreshAsync();
        viewModel.NewProfileCommand.Execute(null);
        viewModel.ProfileName = "My workflow";
        viewModel.ProfileDescription = "Created from the structured editor.";

        await viewModel.SaveProfileCommand.ExecuteAsync(null);

        var created = Assert.Single(
            await library.ListProfilesAsync(true, CancellationToken.None),
            profile => profile.Name == "My workflow");
        Assert.False(created.IsBuiltIn);
        Assert.Equal(1, created.Revision);
        Assert.Equal("My workflow", viewModel.SelectedProfile!.Name);

        string? assigned = null;
        string? scan = null;
        viewModel.AssignToWatchedFolderRequested += (_, id) => assigned = id;
        viewModel.RunScanRequested += (_, id) => scan = id;
        viewModel.AssignToWatchedFolderCommand.Execute(null);
        viewModel.RunScanCommand.Execute(null);
        Assert.Equal(created.Id, assigned);
        Assert.Equal(created.Id, scan);

        await viewModel.DuplicateProfileCommand.ExecuteAsync(null);
        Assert.Contains(
            await library.ListProfilesAsync(true, CancellationToken.None),
            profile => profile.Origin.Kind == WorkflowOriginKind.Duplicated);

        viewModel.ShowArchived = true;
        await viewModel.ToggleProfileArchiveCommand.ExecuteAsync(null);
        Assert.True(viewModel.SelectedProfile!.Profile.IsArchived);
        await viewModel.ToggleProfileArchiveCommand.ExecuteAsync(null);
        Assert.False(viewModel.SelectedProfile!.Profile.IsArchived);
    }

    [Fact]
    public async Task RecipePreviewAndImportConflict_ReportSafeValidationState()
    {
        var (viewModel, _) = CreateViewModel();
        await viewModel.RefreshAsync();
        viewModel.NamingTemplate = "{vendor}";
        viewModel.DestinationTemplate = "../Outside";
        viewModel.RequiredFields = "vendor";
        viewModel.SampleValues = "vendor=Example";

        viewModel.PreviewRecipeCommand.Execute(null);

        Assert.Contains("Unavailable", viewModel.PreviewText, StringComparison.Ordinal);
        Assert.Contains("invalid", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);

        viewModel.DestinationTemplate = "Reviewed/{vendor}";
        viewModel.SampleAiFields = "vendor";
        viewModel.PreviewRecipeCommand.Execute(null);
        Assert.Contains(
            "AI-derived values required: True",
            viewModel.PreviewText,
            StringComparison.Ordinal);

        viewModel.TransferJson = await new WorkflowImportExportService(
                GetLibrary(viewModel),
                new WorkflowValidator(new WorkflowTemplateEngine()))
            .ExportRecipeAsync(BuiltInWorkflowIds.GeneralDocumentRecipe, CancellationToken.None);
        viewModel.ImportConflictPolicy = WorkflowImportConflictPolicy.Cancel;
        await viewModel.ImportCommand.ExecuteAsync(null);
        Assert.Contains("cancelled", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReferencedRecipeDeletion_IsBlockedWithDependencyWarning()
    {
        var (viewModel, library) = CreateViewModel();
        var recipe = await library.DuplicateRecipeAsync(
            BuiltInWorkflowIds.GeneralDocumentRecipe,
            "Referenced recipe",
            CancellationToken.None);
        await library.CreateProfileAsync(
            BuiltInWorkflowLibrary.Profiles[0] with
            {
                Id = "profile:references-recipe",
                Name = "References recipe",
                IsBuiltIn = false,
                Origin = new WorkflowProfileOrigin(WorkflowOriginKind.UserCreated),
                SortingRecipeIds = [recipe.Id],
            },
            CancellationToken.None);
        await viewModel.RefreshAsync();
        viewModel.SelectedRecipe = viewModel.Recipes.Single(row => row.Id == recipe.Id);

        viewModel.RequestDeleteRecipeCommand.Execute(null);
        await viewModel.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Contains("referenced", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await library.GetRecipeAsync(recipe.Id, CancellationToken.None));
    }

    [Fact]
    public async Task WatchedProfileSelector_UsesDurableChoicesAndShowsUnavailableState()
    {
        var (_, library) = CreateViewModel();
        var viewModel = new WatchedFoldersViewModel(workflowLibrary: library);
        await viewModel.RefreshWorkflowChoicesAsync();

        viewModel.SelectProfileForEditor(BuiltInWorkflowIds.Photos);

        Assert.Equal(BuiltInWorkflowIds.Photos, viewModel.ScanProfileId);
        Assert.Equal("Photos", viewModel.SelectedWorkflowProfile!.Name);
        var unavailable = new WatchedFolderRow(WatchedConfiguration() with
        {
            Status = OpenSorSe.Application.Watching.WatchedFolderStatus.ProfileUnavailable,
        });
        Assert.Contains("Profile unavailable", unavailable.AvailabilityText, StringComparison.OrdinalIgnoreCase);
    }

    private (WorkflowsViewModel ViewModel, WorkflowLibraryService Library) CreateViewModel()
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
        var transfer = new WorkflowImportExportService(library, validator);
        var viewModel = new WorkflowsViewModel(library, transfer, engine);
        _libraries[viewModel] = library;
        return (viewModel, library);
    }

    private readonly Dictionary<WorkflowsViewModel, WorkflowLibraryService> _libraries = [];

    private WorkflowLibraryService GetLibrary(WorkflowsViewModel viewModel) => _libraries[viewModel];

    private static OpenSorSe.Application.Watching.WatchedFolderConfiguration WatchedConfiguration() =>
        new(
            "watch:1",
            Path.GetTempPath(),
            "Watched",
            true,
            true,
            [],
            [],
            BuiltInWorkflowIds.GeneralDocuments,
            null,
            true,
            false,
            new OpenSorSe.Application.Watching.WatchedFolderNotificationPreferences(),
            TimeSpan.FromSeconds(2),
            null,
            null,
            OpenSorSe.Application.Watching.WatchedFolderStatus.Watching,
            "catalog:1");

    private sealed class EmptyUsageInspector : IWorkflowUsageInspector
    {
        public Task<WorkflowUsageInfo> InspectAsync(string itemId, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowUsageInfo(itemId, [], [], 0));
    }
}
