using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Workflows;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Maintains validated scan-root selection and emits non-executing scan requests.
/// </summary>
public sealed class FolderSelectionViewModel : ViewModelBase
{
    private const int RecentFolderLimit = 5;
    private readonly StringComparer _pathComparer =
        OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparer;
    private readonly ObservableCollection<string> _recentFolders = [];
    private readonly ObservableCollection<string> _selectedFolders = [];
    private readonly ObservableCollection<WorkflowProfile> _workflowProfiles = [];
    private readonly IWorkflowLibraryService? _workflowLibrary;
    private string? _folderPathInput;
    private string? _selectedFolder;
    private string _statusText = "Ready";
    private WorkflowProfile? _selectedWorkflowProfile;
    private bool _useOneTimeOverride;
    private bool _overrideOcrEnabled;
    private bool _overrideDuplicateAnalysisEnabled = true;
    private bool _overrideAiEnabled;
    private double _overrideMaximumFileSizeMiB = 1024;
    private string _newProfileName = string.Empty;

    /// <summary>
    /// Initializes folder-selection commands.
    /// </summary>
    public FolderSelectionViewModel(IWorkflowLibraryService? workflowLibrary = null)
    {
        _workflowLibrary = workflowLibrary;
        SelectedFolders = new ReadOnlyObservableCollection<string>(_selectedFolders);
        RecentFolders = new ReadOnlyObservableCollection<string>(_recentFolders);
        foreach (var profile in BuiltInWorkflowLibrary.Profiles.Where(profile => !profile.IsArchived))
        {
            _workflowProfiles.Add(profile);
        }

        WorkflowProfiles = new ReadOnlyObservableCollection<WorkflowProfile>(_workflowProfiles);
        _selectedWorkflowProfile = _workflowProfiles.FirstOrDefault();
        AddFolderCommand = new RelayCommand(AddFolderFromInput);
        RemoveSelectedFolderCommand = new RelayCommand(() => _ = RemoveSelectedFolder());
        StartScanCommand = new RelayCommand(RequestScan);
        RefreshProfilesCommand = new AsyncRelayCommand(RefreshProfilesAsync);
        SaveAdjustedAsProfileCommand = new AsyncRelayCommand(
            SaveAdjustedAsProfileAsync,
            () => _workflowLibrary is not null &&
                  SelectedWorkflowProfile is not null &&
                  !string.IsNullOrWhiteSpace(NewProfileName));
    }

    /// <summary>
    /// Occurs when the user requests a scan of the current validated folders.
    /// </summary>
    public event EventHandler<ScanRequest>? ScanRequested;

    /// <summary>
    /// Gets the selected folder roots in user selection order.
    /// </summary>
    public ReadOnlyObservableCollection<string> SelectedFolders { get; }

    /// <summary>
    /// Gets recently added folder roots for the current application process.
    /// </summary>
    public ReadOnlyObservableCollection<string> RecentFolders { get; }

    /// <summary>Gets persistent, enabled workflow profiles available to the manual scan.</summary>
    public ReadOnlyObservableCollection<WorkflowProfile> WorkflowProfiles { get; }

