using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Executor.Models;

#pragma warning disable CS1591

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Identifies where one explicit bounded organization selection was captured.</summary>
public enum OrganizationSelectionOrigin
{
    /// <summary>The selection came from completed-scan Files.</summary>
    Files,
    /// <summary>The selection came from Search.</summary>
    Search,
    /// <summary>The selection came from the current evaluated Saved View.</summary>
    SavedView,
}

/// <summary>Captures stable selected identities without turning a Saved View into a mutation rule.</summary>
public sealed record OrganizationSelectionContext(
    OrganizationSelectionOrigin Origin,
    string DisplayName,
    IReadOnlyList<string> FileIds,
    DiscoveryWorkflowContext? DiscoveryContext = null);

/// <summary>Identifies which constrained recipe pattern receives a selected token.</summary>
public enum OrganizationTokenTarget
{
    /// <summary>Insert into the naming pattern.</summary>
    NamingPattern,
    /// <summary>Insert into the relative destination pattern.</summary>
    DestinationPattern,
}

/// <summary>Identifies the bounded preview rows shown to the user.</summary>
public enum OrganizationPreviewFilter
{
    /// <summary>Show conflict and missing rows first, followed by all other rows.</summary>
    All,
    /// <summary>Show rows that cannot produce a plan action.</summary>
    CannotPropose,
    /// <summary>Show rows that used a fallback or warning.</summary>
    NeedsReview,
    /// <summary>Show rows backed completely by trusted evidence.</summary>
    Reliable,
}

/// <summary>Presents one existing Sorting Recipe as an Organization recipe.</summary>
public sealed record OrganizationRecipeRow(SortingRecipe Model)
{
    /// <summary>Gets the stable recipe identity.</summary>
    public string Id => Model.Id;
    /// <summary>Gets the display name.</summary>
    public string Name => Model.Name;
    /// <summary>Gets a bounded description.</summary>
    public string Description => Model.Description ?? "Deterministic organization recipe.";
    /// <summary>Gets whether OmniSorSe supplied this example.</summary>
    public bool IsBuiltIn => Model.IsBuiltIn;
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>Projects one compact, accessible organization preview row.</summary>
public sealed record OrganizationProposalRowViewModel(OrganizationProposalRow Model)
{
    public string FileName => Path.GetFileName(Model.CurrentPath);
    public string CurrentPath => Model.CurrentPath;
    public string TargetPath => Model.TargetPath ?? "No safe target available";
    public string Readiness => Model.Readiness switch
    {
        OrganizationProposalReadiness.Reliable => "Reliable",
        OrganizationProposalReadiness.NeedsReview => "Needs review",
        _ => "Cannot propose",
    };
    public string EvidenceText => Model.Evidence.Count == 0
        ? "No trusted recipe evidence was used."
        : string.Join("; ", Model.Evidence.Take(3).Select(item =>
            $"{item.Token} = {item.Value} ({item.EvidenceSource})"));
    public string DetailText => string.Join(" ", Model.Conflicts
        .Concat(Model.MissingEvidence.Select(value => $"Missing {value}."))
        .Concat(Model.Fallbacks.Select(value => $"Fallback used for {value}."))
        .Concat(Model.Warnings)
        .Distinct(StringComparer.Ordinal)
        .Take(4));
    public string AccessibleName =>
        $"{FileName}. {Readiness}. Current path {CurrentPath}. Proposed path {TargetPath}. {DetailText}";
}

/// <summary>
/// Connects explicit stable-ID selections to existing recipes, ephemeral preview, and the existing Change Plan.
/// </summary>
public sealed class ReviewedOrganizationViewModel : ViewModelBase, IDisposable
{
    private readonly IReviewedOrganizationService? _organization;
    private readonly IWorkflowLibraryService? _recipes;
    private readonly ObservableCollection<OrganizationRecipeRow> _availableRecipes = [];
    private readonly ObservableCollection<OrganizationProposalRowViewModel> _visibleRows = [];
    private CancellationTokenSource? _previewCancellation;
    private long _previewVersion;
    private OrganizationSelectionContext? _selection;
    private OrganizationRecipeRow? _selectedRecipe;
    private OrganizationProposalSet? _proposal;
    private string _namingPattern = string.Empty;
    private string _destinationPattern = string.Empty;
    private OrganizationTokenTarget _tokenTarget;
    private OrganizationPreviewFilter _previewFilter;
    private string _statusText = "Select indexed files, then choose Organize.";
    private bool _isBusy;
    private bool _isVisible;

    /// <summary>Creates a design-time instance without persistence or mutation services.</summary>
    public ReviewedOrganizationViewModel()
        : this(null, null)
    {
    }

