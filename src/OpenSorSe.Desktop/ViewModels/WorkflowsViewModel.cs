#pragma warning disable CS1591

using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core;
using OpenSorSe.Core.Platform;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.ViewModels;

public sealed record WorkflowProfileRow(WorkflowProfile Profile, WorkflowUsageInfo? Usage = null)
{
    public string Id => Profile.Id;
    public string Name => Profile.Name;
    public string Description => Profile.Description ?? "No description";
    public string OriginText => Profile.IsBuiltIn ? "Built-in" : "User-created";
    public string StateText => Profile.IsArchived ? "Archived" : Profile.IsEnabled ? "Enabled" : "Disabled";
    public string CapabilityText =>
        $"{(Profile.Extraction.OcrEnabled ? "OCR" : "No OCR")} · " +
        $"{(Profile.Analysis.DuplicateAnalysisEnabled ? "Duplicates" : "No duplicates")} · " +
        $"{(Profile.Ai.Enabled ? "Optional AI" : "No AI")}";
    public string UsageText => Usage is null
        ? "Usage not loaded"
        : $"{Usage.WatchedFolderIds.Count} watched folder(s), {Usage.RecentScanCount} recent saved scan(s)";
}

public sealed record SortingRecipeRow(SortingRecipe Recipe, WorkflowUsageInfo? Usage = null)
{
    public string Id => Recipe.Id;
    public string Name => Recipe.Name;
    public string Description => Recipe.Description ?? "No description";
    public string OriginText => Recipe.IsBuiltIn ? "Built-in" : "User-created";
    public string StateText => Recipe.IsArchived ? "Archived" : Recipe.IsEnabled ? "Enabled" : "Disabled";
    public string TemplateText =>
        $"{Recipe.NamingTemplate} → {Recipe.DestinationTemplate} · {Recipe.FileNamePortability}";
    public string UsageText => Usage is null
        ? "Usage not loaded"
        : $"{Usage.ProfileIds.Count} profile(s), {Usage.WatchedFolderIds.Count} watched folder(s)";
}

public sealed class WorkflowRecipeSelectionRow : ViewModelBase
{
    private bool _isSelected;

    public WorkflowRecipeSelectionRow(SortingRecipe recipe, bool isSelected)
    {
        Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        _isSelected = isSelected;
    }