    /// <summary>Gets or sets the persistent profile selected for the next manual scan.</summary>
    public WorkflowProfile? SelectedWorkflowProfile
    {
        get => _selectedWorkflowProfile;
        set
        {
            if (SetProperty(ref _selectedWorkflowProfile, value))
            {
                OnPropertyChanged(nameof(ProfileSummary));
                if (value is not null)
                {
                    OverrideOcrEnabled = value.Extraction.OcrEnabled;
                    OverrideDuplicateAnalysisEnabled = value.Analysis.DuplicateAnalysisEnabled;
                    OverrideAiEnabled = value.Ai.Enabled;
                    OverrideMaximumFileSizeMiB = value.Files.MaximumFileSizeBytes / (1024d * 1024d);
                }

                SaveAdjustedAsProfileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets a concise capability and processing-cost summary for the selected profile.</summary>
    public string ProfileSummary => SelectedWorkflowProfile is null
        ? "Select an available workflow profile."
        : $"{FormatTypes(SelectedWorkflowProfile.Files.IncludedFileTypes)} · " +
          $"{(SelectedWorkflowProfile.Extraction.OcrEnabled ? "OCR requested" : "OCR off")} · " +
          $"{(SelectedWorkflowProfile.Analysis.DuplicateAnalysisEnabled ? "duplicate analysis" : "no duplicate analysis")} · " +
          $"{(SelectedWorkflowProfile.Ai.Enabled ? "optional AI policy" : "AI off")} · " +
          $"{SelectedWorkflowProfile.SortingRecipeIds.Count} attached recipe(s) · " +
          $"{ProcessingIntensity(SelectedWorkflowProfile)} processing";

    /// <summary>Gets or sets whether the next scan applies constrained, session-only overrides.</summary>
    public bool UseOneTimeOverride
    {
        get => _useOneTimeOverride;
        set => SetProperty(ref _useOneTimeOverride, value);
    }

    /// <summary>Gets or sets the one-time OCR constraint.</summary>
    public bool OverrideOcrEnabled
    {
        get => _overrideOcrEnabled;
        set => SetProperty(ref _overrideOcrEnabled, value);
    }

    /// <summary>Gets or sets the one-time duplicate-analysis constraint.</summary>
    public bool OverrideDuplicateAnalysisEnabled
    {
        get => _overrideDuplicateAnalysisEnabled;
        set => SetProperty(ref _overrideDuplicateAnalysisEnabled, value);
    }

    /// <summary>Gets or sets the one-time optional-AI constraint.</summary>
    public bool OverrideAiEnabled
    {
        get => _overrideAiEnabled;
        set => SetProperty(ref _overrideAiEnabled, value);
    }

    /// <summary>Gets or sets the one-time maximum file size in mebibytes.</summary>
    public double OverrideMaximumFileSizeMiB
    {
        get => _overrideMaximumFileSizeMiB;
        set => SetProperty(ref _overrideMaximumFileSizeMiB, value);
    }

    /// <summary>Gets or sets the name used when persisting adjusted settings as a new profile.</summary>
    public string NewProfileName
    {
        get => _newProfileName;
        set
        {
            if (SetProperty(ref _newProfileName, value))
            {
                SaveAdjustedAsProfileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the manually entered folder path awaiting validation.
    /// </summary>
    public string? FolderPathInput
    {
        get => _folderPathInput;
        set => SetProperty(ref _folderPathInput, value);
    }

    /// <summary>
    /// Gets or sets the folder currently selected for removal.
    /// </summary>
    public string? SelectedFolder
    {
        get => _selectedFolder;
        set => SetProperty(ref _selectedFolder, value);
    }

    /// <summary>
    /// Gets the current user-safe validation or request status.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Reports a native folder-picker failure without exposing platform details.</summary>
    public void ReportFolderPickerFailure() =>
        StatusText = "The folder picker could not be opened. Enter an absolute folder path instead.";

    /// <summary>
    /// Gets the command that validates and adds the currently entered folder path.
    /// </summary>
    public IRelayCommand AddFolderCommand { get; }

    /// <summary>
    /// Gets the command that removes the currently selected folder root.
    /// </summary>
    public IRelayCommand RemoveSelectedFolderCommand { get; }

    /// <summary>
    /// Gets the command that emits a non-executing scan request.
    /// </summary>
    public IRelayCommand StartScanCommand { get; }

    /// <summary>Gets the command that reloads available persistent profiles.</summary>
    public IAsyncRelayCommand RefreshProfilesCommand { get; }

    /// <summary>Gets the command that saves constrained adjustments as a new profile.</summary>
    public IAsyncRelayCommand SaveAdjustedAsProfileCommand { get; }

    /// <summary>
    /// Validates and adds a folder root when it exists and has not already been selected.
    /// </summary>
    /// <param name="folderPath">The user-selected folder path.</param>
    /// <returns><see langword="true"/> when the folder was added; otherwise, <see langword="false"/>.</returns>
    public bool AddFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Path.IsPathRooted(folderPath))
        {
            StatusText = "Select an absolute folder path.";
            return false;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(folderPath);
        }
        catch (ArgumentException)
        {
            StatusText = "The folder path is invalid.";
            return false;
        }

        if (!Directory.Exists(normalizedPath))
        {
            StatusText = "The selected folder is unavailable.";
            return false;
        }

        if (_selectedFolders.Any(existingPath => _pathComparer.Equals(existingPath, normalizedPath)))
        {
            StatusText = "The folder is already selected.";
            return false;
        }

        _selectedFolders.Add(normalizedPath);
        AddRecentFolder(normalizedPath);
        FolderPathInput = string.Empty;
        StatusText = "Folder added.";
        return true;
    }

    /// <summary>
    /// Removes the currently selected folder root when it belongs to the selection.
    /// </summary>
    /// <returns><see langword="true"/> when a folder was removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveSelectedFolder()
    {
        if (SelectedFolder is null)
        {
            StatusText = "Select a folder to remove.";
            return false;
        }

        var selectedPath = _selectedFolders.FirstOrDefault(path => _pathComparer.Equals(path, SelectedFolder));
        if (selectedPath is null)
        {
            StatusText = "The selected folder is unavailable.";
            return false;
        }

        _selectedFolders.Remove(selectedPath);
        SelectedFolder = null;
        StatusText = "Folder removed.";
        return true;
    }

    /// <summary>
    /// Emits the current validated folder selection without performing a scan.
    /// </summary>
    public void RequestScan()
    {
        if (_selectedFolders.Count == 0)
        {
            StatusText = "Select at least one folder before starting a scan.";
            return;
        }

        if (SelectedWorkflowProfile is null)
        {
            StatusText = "Select an available workflow profile before starting the scan.";
            return;
        }

        ScanRequested?.Invoke(this, new ScanRequest(_selectedFolders.ToArray())
        {
            ProfileId = SelectedWorkflowProfile.Id,
            OneTimeOverride = UseOneTimeOverride
                ? new WorkflowProfileOverride(
                    MaximumFileSizeBytes: checked((long)(OverrideMaximumFileSizeMiB * 1024 * 1024)),
                    OcrEnabled: OverrideOcrEnabled,
                    DuplicateAnalysisEnabled: OverrideDuplicateAnalysisEnabled,
                    AiEnabled: OverrideAiEnabled)
                : null,
        });
        StatusText = "Scan request created.";
    }

    /// <summary>Reloads active profiles from the durable workflow library.</summary>
    public async Task RefreshProfilesAsync()
    {
        if (_workflowLibrary is null)
        {
            return;
        }

        var selectedId = SelectedWorkflowProfile?.Id;
        await _workflowLibrary.InitializeAsync(CancellationToken.None);
        var profiles = await _workflowLibrary.ListProfilesAsync(false, CancellationToken.None);
        _workflowProfiles.Clear();
        foreach (var profile in profiles.Where(profile => profile.IsEnabled && !profile.IsArchived))
        {
            _workflowProfiles.Add(profile);
        }

        SelectedWorkflowProfile = _workflowProfiles.FirstOrDefault(profile => profile.Id == selectedId) ??
                                  _workflowProfiles.FirstOrDefault();
        StatusText = $"{_workflowProfiles.Count} workflow profile(s) available for manual scans.";
    }

    /// <summary>Selects an already loaded profile by its stable identifier.</summary>
    /// <param name="profileId">The profile identifier.</param>
    /// <returns><see langword="true"/> when the profile was available.</returns>
    public bool SelectProfile(string profileId)
    {
        var profile = _workflowProfiles.FirstOrDefault(item =>
            string.Equals(item.Id, profileId, StringComparison.Ordinal));
        if (profile is null)
        {
            StatusText = "The requested workflow profile is unavailable.";
            return false;
        }

        SelectedWorkflowProfile = profile;
        StatusText = $"Selected workflow profile \"{profile.Name}\".";
        return true;
    }

    private async Task SaveAdjustedAsProfileAsync()
    {
        if (_workflowLibrary is null || SelectedWorkflowProfile is null)
        {
            return;
        }

        try
        {
            var copy = await _workflowLibrary.DuplicateProfileAsync(
                SelectedWorkflowProfile.Id,
                NewProfileName,
                CancellationToken.None);
            var adjusted = copy with
            {
                Files = copy.Files with
                {
                    MaximumFileSizeBytes = Math.Min(
                        copy.Files.MaximumFileSizeBytes,
                        checked((long)(OverrideMaximumFileSizeMiB * 1024 * 1024))),
                },
                Extraction = copy.Extraction with
                {
                    OcrEnabled = copy.Extraction.OcrEnabled && OverrideOcrEnabled,
                },
                Analysis = copy.Analysis with
                {
                    DuplicateAnalysisEnabled =
                        copy.Analysis.DuplicateAnalysisEnabled &&
                        OverrideDuplicateAnalysisEnabled,
                },
                Ai = copy.Ai with
                {
                    Enabled = copy.Ai.Enabled && OverrideAiEnabled,
                    InvocationPolicy = copy.Ai.Enabled && OverrideAiEnabled
                        ? copy.Ai.InvocationPolicy
                        : WorkflowAiInvocationPolicy.Disabled,
                },
            };
            adjusted = await _workflowLibrary.UpdateProfileAsync(adjusted, CancellationToken.None);
            await RefreshProfilesAsync();
            SelectedWorkflowProfile = _workflowProfiles.First(profile => profile.Id == adjusted.Id);
            UseOneTimeOverride = false;
            NewProfileName = string.Empty;
            StatusText = "The adjusted configuration was saved as a new profile; the original was not changed.";
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or InvalidDataException or IOException)
        {
            StatusText = exception.Message;
        }
    }

    private void AddFolderFromInput() => AddFolder(FolderPathInput);

    private void AddRecentFolder(string normalizedPath)
    {
        var existingPath = _recentFolders.FirstOrDefault(path => _pathComparer.Equals(path, normalizedPath));
        if (existingPath is not null)
        {
            _recentFolders.Remove(existingPath);
        }

        _recentFolders.Insert(0, normalizedPath);
        while (_recentFolders.Count > RecentFolderLimit)
        {
            _recentFolders.RemoveAt(_recentFolders.Count - 1);
        }
    }

    private static string FormatTypes(IReadOnlyList<string> types) =>
        types.Count == 0 ? "all file types" : string.Join(", ", types.Take(5));

    private static string ProcessingIntensity(WorkflowProfile profile) =>
        profile.Extraction.OcrEnabled || profile.Ai.Enabled
            ? "higher"
            : profile.Extraction.TextEnabled || profile.Analysis.DuplicateAnalysisEnabled
                ? "balanced"
                : "minimal";
}