    /// <summary>Creates the reviewed Organization recipe workflow.</summary>
    public ReviewedOrganizationViewModel(
        IReviewedOrganizationService? organization,
        IWorkflowLibraryService? recipes)
    {
        _organization = organization;
        _recipes = recipes;
        AvailableRecipes = new ReadOnlyObservableCollection<OrganizationRecipeRow>(_availableRecipes);
        VisibleRows = new ReadOnlyObservableCollection<OrganizationProposalRowViewModel>(_visibleRows);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, CanPreview);
        ReviewChangesCommand = new AsyncRelayCommand(CreateChangePlanAsync, CanReviewChanges);
        CancelPreviewCommand = new RelayCommand(CancelPreview, () => IsBusy);
        RemoveUnreadyCommand = new AsyncRelayCommand(RemoveUnreadyAsync, CanRemoveUnready);
        InsertTokenCommand = new RelayCommand<OrganizationRecipeToken>(InsertToken, token => token is not null && !IsBusy);
        ManageRecipesCommand = new RelayCommand(
            () => ManageRecipesRequested?.Invoke(this, EventArgs.Empty),
            () => !IsBusy);
        CloseCommand = new RelayCommand(Close, () => !IsBusy);
    }

    public event EventHandler<ChangePlan>? ChangePlanCreated;
    public event EventHandler? ManageRecipesRequested;

    public ReadOnlyObservableCollection<OrganizationRecipeRow> AvailableRecipes { get; }
    public ReadOnlyObservableCollection<OrganizationProposalRowViewModel> VisibleRows { get; }
    public IReadOnlyList<OrganizationRecipeToken> AvailableTokens => OrganizationRecipeTokenCatalog.Tokens;
    public IReadOnlyList<OrganizationTokenTarget> TokenTargets { get; } = Enum.GetValues<OrganizationTokenTarget>();
    public IReadOnlyList<OrganizationPreviewFilter> PreviewFilters { get; } = Enum.GetValues<OrganizationPreviewFilter>();

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    public OrganizationRecipeRow? SelectedRecipe
    {
        get => _selectedRecipe;
        set
        {
            if (SetProperty(ref _selectedRecipe, value))
            {
                NamingPattern = value?.Model.NamingTemplate ?? string.Empty;
                DestinationPattern = value?.Model.DestinationTemplate ?? string.Empty;
                InvalidateProposal("Recipe changed. Preview the selected files again.");
            }
        }
    }

    public string NamingPattern
    {
        get => _namingPattern;
        set
        {
            if (SetProperty(ref _namingPattern, value ?? string.Empty))
            {
                InvalidateProposal("Naming pattern changed. Preview again before Review Changes.");
            }
        }
    }

    public string DestinationPattern
    {
        get => _destinationPattern;
        set
        {
            if (SetProperty(ref _destinationPattern, value ?? string.Empty))
            {
                InvalidateProposal("Destination pattern changed. Preview again before Review Changes.");
            }
        }
    }

    public OrganizationTokenTarget TokenTarget
    {
        get => _tokenTarget;
        set => SetProperty(ref _tokenTarget, value);
    }

    public OrganizationPreviewFilter PreviewFilter
    {
        get => _previewFilter;
        set
        {
            if (SetProperty(ref _previewFilter, value))
            {
                RefreshVisibleRows();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public int SelectedCount => _selection?.FileIds.Count ?? 0;
    /// <summary>Gets the explicit bounded durable identities captured for this preview.</summary>
    public IReadOnlyList<string> SelectedFileIds => _selection?.FileIds ?? [];
    public string SelectedCountText => $"{SelectedCount} explicitly selected file{(SelectedCount == 1 ? string.Empty : "s")}";
    public bool HasProposal => _proposal is not null;
    public bool HasSensitivePathWarning => _proposal?.HasSensitivePathEvidence == true;
    public string PrivacyWarningText => HasSensitivePathWarning
        ? "This recipe writes classification information into filenames or folders. Those names may be visible in backups, sync services, shared folders, or attachments."
        : string.Empty;
    public string CoverageText => _proposal is null
        ? "Preview to inspect trusted-evidence coverage."
        : string.Join("; ", _proposal.Coverage.Select(item =>
            $"{item.DisplayName}: {item.AvailableCount} / {item.SelectedCount}"));
    public string ActionCountText => _proposal is null
        ? "No Change Plan actions projected."
        : $"Projected actions: {_proposal.ProjectedFileActionCount} file + {_proposal.ProjectedDirectoryActionCount} folder = {_proposal.ProjectedActionCount} / {OpenSorSe.Executor.Models.ChangePlanSchema.MaximumActions}.";
    public string PreviewRangeText => _proposal is null
        ? "No preview rows."
        : $"Showing {_visibleRows.Count} of {_proposal.Rows.Count} bounded preview rows; conflicts and missing evidence are prioritized.";
    public string RecommendationText => _proposal?.CanCreateChangePlan == true && SelectedRecipe?.IsBuiltIn == true
        ? "This built-in example resolves every required token and has no blocking collision for the current selection."
        : "Coverage is factual; OmniSorSe does not recommend incomplete recipes.";

    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand ReviewChangesCommand { get; }
    public IRelayCommand CancelPreviewCommand { get; }
    public IAsyncRelayCommand RemoveUnreadyCommand { get; }
    public IRelayCommand<OrganizationRecipeToken> InsertTokenCommand { get; }
    public IRelayCommand ManageRecipesCommand { get; }
    public IRelayCommand CloseCommand { get; }

    /// <summary>Starts one explicit bounded organization workflow without resolving paths in the UI.</summary>
    public async Task OpenAsync(OrganizationSelectionContext selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var ids = selection.FileIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(WorkflowLibraryLimits.MaximumOrganizationSelection + 1)
            .ToArray();
        if (ids.Length == 0 || ids.Length > WorkflowLibraryLimits.MaximumOrganizationSelection)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selection),
                $"Select between 1 and {WorkflowLibraryLimits.MaximumOrganizationSelection} files.");
        }

        CancelPreview();
        _selection = selection with { FileIds = Array.AsReadOnly(ids) };
        _proposal = null;
        _visibleRows.Clear();
        IsVisible = true;
        OnProposalChanged();
        await LoadRecipesAsync();
        StatusText = $"Ready to preview {SelectedCount} explicit {selection.DisplayName} selection item{(SelectedCount == 1 ? string.Empty : "s")}. No files have changed.";
    }