    public SortingRecipe Recipe { get; }
    public string Id => Recipe.Id;
    public string Name => Recipe.Name;
    public string Description => Recipe.Description ?? "No description";
    public string OriginText => Recipe.IsBuiltIn ? "Built-in" : $"Revision {Recipe.Revision}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class WorkflowsViewModel : ViewModelBase
{
    private readonly IWorkflowLibraryService? _library;
    private readonly IWorkflowImportExportService? _importExport;
    private readonly IWorkflowTemplateEngine? _templateEngine;
    private readonly ObservableCollection<WorkflowProfileRow> _profiles = [];
    private readonly ObservableCollection<SortingRecipeRow> _recipes = [];
    private readonly ObservableCollection<WorkflowRecipeSelectionRow> _profileRecipeChoices = [];
    private IReadOnlyList<WorkflowProfile> _allProfiles = [];
    private IReadOnlyList<SortingRecipe> _allRecipes = [];
    private WorkflowProfileRow? _selectedProfile;
    private SortingRecipeRow? _selectedRecipe;
    private bool _isBusy;
    private bool _showArchived;
    private bool _onlyBuiltIn;
    private bool _onlyUserCreated;
    private bool _onlyAiEnabled;
    private bool _onlyOcrEnabled;
    private bool _onlyDuplicateEnabled;
    private string _searchText = string.Empty;
    private string _fileTypeFilter = string.Empty;
    private string _statusText = "Workflow profiles automate configuration and analysis, not approval or file modification.";
    private bool _deleteConfirmationPending;
    private bool _deleteRecipe;

    public WorkflowsViewModel(
        IWorkflowLibraryService? library = null,
        IWorkflowImportExportService? importExport = null,
        IWorkflowTemplateEngine? templateEngine = null)
    {
        _library = library;
        _importExport = importExport;
        _templateEngine = templateEngine;
        Profiles = new ReadOnlyObservableCollection<WorkflowProfileRow>(_profiles);
        Recipes = new ReadOnlyObservableCollection<SortingRecipeRow>(_recipes);
        ProfileRecipeChoices = new ReadOnlyObservableCollection<WorkflowRecipeSelectionRow>(_profileRecipeChoices);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy && _library is not null);
        NewProfileCommand = new RelayCommand(NewProfile, () => !IsBusy);
        DuplicateProfileCommand = new AsyncRelayCommand(DuplicateProfileAsync, CanUseSelectedProfile);
        SaveProfileCommand = new AsyncRelayCommand(SaveProfileAsync, () => !IsBusy && _library is not null);
        ToggleProfileArchiveCommand = new AsyncRelayCommand(ToggleProfileArchiveAsync, CanEditSelectedProfile);
        ToggleProfileEnabledCommand = new AsyncRelayCommand(ToggleProfileEnabledAsync, CanEditSelectedProfile);
        NewRecipeCommand = new RelayCommand(NewRecipe, () => !IsBusy);
        DuplicateRecipeCommand = new AsyncRelayCommand(DuplicateRecipeAsync, CanUseSelectedRecipe);
        SaveRecipeCommand = new AsyncRelayCommand(SaveRecipeAsync, () => !IsBusy && _library is not null);
        ToggleRecipeArchiveCommand = new AsyncRelayCommand(ToggleRecipeArchiveAsync, CanEditSelectedRecipe);
        PreviewRecipeCommand = new RelayCommand(PreviewRecipe, () => !IsBusy && _templateEngine is not null);
        ExportProfileCommand = new AsyncRelayCommand(ExportProfileAsync, CanUseSelectedProfile);
        ExportRecipeCommand = new AsyncRelayCommand(ExportRecipeAsync, CanUseSelectedRecipe);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy && _importExport is not null);
        ExportDiagnosticsCommand = new RelayCommand(ExportDiagnostics, () => !IsBusy && _library is not null);
        RequestDeleteProfileCommand = new RelayCommand(
            () =>
            {
                _deleteRecipe = false;
                DeleteConfirmationPending = true;
            },
            CanDeleteProfile);
        RequestDeleteRecipeCommand = new RelayCommand(
            () =>
            {
                _deleteRecipe = true;
                DeleteConfirmationPending = true;
            },
            CanDeleteRecipe);
        ConfirmDeleteCommand = new AsyncRelayCommand(
            DeleteSelectedAsync,
            () => (_deleteRecipe ? CanDeleteRecipe() : CanDeleteProfile()) && DeleteConfirmationPending);
        CancelDeleteCommand = new RelayCommand(() => DeleteConfirmationPending = false, () => DeleteConfirmationPending && !IsBusy);
        AssignToWatchedFolderCommand = new RelayCommand(
            () => AssignToWatchedFolderRequested?.Invoke(this, SelectedProfile!.Id),
            CanUseSelectedProfile);
        RunScanCommand = new RelayCommand(
            () => RunScanRequested?.Invoke(this, SelectedProfile!.Id),
            CanUseSelectedProfile);
        LoadProfileEditor(BuiltInWorkflowLibrary.Profiles[0]);
        LoadRecipeEditor(BuiltInWorkflowLibrary.Recipes[0]);
    }

    public event EventHandler<string>? AssignToWatchedFolderRequested;
    public event EventHandler<string>? RunScanRequested;
    public event EventHandler<string>? LibraryChanged;

    public ReadOnlyObservableCollection<WorkflowProfileRow> Profiles { get; }
    public ReadOnlyObservableCollection<SortingRecipeRow> Recipes { get; }
    public ReadOnlyObservableCollection<WorkflowRecipeSelectionRow> ProfileRecipeChoices { get; }
    public IReadOnlyList<WorkflowAiInvocationPolicy> AiPolicies { get; } =
        Enum.GetValues<WorkflowAiInvocationPolicy>();
    public IReadOnlyList<WorkflowCasePolicy> CasePolicies { get; } =
        Enum.GetValues<WorkflowCasePolicy>();
    public IReadOnlyList<WorkflowMissingValuePolicy> MissingValuePolicies { get; } =
        Enum.GetValues<WorkflowMissingValuePolicy>();
    public IReadOnlyList<WorkflowCollisionPolicy> CollisionPolicies { get; } =
        Enum.GetValues<WorkflowCollisionPolicy>();
    public IReadOnlyList<WorkflowInvalidCharacterPolicy> InvalidCharacterPolicies { get; } =
        Enum.GetValues<WorkflowInvalidCharacterPolicy>();
    public IReadOnlyList<WorkflowUncertaintyPolicy> UncertaintyPolicies { get; } =
        Enum.GetValues<WorkflowUncertaintyPolicy>();
    public IReadOnlyList<WorkflowImportConflictPolicy> ImportConflictPolicies { get; } =
        Enum.GetValues<WorkflowImportConflictPolicy>();
    public IReadOnlyList<FileNamePortabilityMode> FileNamePortabilityModes { get; } =
        Enum.GetValues<FileNamePortabilityMode>();

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand NewProfileCommand { get; }
    public IAsyncRelayCommand DuplicateProfileCommand { get; }
    public IAsyncRelayCommand SaveProfileCommand { get; }
    public IAsyncRelayCommand ToggleProfileArchiveCommand { get; }
    public IAsyncRelayCommand ToggleProfileEnabledCommand { get; }
    public IRelayCommand NewRecipeCommand { get; }
    public IAsyncRelayCommand DuplicateRecipeCommand { get; }
    public IAsyncRelayCommand SaveRecipeCommand { get; }
    public IAsyncRelayCommand ToggleRecipeArchiveCommand { get; }
    public IRelayCommand PreviewRecipeCommand { get; }
    public IAsyncRelayCommand ExportProfileCommand { get; }
    public IAsyncRelayCommand ExportRecipeCommand { get; }
    public IAsyncRelayCommand ImportCommand { get; }
    public IRelayCommand ExportDiagnosticsCommand { get; }
    public IRelayCommand RequestDeleteProfileCommand { get; }
    public IRelayCommand RequestDeleteRecipeCommand { get; }
    public IAsyncRelayCommand ConfirmDeleteCommand { get; }
    public IRelayCommand CancelDeleteCommand { get; }
    public IRelayCommand AssignToWatchedFolderCommand { get; }
    public IRelayCommand RunScanCommand { get; }

    public WorkflowProfileRow? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                DeleteConfirmationPending = false;
                if (value is not null)
                {
                    LoadProfileEditor(value.Profile);
                    _ = LoadProfileUsageAsync(value);
                }

                RefreshCommands();
            }
        }
    }

    public SortingRecipeRow? SelectedRecipe
    {
        get => _selectedRecipe;
        set
        {
            if (SetProperty(ref _selectedRecipe, value))
            {
                DeleteConfirmationPending = false;
                if (value is not null)
                {
                    LoadRecipeEditor(value.Recipe);
                    _ = LoadRecipeUsageAsync(value);
                }

                RefreshCommands();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool DeleteConfirmationPending
    {
        get => _deleteConfirmationPending;
        private set
        {
            if (SetProperty(ref _deleteConfirmationPending, value))
            {
                ConfirmDeleteCommand.NotifyCanExecuteChanged();
                CancelDeleteCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public string FileTypeFilter
    {
        get => _fileTypeFilter;
        set
        {
            if (SetProperty(ref _fileTypeFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool ShowArchived
    {
        get => _showArchived;
        set
        {
            if (SetProperty(ref _showArchived, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool OnlyBuiltIn
    {
        get => _onlyBuiltIn;
        set
        {
            if (SetProperty(ref _onlyBuiltIn, value))
            {
                if (value && _onlyUserCreated)
                {
                    _onlyUserCreated = false;
                    OnPropertyChanged(nameof(OnlyUserCreated));
                }

                ApplyFilters();
            }
        }
    }

    public bool OnlyUserCreated
    {
        get => _onlyUserCreated;
        set
        {
            if (SetProperty(ref _onlyUserCreated, value))
            {
                if (value && _onlyBuiltIn)
                {
                    _onlyBuiltIn = false;
                    OnPropertyChanged(nameof(OnlyBuiltIn));
                }

                ApplyFilters();
            }
        }
    }

    public bool OnlyAiEnabled
    {
        get => _onlyAiEnabled;
        set
        {
            if (SetProperty(ref _onlyAiEnabled, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool OnlyOcrEnabled
    {
        get => _onlyOcrEnabled;
        set
        {
            if (SetProperty(ref _onlyOcrEnabled, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool OnlyDuplicateEnabled
    {
        get => _onlyDuplicateEnabled;
        set
        {
            if (SetProperty(ref _onlyDuplicateEnabled, value))
            {
                ApplyFilters();
            }
        }
    }

    public string ProfileName { get; set; } = "New workflow profile";
    public string ProfileDescription { get; set; } = string.Empty;
    public string IncludedFileTypes { get; set; } = string.Empty;
    public string ExcludedFileTypes { get; set; } = ".tmp, .part, .crdownload";
    public double MaximumFileSizeMiB { get; set; } = 1024;
    public bool IncludeHiddenFiles { get; set; }
    public bool MetadataEnabled { get; set; } = true;
    public bool TextEnabled { get; set; } = true;
    public bool OcrEnabled { get; set; }
    public bool OcrOnlyWhenTextUnavailable { get; set; } = true;
    public string OcrLanguage { get; set; } = "eng";
    public int MaximumPages { get; set; } = 25;
    public bool DuplicateAnalysisEnabled { get; set; } = true;
    public bool ClassificationEnabled { get; set; } = true;
    public bool RuleEvaluationEnabled { get; set; } = true;
    public bool ProfileAiEnabled { get; set; }
    public WorkflowAiInvocationPolicy AiPolicy { get; set; } = WorkflowAiInvocationPolicy.Disabled;
    public string ProfileAiFileTypes { get; set; } = string.Empty;
    public string SelectedRecipeIds { get; set; } = string.Empty;
    public WorkflowUncertaintyPolicy ProfileUncertaintyPolicy { get; set; } = WorkflowUncertaintyPolicy.Skip;
    public bool GenerateChangePlans { get; set; } = true;
    public bool PermitRenameProposals { get; set; } = true;
    public bool PermitMoveProposals { get; set; } = true;
    public bool PermitDirectoryProposals { get; set; } = true;
    public bool NotifyWhenComplete { get; set; } = true;
    public bool NotifyWhenPlanReady { get; set; } = true;
    public bool NotifyOnErrors { get; set; } = true;
    public bool IncrementalScanEnabled { get; set; } = true;
    public bool ReanalyseChangedContentOnly { get; set; } = true;
    public bool ReconcileMissingItems { get; set; } = true;
    public bool PreserveUnchangedAnalysis { get; set; } = true;
    public bool FullScanEnabled { get; set; } = true;

    public string RecipeName { get; set; } = "New sorting recipe";
    public string RecipeDescription { get; set; } = string.Empty;
    public string RecipeFileTypes { get; set; } = string.Empty;
    public string RecipeCategories { get; set; } = string.Empty;
    public double RecipeMinimumFileSizeMiB { get; set; }
    public double RecipeMaximumFileSizeMiB { get; set; }
    public int RecipePriority { get; set; } = 100;
    public string NamingTemplate { get; set; } = "{originalName}";
    public string DestinationTemplate { get; set; } = "Organized/{category}";
    public string RequiredFields { get; set; } = "originalName, category";
    public string OptionalFields { get; set; } = string.Empty;
    public string FallbackValues { get; set; } = "category=Unknown";
    public WorkflowCasePolicy CasePolicy { get; set; } = WorkflowCasePolicy.Preserve;
    public WorkflowInvalidCharacterPolicy InvalidCharacterPolicy { get; set; } =
        WorkflowInvalidCharacterPolicy.ReplaceWithUnderscore;
    public WorkflowMissingValuePolicy MissingValuePolicy { get; set; } = WorkflowMissingValuePolicy.UseFallback;
    public WorkflowCollisionPolicy CollisionPolicy { get; set; } = WorkflowCollisionPolicy.Block;
    public FileNamePortabilityMode FileNamePortability { get; set; } =
        FileNamePortabilityMode.Portable;
    public WorkflowUncertaintyPolicy RecipeUncertaintyPolicy { get; set; } =
        WorkflowUncertaintyPolicy.IncludeAsWarning;
    public string DefaultDateFormat { get; set; } = "yyyy-MM-dd";
    public bool CollapseWhitespace { get; set; } = true;
    public bool NormalizeUnicode { get; set; } = true;
    public int MaximumFileNameLength { get; set; } = WorkflowLibraryLimits.MaximumFileNameLength;
    public bool PreserveExtension { get; set; } = true;
    public string SamplePath { get; set; } = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSePreview",
        "sample.pdf");
    public string SampleValues { get; set; } =
        "date=2026-01-31\nvendor=Example Vendor\ndocumentType=Invoice\namount=42.00\ncategory=Documents";
    public string SampleAiFields { get; set; } = string.Empty;
    public string PreviewText { get; private set; } = "Select Preview recipe to evaluate deterministic sample metadata.";
    public string TransferJson { get; set; } = string.Empty;
    public WorkflowImportConflictPolicy ImportConflictPolicy { get; set; } =
        WorkflowImportConflictPolicy.ImportAsCopy;

    public async Task RefreshAsync()
    {
        if (_library is null)
        {
            StatusText = "Workflow services are unavailable in this preview context.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            await _library.InitializeAsync(CancellationToken.None);
            _allProfiles = await _library.ListProfilesAsync(true, CancellationToken.None);
            _allRecipes = await _library.ListRecipesAsync(true, CancellationToken.None);
            ApplyFilters();
            if (_library.RecoveryMessage is not null)
            {
                StatusText = $"{_library.RecoveryMessage} The original data was preserved.";
            }
            else
            {
                StatusText = $"{_allProfiles.Count} profile(s) and {_allRecipes.Count} recipe(s) loaded.";
            }
        });
    }

    private void ApplyFilters()
    {
        var selectedProfileId = SelectedProfile?.Id;
        var selectedRecipeId = SelectedRecipe?.Id;
        var search = SearchText.Trim();
        var fileType = NormalizeExtension(FileTypeFilter);
        var profiles = _allProfiles.Where(profile =>
            (ShowArchived || !profile.IsArchived) &&
            (!OnlyBuiltIn || profile.IsBuiltIn) &&
            (!OnlyUserCreated || !profile.IsBuiltIn) &&
            (!OnlyAiEnabled || profile.Ai.Enabled) &&
            (!OnlyOcrEnabled || profile.Extraction.OcrEnabled) &&
            (!OnlyDuplicateEnabled || profile.Analysis.DuplicateAnalysisEnabled) &&
            (search.Length == 0 ||
             profile.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             profile.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) &&
            (fileType.Length == 0 ||
             profile.Files.IncludedFileTypes.Count == 0 ||
             profile.Files.IncludedFileTypes.Contains(fileType, StringComparer.OrdinalIgnoreCase)));
        _profiles.Clear();
        foreach (var profile in profiles)
        {
            _profiles.Add(new WorkflowProfileRow(profile));
        }

        _recipes.Clear();
        foreach (var recipe in _allRecipes.Where(recipe =>
                     (ShowArchived || !recipe.IsArchived) &&
                     (!OnlyBuiltIn || recipe.IsBuiltIn) &&
                     (!OnlyUserCreated || !recipe.IsBuiltIn) &&
                     (search.Length == 0 ||
                      recipe.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                      recipe.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) == true) &&
                     (fileType.Length == 0 ||
                      recipe.Applicability.IncludedFileTypes.Count == 0 ||
                      recipe.Applicability.IncludedFileTypes.Contains(fileType, StringComparer.OrdinalIgnoreCase))))
        {
            _recipes.Add(new SortingRecipeRow(recipe));
        }

        SelectedProfile = _profiles.FirstOrDefault(row => row.Id == selectedProfileId) ?? _profiles.FirstOrDefault();
        SelectedRecipe = _recipes.FirstOrDefault(row => row.Id == selectedRecipeId) ?? _recipes.FirstOrDefault();
    }

    private void NewProfile()
    {
        SelectedProfile = null;
        LoadProfileEditor(BuiltInWorkflowLibrary.Profiles[0] with
        {
            Id = string.Empty,
            Name = "New workflow profile",
            Description = null,
            IsBuiltIn = false,
        });
        StatusText = "Configure the structured sections, then save the new profile.";
    }

    private async Task DuplicateProfileAsync()
    {
        if (_library is null || SelectedProfile is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var duplicate = await _library.DuplicateProfileAsync(
                SelectedProfile.Id,
                UniqueCopyName(SelectedProfile.Name, _allProfiles.Select(profile => profile.Name)),
                CancellationToken.None);
            LibraryChanged?.Invoke(this, duplicate.Id);
            await RefreshCoreAsync(duplicate.Id, null);
            StatusText = "A user-editable profile copy was created.";
        });
    }

    private async Task SaveProfileAsync()
    {
        if (_library is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var source = SelectedProfile?.Profile ?? BuiltInWorkflowLibrary.Profiles[0] with
            {
                Id = string.Empty,
                IsBuiltIn = false,
                Origin = new WorkflowProfileOrigin(WorkflowOriginKind.UserCreated),
            };
            if (SelectedProfile?.Profile.IsBuiltIn == true)
            {
                throw new InvalidOperationException("Duplicate a built-in profile before editing it.");
            }

            var edited = source with
            {
                Id = string.IsNullOrWhiteSpace(source.Id) ? $"profile:{Guid.NewGuid():N}" : source.Id,
                Name = ProfileName,
                Description = ProfileDescription,
                Files = source.Files with
                {
                    IncludedFileTypes = ParseExtensions(IncludedFileTypes),
                    ExcludedFileTypes = ParseExtensions(ExcludedFileTypes),
                    MaximumFileSizeBytes = checked((long)(MaximumFileSizeMiB * 1024 * 1024)),
                    IncludeHiddenFiles = IncludeHiddenFiles,
                },
                Extraction = source.Extraction with
                {
                    MetadataEnabled = MetadataEnabled,
                    TextEnabled = TextEnabled,
                    OcrEnabled = OcrEnabled,
                    OcrOnlyWhenTextUnavailable = OcrOnlyWhenTextUnavailable,
                    OcrLanguage = OcrLanguage,
                    MaximumPagesPerDocument = MaximumPages,
                },
                Analysis = source.Analysis with
                {
                    DuplicateAnalysisEnabled = DuplicateAnalysisEnabled,
                    ClassificationEnabled = ClassificationEnabled,
                    RuleEvaluationEnabled = RuleEvaluationEnabled,
                },
                Ai = source.Ai with
                {
                    Enabled = ProfileAiEnabled,
                    InvocationPolicy = ProfileAiEnabled ? AiPolicy : WorkflowAiInvocationPolicy.Disabled,
                    SelectedFileTypes = ParseExtensions(ProfileAiFileTypes),
                },
                SortingRecipeIds = SelectedProfileRecipeIds(),
                UncertaintyPolicy = ProfileUncertaintyPolicy,
                ChangePlans = source.ChangePlans with
                {
                    GenerateChangePlans = GenerateChangePlans,
                    PermitRenameProposals = PermitRenameProposals,
                    PermitMoveProposals = PermitMoveProposals,
                    PermitDirectoryProposals = PermitDirectoryProposals,
                },
                Notifications = source.Notifications with
                {
                    NotifyWhenComplete = NotifyWhenComplete,
                    NotifyWhenPlanReady = NotifyWhenPlanReady,
                    NotifyOnErrors = NotifyOnErrors,
                },
                IncrementalScan = source.IncrementalScan with
                {
                    Enabled = IncrementalScanEnabled,
                    ReanalyseChangedContentOnly = ReanalyseChangedContentOnly,
                    ReconcileMissingItems = ReconcileMissingItems,
                    PreserveUnchangedAnalysis = PreserveUnchangedAnalysis,
                },
                FullScan = source.FullScan with { Enabled = FullScanEnabled },
            };
            var saved = SelectedProfile is null
                ? await _library.CreateProfileAsync(edited, CancellationToken.None)
                : await _library.UpdateProfileAsync(edited, CancellationToken.None);
            LibraryChanged?.Invoke(this, saved.Id);
            await RefreshCoreAsync(saved.Id, null);
            StatusText = $"Workflow profile revision {saved.Revision} saved after validation.";
        });
    }

    private async Task ToggleProfileArchiveAsync()
    {
        if (_library is null || SelectedProfile is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var updated = await _library.SetProfileArchivedAsync(
                SelectedProfile.Id,
                !SelectedProfile.Profile.IsArchived,
                CancellationToken.None);
            LibraryChanged?.Invoke(this, updated.Id);
            await RefreshCoreAsync(updated.Id, null);
        });
    }

    private async Task ToggleProfileEnabledAsync()
    {
        if (_library is null || SelectedProfile is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var updated = await _library.SetProfileEnabledAsync(
                SelectedProfile.Id,
                !SelectedProfile.Profile.IsEnabled,
                CancellationToken.None);
            LibraryChanged?.Invoke(this, updated.Id);
            await RefreshCoreAsync(updated.Id, null);
        });
    }

    private void NewRecipe()
    {
        SelectedRecipe = null;
        LoadRecipeEditor(BuiltInWorkflowLibrary.Recipes[0] with
        {
            Id = string.Empty,
            Name = "New sorting recipe",
            Description = null,
            IsBuiltIn = false,
        });
        StatusText = "Configure declarative conditions and templates, then preview before saving.";
    }

    private async Task DuplicateRecipeAsync()
    {
        if (_library is null || SelectedRecipe is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var duplicate = await _library.DuplicateRecipeAsync(
                SelectedRecipe.Id,
                UniqueCopyName(SelectedRecipe.Name, _allRecipes.Select(recipe => recipe.Name)),
                CancellationToken.None);
            LibraryChanged?.Invoke(this, duplicate.Id);
            await RefreshCoreAsync(null, duplicate.Id);
            StatusText = "A user-editable recipe copy was created.";
        });
    }

    private async Task SaveRecipeAsync()
    {
        if (_library is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var source = SelectedRecipe?.Recipe ?? BuiltInWorkflowLibrary.Recipes[0] with
            {
                Id = string.Empty,
                IsBuiltIn = false,
                Origin = new WorkflowProfileOrigin(WorkflowOriginKind.UserCreated),
            };
            if (SelectedRecipe?.Recipe.IsBuiltIn == true)
            {
                throw new InvalidOperationException("Duplicate a built-in recipe before editing it.");
            }

            var edited = source with
            {
                Id = string.IsNullOrWhiteSpace(source.Id) ? $"recipe:{Guid.NewGuid():N}" : source.Id,
                Name = RecipeName,
                Description = RecipeDescription,
                Priority = RecipePriority,
                Applicability = source.Applicability with
                {
                    IncludedFileTypes = ParseExtensions(RecipeFileTypes),
                    Categories = ParseCategories(RecipeCategories),
                    MinimumFileSizeBytes = ToOptionalBytes(RecipeMinimumFileSizeMiB),
                    MaximumFileSizeBytes = ToOptionalBytes(RecipeMaximumFileSizeMiB),
                },
                NamingTemplate = NamingTemplate,
                DestinationTemplate = DestinationTemplate,
                RequiredFields = ParseIds(RequiredFields),
                OptionalFields = ParseIds(OptionalFields),
                FallbackValues = ParseFallbacks(FallbackValues),
                Normalization = source.Normalization with
                {
                    CasePolicy = CasePolicy,
                    InvalidCharacterPolicy = InvalidCharacterPolicy,
                    MissingValuePolicy = MissingValuePolicy,
                    CollapseWhitespace = CollapseWhitespace,
                    NormalizeUnicode = NormalizeUnicode,
                },
                DefaultDateFormat = DefaultDateFormat,
                CollisionPolicy = CollisionPolicy,
                FileNamePortability = FileNamePortability,
                UncertaintyPolicy = RecipeUncertaintyPolicy,
                MaximumFileNameLength = MaximumFileNameLength,
                PreserveExtension = PreserveExtension,
            };
            var saved = SelectedRecipe is null
                ? await _library.CreateRecipeAsync(edited, CancellationToken.None)
                : await _library.UpdateRecipeAsync(edited, CancellationToken.None);
            LibraryChanged?.Invoke(this, saved.Id);
            await RefreshCoreAsync(null, saved.Id);
            StatusText = $"Sorting recipe revision {saved.Revision} saved after validation.";
        });
    }

    private async Task ToggleRecipeArchiveAsync()
    {
        if (_library is null || SelectedRecipe is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var updated = await _library.SetRecipeArchivedAsync(
                SelectedRecipe.Id,
                !SelectedRecipe.Recipe.IsArchived,
                CancellationToken.None);
            LibraryChanged?.Invoke(this, updated.Id);
            await RefreshCoreAsync(null, updated.Id);
        });
    }

    private void PreviewRecipe()
    {
        if (_templateEngine is null)
        {
            return;
        }

        try
        {
            var source = SelectedRecipe?.Recipe ?? BuiltInWorkflowLibrary.Recipes[0];
            var recipe = source with
            {
                NamingTemplate = NamingTemplate,
                DestinationTemplate = DestinationTemplate,
                RequiredFields = ParseIds(RequiredFields),
                OptionalFields = ParseIds(OptionalFields),
                FallbackValues = ParseFallbacks(FallbackValues),
                Normalization = source.Normalization with
                {
                    CasePolicy = CasePolicy,
                    InvalidCharacterPolicy = InvalidCharacterPolicy,
                    MissingValuePolicy = MissingValuePolicy,
                    CollapseWhitespace = CollapseWhitespace,
                    NormalizeUnicode = NormalizeUnicode,
                },
                DefaultDateFormat = DefaultDateFormat,
                CollisionPolicy = CollisionPolicy,
                FileNamePortability = FileNamePortability,
                UncertaintyPolicy = RecipeUncertaintyPolicy,
                MaximumFileNameLength = MaximumFileNameLength,
                PreserveExtension = PreserveExtension,
            };
            var aiFields = ParseIds(SampleAiFields).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var values = ParseFallbacks(SampleValues).ToDictionary(
                pair => pair.Key,
                pair => new RecipeFieldValue(
                    pair.Value,
                    aiFields.Contains(pair.Key)
                        ? "explicitly approved AI-derived preview metadata"
                        : "representative deterministic preview metadata",
                    aiFields.Contains(pair.Key)),
                StringComparer.OrdinalIgnoreCase);
            var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(SamplePath))!)
                       ?? Path.GetPathRoot(Path.GetFullPath(SamplePath))!;
            var result = _templateEngine.Evaluate(
                recipe,
                new RecipeEvaluationContext(root, Path.GetFullPath(SamplePath), values));
            PreviewText = string.Join(
                Environment.NewLine,
                $"Original: {result.OriginalPath}",
                $"Filename: {result.ProposedFileName ?? "Unavailable"}",
                $"Destination: {result.ProposedDestinationPath ?? "Unavailable"}",
                $"Values: {string.Join(", ", result.ValuesUsed.Select(pair => $"{pair.Key}={pair.Value}"))}",
                $"Missing: {string.Join(", ", result.MissingValues)}",
                $"Fallbacks: {string.Join(", ", result.FallbackValues)}",
                $"Sanitization: {string.Join(" ", result.SanitizationChanges)}",
                $"Conflicts: {string.Join(" ", result.Conflicts)}",
                $"Warnings: {string.Join(" ", result.Warnings)}",
                $"AI-derived values required: {result.RequiresAiDerivedValues}");
            OnPropertyChanged(nameof(PreviewText));
            StatusText = result.IsValid
                ? "Recipe preview completed without touching the filesystem."
                : "Recipe preview is invalid and cannot become an executable proposal.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or IOException)
        {
            PreviewText = exception.Message;
            OnPropertyChanged(nameof(PreviewText));
            StatusText = "Recipe preview failed safely.";
        }
    }

    private async Task ExportProfileAsync()
    {
        if (_importExport is null || SelectedProfile is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            TransferJson = await _importExport.ExportProfileAsync(SelectedProfile.Id, CancellationToken.None);
            OnPropertyChanged(nameof(TransferJson));
            StatusText = "Profile exported to human-inspectable JSON without provider settings or secrets.";
        });
    }

    private async Task ExportRecipeAsync()
    {
        if (_importExport is null || SelectedRecipe is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            TransferJson = await _importExport.ExportRecipeAsync(SelectedRecipe.Id, CancellationToken.None);
            OnPropertyChanged(nameof(TransferJson));
            StatusText = "Recipe exported to declarative JSON. No executable code is included.";
        });
    }

    private async Task ImportAsync()
    {
        if (_importExport is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _importExport.ImportAsync(
                TransferJson,
                ImportConflictPolicy,
                CancellationToken.None);
            StatusText = result.Message;
            if (result.Imported && result.ImportedId is not null)
            {
                LibraryChanged?.Invoke(this, result.ImportedId);
                await RefreshCoreAsync(result.ImportedId, result.ImportedId);
            }
        });
    }

    private async Task DeleteSelectedAsync()
    {
        if (_library is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            string? deletedId = null;
            if (_deleteRecipe && SelectedRecipe is not null && !SelectedRecipe.Recipe.IsBuiltIn)
            {
                deletedId = SelectedRecipe.Id;
                await _library.DeleteRecipeAsync(deletedId, CancellationToken.None);
            }
            else if (SelectedProfile is not null && !SelectedProfile.Profile.IsBuiltIn)
            {
                deletedId = SelectedProfile.Id;
                await _library.DeleteProfileAsync(deletedId, CancellationToken.None);
            }
            else
            {
                throw new InvalidOperationException("Canonical built-ins cannot be deleted.");
            }

            DeleteConfirmationPending = false;
            LibraryChanged?.Invoke(this, deletedId);
            await RefreshCoreAsync(null, null);
            StatusText = "The unreferenced user-created item was deleted. No user file was changed.";
        });
    }

    private void ExportDiagnostics()
    {
        if (_library is null)
        {
            return;
        }

        TransferJson = JsonSerializer.Serialize(
            new
            {
                Type = "OpenSorSeWorkflowDiagnostics",
                SchemaVersion = 1,
                ApplicationVersion = ApplicationVersionInfo.Current,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                _library.RecoveryMessage,
                _library.PreservedCorruptCopyPath,
                Diagnostics = _library.GetDiagnostics(),
            },
            new JsonSerializerOptions { WriteIndented = true });
        OnPropertyChanged(nameof(TransferJson));
        StatusText = "Workflow diagnostics exported without document contents, provider settings, or secrets.";
    }

    private async Task LoadProfileUsageAsync(WorkflowProfileRow row)
    {
        if (_library is null)
        {
            return;
        }

        var usage = await _library.GetUsageAsync(row.Id, CancellationToken.None);
        var index = _profiles.IndexOf(row);
        if (index >= 0)
        {
            _profiles[index] = row with { Usage = usage };
        }
    }

    private async Task LoadRecipeUsageAsync(SortingRecipeRow row)
    {
        if (_library is null)
        {
            return;
        }

        var usage = await _library.GetUsageAsync(row.Id, CancellationToken.None);
        var index = _recipes.IndexOf(row);
        if (index >= 0)
        {
            _recipes[index] = row with { Usage = usage };
        }
    }

    private async Task RefreshCoreAsync(string? profileId, string? recipeId)
    {
        _allProfiles = await _library!.ListProfilesAsync(true, CancellationToken.None);
        _allRecipes = await _library.ListRecipesAsync(true, CancellationToken.None);
        ApplyFilters();
        SelectedProfile = _profiles.FirstOrDefault(row => row.Id == profileId) ?? SelectedProfile;
        SelectedRecipe = _recipes.FirstOrDefault(row => row.Id == recipeId) ?? SelectedRecipe;
    }

    private void LoadProfileEditor(WorkflowProfile profile)
    {
        ProfileName = profile.Name;
        ProfileDescription = profile.Description ?? string.Empty;
        IncludedFileTypes = string.Join(", ", profile.Files.IncludedFileTypes);
        ExcludedFileTypes = string.Join(", ", profile.Files.ExcludedFileTypes);
        MaximumFileSizeMiB = profile.Files.MaximumFileSizeBytes / (1024d * 1024d);
        IncludeHiddenFiles = profile.Files.IncludeHiddenFiles;
        MetadataEnabled = profile.Extraction.MetadataEnabled;
        TextEnabled = profile.Extraction.TextEnabled;
        OcrEnabled = profile.Extraction.OcrEnabled;
        OcrOnlyWhenTextUnavailable = profile.Extraction.OcrOnlyWhenTextUnavailable;
        OcrLanguage = profile.Extraction.OcrLanguage;
        MaximumPages = profile.Extraction.MaximumPagesPerDocument;
        DuplicateAnalysisEnabled = profile.Analysis.DuplicateAnalysisEnabled;
        ClassificationEnabled = profile.Analysis.ClassificationEnabled;
        RuleEvaluationEnabled = profile.Analysis.RuleEvaluationEnabled;
        ProfileAiEnabled = profile.Ai.Enabled;
        AiPolicy = profile.Ai.InvocationPolicy;
        ProfileAiFileTypes = string.Join(", ", profile.Ai.SelectedFileTypes);
        SelectedRecipeIds = string.Join(", ", profile.SortingRecipeIds);
        RebuildProfileRecipeChoices(profile.SortingRecipeIds);
        ProfileUncertaintyPolicy = profile.UncertaintyPolicy;
        GenerateChangePlans = profile.ChangePlans.GenerateChangePlans;
        PermitRenameProposals = profile.ChangePlans.PermitRenameProposals;
        PermitMoveProposals = profile.ChangePlans.PermitMoveProposals;
        PermitDirectoryProposals = profile.ChangePlans.PermitDirectoryProposals;
        NotifyWhenComplete = profile.Notifications.NotifyWhenComplete;
        NotifyWhenPlanReady = profile.Notifications.NotifyWhenPlanReady;
        NotifyOnErrors = profile.Notifications.NotifyOnErrors;
        IncrementalScanEnabled = profile.IncrementalScan.Enabled;
        ReanalyseChangedContentOnly = profile.IncrementalScan.ReanalyseChangedContentOnly;
        ReconcileMissingItems = profile.IncrementalScan.ReconcileMissingItems;
        PreserveUnchangedAnalysis = profile.IncrementalScan.PreserveUnchangedAnalysis;
        FullScanEnabled = profile.FullScan.Enabled;
        NotifyEditorProperties();
    }

    private void LoadRecipeEditor(SortingRecipe recipe)
    {
        RecipeName = recipe.Name;
        RecipeDescription = recipe.Description ?? string.Empty;
        RecipeFileTypes = string.Join(", ", recipe.Applicability.IncludedFileTypes);
        RecipeCategories = string.Join(", ", recipe.Applicability.Categories);
        RecipeMinimumFileSizeMiB = ToMiB(recipe.Applicability.MinimumFileSizeBytes);
        RecipeMaximumFileSizeMiB = ToMiB(recipe.Applicability.MaximumFileSizeBytes);
        RecipePriority = recipe.Priority;
        NamingTemplate = recipe.NamingTemplate;
        DestinationTemplate = recipe.DestinationTemplate;
        RequiredFields = string.Join(", ", recipe.RequiredFields);
        OptionalFields = string.Join(", ", recipe.OptionalFields);
        FallbackValues = string.Join(Environment.NewLine, recipe.FallbackValues.Select(pair => $"{pair.Key}={pair.Value}"));
        CasePolicy = recipe.Normalization.CasePolicy;
        InvalidCharacterPolicy = recipe.Normalization.InvalidCharacterPolicy;
        MissingValuePolicy = recipe.Normalization.MissingValuePolicy;
        CollapseWhitespace = recipe.Normalization.CollapseWhitespace;
        NormalizeUnicode = recipe.Normalization.NormalizeUnicode;
        DefaultDateFormat = recipe.DefaultDateFormat;
        CollisionPolicy = recipe.CollisionPolicy;
        FileNamePortability = recipe.FileNamePortability;
        RecipeUncertaintyPolicy = recipe.UncertaintyPolicy;
        MaximumFileNameLength = recipe.MaximumFileNameLength;
        PreserveExtension = recipe.PreserveExtension;
        NotifyEditorProperties();
    }

    private void NotifyEditorProperties()
    {
        foreach (var property in new[]
                 {
                     nameof(ProfileName), nameof(ProfileDescription), nameof(IncludedFileTypes),
                     nameof(ExcludedFileTypes), nameof(MaximumFileSizeMiB), nameof(MetadataEnabled),
                     nameof(IncludeHiddenFiles), nameof(TextEnabled), nameof(OcrEnabled),
                     nameof(OcrOnlyWhenTextUnavailable), nameof(OcrLanguage), nameof(MaximumPages),
                     nameof(DuplicateAnalysisEnabled), nameof(ClassificationEnabled),
                     nameof(RuleEvaluationEnabled), nameof(ProfileAiEnabled), nameof(AiPolicy),
                     nameof(ProfileAiFileTypes), nameof(SelectedRecipeIds), nameof(ProfileUncertaintyPolicy),
                     nameof(GenerateChangePlans), nameof(PermitRenameProposals), nameof(PermitMoveProposals),
                     nameof(PermitDirectoryProposals), nameof(NotifyWhenComplete), nameof(NotifyWhenPlanReady),
                     nameof(NotifyOnErrors), nameof(IncrementalScanEnabled),
                     nameof(ReanalyseChangedContentOnly), nameof(ReconcileMissingItems),
                     nameof(PreserveUnchangedAnalysis), nameof(FullScanEnabled), nameof(RecipeName),
                     nameof(RecipeDescription), nameof(RecipeFileTypes), nameof(RecipeCategories),
                     nameof(RecipeMinimumFileSizeMiB), nameof(RecipeMaximumFileSizeMiB), nameof(RecipePriority),
                     nameof(NamingTemplate), nameof(DestinationTemplate), nameof(RequiredFields),
                     nameof(OptionalFields), nameof(FallbackValues), nameof(CasePolicy),
                     nameof(InvalidCharacterPolicy), nameof(MissingValuePolicy),
                     nameof(CollisionPolicy), nameof(FileNamePortability),
                     nameof(RecipeUncertaintyPolicy), nameof(DefaultDateFormat),
                     nameof(CollapseWhitespace), nameof(NormalizeUnicode),
                     nameof(MaximumFileNameLength), nameof(PreserveExtension),
                 })
        {
            OnPropertyChanged(property);
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUseSelectedProfile() => !IsBusy && SelectedProfile is not null && _library is not null;
    private bool CanEditSelectedProfile() =>
        CanUseSelectedProfile() && !SelectedProfile!.Profile.IsBuiltIn;
    private bool CanUseSelectedRecipe() => !IsBusy && SelectedRecipe is not null && _library is not null;
    private bool CanEditSelectedRecipe() =>
        CanUseSelectedRecipe() && !SelectedRecipe!.Recipe.IsBuiltIn;
    private bool CanDeleteProfile() =>
        !IsBusy && SelectedProfile is { Profile.IsBuiltIn: false };
    private bool CanDeleteRecipe() =>
        !IsBusy && SelectedRecipe is { Recipe.IsBuiltIn: false };

    private void RefreshCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        DuplicateProfileCommand.NotifyCanExecuteChanged();
        SaveProfileCommand.NotifyCanExecuteChanged();
        ToggleProfileArchiveCommand.NotifyCanExecuteChanged();
        ToggleProfileEnabledCommand.NotifyCanExecuteChanged();
        DuplicateRecipeCommand.NotifyCanExecuteChanged();
        SaveRecipeCommand.NotifyCanExecuteChanged();
        ToggleRecipeArchiveCommand.NotifyCanExecuteChanged();
        PreviewRecipeCommand.NotifyCanExecuteChanged();
        ExportProfileCommand.NotifyCanExecuteChanged();
        ExportRecipeCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        ExportDiagnosticsCommand.NotifyCanExecuteChanged();
        RequestDeleteProfileCommand.NotifyCanExecuteChanged();
        RequestDeleteRecipeCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCommand.NotifyCanExecuteChanged();
        AssignToWatchedFolderCommand.NotifyCanExecuteChanged();
        RunScanCommand.NotifyCanExecuteChanged();
    }

    private static IReadOnlyList<string> ParseIds(string value) =>
        Array.AsReadOnly((value ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray());

    private static IReadOnlyList<string> ParseExtensions(string value) =>
        Array.AsReadOnly(ParseIds(value)
            .Select(NormalizeExtension)
            .Where(extension => extension.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());

    private IReadOnlyList<string> SelectedProfileRecipeIds() =>
        _profileRecipeChoices.Count == 0
            ? ParseIds(SelectedRecipeIds)
            : Array.AsReadOnly(_profileRecipeChoices
                .Where(choice => choice.IsSelected)
                .Select(choice => choice.Id)
                .ToArray());

    private void RebuildProfileRecipeChoices(IReadOnlyList<string> selectedIds)
    {
        _profileRecipeChoices.Clear();
        foreach (var recipe in _allRecipes.Where(recipe => !recipe.IsArchived && recipe.IsEnabled))
        {
            _profileRecipeChoices.Add(new WorkflowRecipeSelectionRow(
                recipe,
                selectedIds.Contains(recipe.Id, StringComparer.Ordinal)));
        }
    }

    private static IReadOnlyList<FileCategory> ParseCategories(string value)
    {
        var categories = new List<FileCategory>();
        foreach (var token in ParseIds(value))
        {
            if (!Enum.TryParse<FileCategory>(token, true, out var category) ||
                !Enum.IsDefined(category))
            {
                throw new ArgumentException($"Recipe category \"{token}\" is unsupported.");
            }

            categories.Add(category);
        }

        return Array.AsReadOnly(categories.Distinct().ToArray());
    }

    private static long? ToOptionalBytes(double mebibytes) =>
        mebibytes <= 0 ? null : checked((long)(mebibytes * 1024 * 1024));

    private static double ToMiB(long? bytes) => bytes is null ? 0 : bytes.Value / (1024d * 1024d);

    private static IReadOnlyDictionary<string, string> ParseFallbacks(string value) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            (value ?? string.Empty)
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last()[1], StringComparer.OrdinalIgnoreCase));

    private static string NormalizeExtension(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : $".{normalized.TrimStart('.').ToLowerInvariant()}";
    }

    private static string UniqueCopyName(string name, IEnumerable<string> existing)
    {
        var names = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < 1000; index++)
        {
            var suffix = index == 1 ? " Copy" : $" Copy {index}";
            var candidate = name[..Math.Min(name.Length, WorkflowLibraryLimits.MaximumNameLength - suffix.Length)] + suffix;
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("A unique profile or recipe copy name could not be generated.");
    }
}