    private async Task LoadRecipesAsync()
    {
        _availableRecipes.Clear();
        if (_recipes is null)
        {
            StatusText = "Organization recipes are unavailable in this application mode.";
            return;
        }

        var recipes = await _recipes.ListRecipesAsync(false, CancellationToken.None);
        foreach (var recipe in recipes
                     .Where(recipe => recipe.IsEnabled && !recipe.IsArchived)
                     .OrderByDescending(recipe => recipe.IsBuiltIn)
                     .ThenBy(recipe => recipe.Name, StringComparer.OrdinalIgnoreCase))
        {
            _availableRecipes.Add(new OrganizationRecipeRow(recipe));
        }

        SelectedRecipe = _availableRecipes.FirstOrDefault(item =>
            item.Id == BuiltInWorkflowIds.TrustedClassificationRecipe) ??
            _availableRecipes.FirstOrDefault();
        NotifyCommands();
    }

    private bool CanPreview() =>
        _organization is not null &&
        _selection is not null &&
        SelectedRecipe is not null &&
        !IsBusy &&
        (!string.IsNullOrWhiteSpace(NamingPattern) || !string.IsNullOrWhiteSpace(DestinationPattern));

    private async Task PreviewAsync()
    {
        if (!CanPreview() || _organization is null || _selection is null || SelectedRecipe is null)
        {
            return;
        }

        var version = Interlocked.Increment(ref _previewVersion);
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;
        IsBusy = true;
        StatusText = "Resolving trusted indexed evidence and checking deterministic targets...";
        try
        {
            var recipe = SelectedRecipe.Model with
            {
                NamingTemplate = NamingPattern,
                DestinationTemplate = DestinationPattern,
            };
            var proposal = await _organization.PreviewAsync(
                new OrganizationPreviewRequest(recipe, _selection.FileIds),
                token);
            if (version != Volatile.Read(ref _previewVersion) || token.IsCancellationRequested)
            {
                return;
            }

            _proposal = proposal;
            RefreshVisibleRows();
            StatusText = proposal.CanCreateChangePlan
                ? "Preview is ready. Review provenance and projected actions before continuing."
                : proposal.Warnings.FirstOrDefault() ?? "Preview contains files that cannot be proposed safely. Edit the recipe or reduce the selection.";
            OnProposalChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StatusText = "Organization preview cancelled. No files or Change Plans changed.";
        }
        catch (ArgumentException exception)
        {
            StatusText = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            StatusText = exception.Message;
        }
        finally
        {
            if (version == Volatile.Read(ref _previewVersion))
            {
                IsBusy = false;
            }
        }
    }

    private bool CanReviewChanges() => _organization is not null && _proposal?.CanCreateChangePlan == true && !IsBusy;

    private async Task CreateChangePlanAsync()
    {
        if (!CanReviewChanges() || _organization is null || _proposal is null || _selection is null)
        {
            return;
        }

        var version = Interlocked.Increment(ref _previewVersion);
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;
        IsBusy = true;
        StatusText = "Rechecking the preview before creating the existing Change Plan...";
        try
        {
            var plan = await _organization.CreateChangePlanAsync(
                _proposal,
                $"organization:{_selection.Origin.ToString().ToLowerInvariant()}",
                token);
            if (version != Volatile.Read(ref _previewVersion) || token.IsCancellationRequested)
            {
                return;
            }

            StatusText = "Change Plan created for explicit review. No file operation has run yet.";
            ChangePlanCreated?.Invoke(this, plan);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StatusText = "Organization recheck cancelled. No files or Change Plans changed.";
        }
        catch (ArgumentException exception)
        {
            InvalidateProposal(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            InvalidateProposal(exception.Message);
        }
        finally
        {
            if (version == Volatile.Read(ref _previewVersion))
            {
                IsBusy = false;
            }
        }
    }

    private bool CanRemoveUnready() =>
        _proposal is not null &&
        _proposal.Rows.Any(row => row.Readiness == OrganizationProposalReadiness.CannotPropose) &&
        _proposal.Rows.Any(row => row.Readiness != OrganizationProposalReadiness.CannotPropose) &&
        !IsBusy;

    private async Task RemoveUnreadyAsync()
    {
        if (!CanRemoveUnready() || _proposal is null || _selection is null)
        {
            return;
        }

        var eligible = _proposal.Rows
            .Where(row => row.Readiness != OrganizationProposalReadiness.CannotPropose)
            .Select(row => row.FileId)
            .ToArray();
        _selection = _selection with { FileIds = Array.AsReadOnly(eligible) };
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        InvalidateProposal("Unready files were removed from this explicit snapshot. Preview the reduced selection again.");
        await PreviewAsync();
    }

    private void InsertToken(OrganizationRecipeToken? token)
    {
        if (token is null || IsBusy)
        {
            return;
        }

        if (TokenTarget == OrganizationTokenTarget.NamingPattern)
        {
            NamingPattern += token.Token;
        }
        else
        {
            DestinationPattern += token.Token;
        }
    }

    private void RefreshVisibleRows()
    {
        _visibleRows.Clear();
        if (_proposal is null)
        {
            return;
        }

        var rows = PreviewFilter switch
        {
            OrganizationPreviewFilter.CannotPropose => _proposal.Rows.Where(row => row.Readiness == OrganizationProposalReadiness.CannotPropose),
            OrganizationPreviewFilter.NeedsReview => _proposal.Rows.Where(row => row.Readiness == OrganizationProposalReadiness.NeedsReview),
            OrganizationPreviewFilter.Reliable => _proposal.Rows.Where(row => row.Readiness == OrganizationProposalReadiness.Reliable),
            _ => _proposal.Rows,
        };
        foreach (var row in rows
                     .OrderByDescending(row => row.Readiness == OrganizationProposalReadiness.CannotPropose)
                     .ThenByDescending(row => row.Readiness == OrganizationProposalReadiness.NeedsReview)
                     .ThenBy(row => row.CurrentPath, OpenSorSe.Executor.ChangePlanFactory.PathComparer)
                     .Take(WorkflowLibraryLimits.MaximumOrganizationPreviewRows))
        {
            _visibleRows.Add(new OrganizationProposalRowViewModel(row));
        }

        OnPropertyChanged(nameof(PreviewRangeText));
    }

    private void InvalidateProposal(string status)
    {
        Interlocked.Increment(ref _previewVersion);
        _previewCancellation?.Cancel();
        _proposal = null;
        _visibleRows.Clear();
        StatusText = status;
        OnProposalChanged();
    }

    private void CancelPreview()
    {
        Interlocked.Increment(ref _previewVersion);
        _previewCancellation?.Cancel();
        IsBusy = false;
        StatusText = "Organization preview cancelled. No files or Change Plans changed.";
    }

    private void Close()
    {
        CancelPreview();
        _selection = null;
        _proposal = null;
        _visibleRows.Clear();
        IsVisible = false;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnProposalChanged();
    }

    private void OnProposalChanged()
    {
        OnPropertyChanged(nameof(HasProposal));
        OnPropertyChanged(nameof(HasSensitivePathWarning));
        OnPropertyChanged(nameof(PrivacyWarningText));
        OnPropertyChanged(nameof(CoverageText));
        OnPropertyChanged(nameof(ActionCountText));
        OnPropertyChanged(nameof(PreviewRangeText));
        OnPropertyChanged(nameof(RecommendationText));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        PreviewCommand.NotifyCanExecuteChanged();
        ReviewChangesCommand.NotifyCanExecuteChanged();
        CancelPreviewCommand.NotifyCanExecuteChanged();
        RemoveUnreadyCommand.NotifyCanExecuteChanged();
        InsertTokenCommand.NotifyCanExecuteChanged();
        ManageRecipesCommand.NotifyCanExecuteChanged();
        CloseCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
    }
}
