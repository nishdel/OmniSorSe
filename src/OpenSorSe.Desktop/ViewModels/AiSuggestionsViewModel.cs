using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.ChangePlans;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Owns the non-mutating AI suggestion review workflow for known completed-scan results.
/// </summary>
public sealed class AiSuggestionsViewModel : ViewModelBase, IDisposable
{
    private readonly IAiSuggestionService? _aiSuggestionService;
    private readonly IConfigurationService _configurationService;
    private readonly IContentStore? _contentStore;
    private readonly ISuggestionChangePlanFactory? _changePlanFactory;
    private readonly ObservableCollection<AiFolderStructurePlanItem> _structureItems = [];
    private IReadOnlyList<string> _existingFolderNames = Array.Empty<string>();
    private IReadOnlyList<string> _siblingFileNames = Array.Empty<string>();
    private IReadOnlyList<ResultFile> _pageFiles = Array.Empty<ResultFile>();
    private ResultFile? _selectedFile;
    private string? _sourceScanId;
    private WorkflowConfigurationSnapshot? _workflow;
    private AiFileRenameSuggestion? _renameSuggestion;
    private AiFolderStructurePlan? _folderStructurePlan;
    private AiDocumentInterpretationSuggestion? _documentInterpretation;
    private string? _proposedFileName;
    private string _statusText = "Enable an AI capability in Settings to request review-only suggestions.";
    private StatusPresentation _status = StatusPresentation.Information("Enable an AI capability in Settings to request review-only suggestions.");
    private AiRequestStage? _progressStage;
    private string _progressText = "No AI request is active.";
    private string _elapsedText = string.Empty;
    private bool _isBusy;
    private bool _hasContext;
    private AiReadinessState _readinessState = AiReadinessState.NotConfigured;
    private string? _actualModelUsed;
    private CancellationTokenSource? _operationCancellation;
    private long _operationVersion;
    private bool _isDisposed;

    /// <summary>Initializes the suggestion-review model over the optional application service.</summary>
    public AiSuggestionsViewModel(
        IConfigurationService configurationService,
        IAiSuggestionService? aiSuggestionService = null,
        IContentStore? contentStore = null,
        ISuggestionChangePlanFactory? changePlanFactory = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _aiSuggestionService = aiSuggestionService;
        _contentStore = contentStore;
        _changePlanFactory = changePlanFactory;
        StructureItems = new ReadOnlyObservableCollection<AiFolderStructurePlanItem>(_structureItems);
        GenerateSuggestionCommand = new AsyncRelayCommand(GenerateRenameAsync, CanGenerateRename);
        AcceptRenameCommand = new AsyncRelayCommand(() => RecordRenameAsync(AiSuggestionDecisionOutcome.Accepted), CanReviewRename);
        RejectRenameCommand = new AsyncRelayCommand(() => RecordRenameAsync(AiSuggestionDecisionOutcome.Rejected), CanReviewRename);
        GenerateFolderStructureCommand = new AsyncRelayCommand(GenerateFolderStructureAsync, CanGenerateFolderStructure);
        AcceptFolderStructureCommand = new AsyncRelayCommand(() => RecordFolderStructureAsync(AiSuggestionDecisionOutcome.Accepted), CanReviewFolderStructure);
        RejectFolderStructureCommand = new AsyncRelayCommand(() => RecordFolderStructureAsync(AiSuggestionDecisionOutcome.Rejected), CanReviewFolderStructure);
        GenerateDocumentInterpretationCommand = new AsyncRelayCommand(
            GenerateDocumentInterpretationAsync,
            CanGenerateDocumentInterpretation);
        DismissDocumentInterpretationCommand = new RelayCommand(
            DismissDocumentInterpretation,
            () => DocumentInterpretation is not null && !IsBusy);
        CancelAiOperationCommand = new RelayCommand(CancelOperation, () => IsBusy);
        RetryConnectionCommand = new AsyncRelayCommand(RetryConnectionAsync, CanRetryConnection);
        RefreshFeatureAvailability();
    }

    /// <summary>Occurs after an accepted suggestion becomes a non-mutating Change Plan.</summary>
    public event EventHandler<ChangePlan>? ChangePlanCreated;

    /// <summary>Gets the selected completed-scan file currently available for review.</summary>
    public ResultFile? SelectedFile => _selectedFile;

    /// <summary>Gets whether any enabled AI capability should be presented.</summary>
    public bool IsVisible => HasContext && (IsFileRenameVisible || IsFolderStructureVisible || IsDocumentInterpretationVisible);

    /// <summary>Gets whether the rename capability is enabled and available.</summary>
    public bool IsFileRenameVisible =>
        _aiSuggestionService is not null &&
        IsWorkflowFileAllowed(_selectedFile) &&
        _configurationService.Current.Ai.IsCapabilityEnabled(AiCapability.FileRenameSuggestions);

    /// <summary>Gets whether the folder-structure capability is enabled and available.</summary>
    public bool IsFolderStructureVisible =>
        _aiSuggestionService is not null &&
        (_workflow is null || _pageFiles.Count > 0) &&
        _configurationService.Current.Ai.IsCapabilityEnabled(AiCapability.FolderStructureSuggestions);

    /// <summary>Gets whether explicit bounded extracted-text interpretation is enabled.</summary>
    public bool IsDocumentInterpretationVisible =>
        _aiSuggestionService is not null &&
        _contentStore is not null &&
        IsWorkflowFileAllowed(_selectedFile) &&
        _configurationService.Current.Ai.IsCapabilityEnabled(AiCapability.DocumentTextInterpretation);

    /// <summary>Explains exactly how the bounded folder request treats the current result-page files.</summary>
    public string FolderStructureContextText => _pageFiles.Count switch
    {
        0 => "No files from the current results page are available for a folder request.",
        > AiPromptLimits.MaximumFolderStructureFiles =>
            $"{_pageFiles.Count} files are in the current results-page selection. The safe request limit is " +
            $"{AiPromptLimits.MaximumFolderStructureFiles}; none will be sent until the selection is reduced.",
        _ =>
            $"{_pageFiles.Count} of {_pageFiles.Count} files will be sent using opaque IDs and must be assigned exactly once.",
    };

    /// <summary>Provides a concise review-before-send summary for the currently available AI tasks.</summary>
    public string AiRequestContextText
    {
        get
        {
            var model = _configurationService.Current.Ai.SelectedModel ?? "not selected";
            var selectedMetadata = _selectedFile is null
                ? "No file selected."
                : $"Filename: {_selectedFile.DisplayFileName}; extension: {_selectedFile.NormalizedExtension}; type: {_selectedFile.ClassificationDisplay}.";
            return
                $"Model: {model}; profile policy: {WorkflowPolicyText()}.{Environment.NewLine}" +
                $"Rename: {AiPromptBuilder.FileRenameTaskId}, prompt {AiPromptTemplates.FileRenamePromptVersion}. {selectedMetadata}{Environment.NewLine}" +
                $"Folder plan: {AiPromptBuilder.FolderStructureTaskId}, prompt {AiPromptTemplates.FolderStructurePromptVersion}. {FolderStructureContextText}{Environment.NewLine}" +
                $"Document interpretation: {AiPromptBuilder.DocumentInterpretationTaskId}, prompt {AiPromptTemplates.DocumentInterpretationPromptVersion}; " +
                $"at most {AiPromptLimits.MaximumDocumentTextPages} text segments and {AiPromptLimits.MaximumDocumentTextCharacters} characters. " +
                "The indexed native/OCR provenance and any truncation are shown in Advanced Diagnostics after an explicit request.";
        }
    }

    /// <summary>Gets the current concise readiness of the optional local AI service.</summary>
    public AiReadinessState ReadinessState
    {
        get => _readinessState;
        private set
        {
            if (SetProperty(ref _readinessState, value))
            {
                OnPropertyChanged(nameof(ReadinessText));
                OnPropertyChanged(nameof(RenameActionAvailabilityText));
                OnPropertyChanged(nameof(IsRenameActionAvailable));
                RetryConnectionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets plain-language local-AI readiness guidance.</summary>
    public string ReadinessText => ReadinessState switch
    {
        AiReadinessState.NotConfigured => "Local AI is not configured. Enable AI and choose an installed model in Settings.",
        AiReadinessState.NotChecked => "Local AI has not been checked yet. Retry the connection or request a suggestion.",
        AiReadinessState.ServerUnavailable => "Your local AI is not running. Start Ollama, then retry the connection.",
        AiReadinessState.ServerAvailable => "Your local AI is running. The selected model still needs to be checked.",
        AiReadinessState.ModelMissing => "The selected model is not installed. Choose an installed model in Settings.",
        AiReadinessState.Ready => ActualModelUsed is null
            ? "File Assistant is ready."
            : $"File Assistant is ready. Last model used: {ActualModelUsed}.",
        AiReadinessState.Running => "File Assistant is working on your explicit request.",
        AiReadinessState.Failed => "The last suggestion failed safely. Your files were not changed; you can retry.",
        AiReadinessState.Cancelled => "Suggestion cancelled. Your files were not changed; you can retry.",
        _ => "Local AI status is unavailable.",
    };

    /// <summary>Gets the actual model named by the latest validated suggestion.</summary>
    public string? ActualModelUsed
    {
        get => _actualModelUsed;
        private set
        {
            if (SetProperty(ref _actualModelUsed, value))
            {
                OnPropertyChanged(nameof(ReadinessText));
            }
        }
    }

    /// <summary>Gets whether rename generation is currently executable.</summary>
    public bool IsRenameActionAvailable => CanGenerateRename();

    /// <summary>Gets the exact reason rename generation is ready or unavailable.</summary>
    public string RenameActionAvailabilityText
    {
        get
        {
            var settings = _configurationService.Current.Ai;
            if (_selectedFile is null)
            {
                return "Choose a file to request a rename suggestion.";
            }

            if (!settings.Enabled)
            {
                return "AI features are off. Enable them in Settings.";
            }

            if (!settings.FileRenameSuggestionsEnabled)
            {
                return "Rename suggestions are off. Enable them in Settings.";
            }

            if (_aiSuggestionService is null)
            {
                return "The local AI service is unavailable in this application session.";
            }

            if (!IsSupportedRenameContext(_selectedFile))
            {
                return "This item does not have a supported filename for rename suggestions.";
            }

            if (IsBusy)
            {
                return "Another File Assistant operation is running.";
            }

            if (string.IsNullOrWhiteSpace(settings.SelectedModel))
            {
                return "Choose an installed AI model in Settings.";
            }

            return ReadinessState switch
            {
                AiReadinessState.ServerUnavailable => "Your local AI is not running. Retry the connection or try the request again.",
                AiReadinessState.ModelMissing => "The selected model is unavailable. Choose another model in Settings.",
                AiReadinessState.Failed => "The previous request failed safely. Try again or retry the connection.",
                AiReadinessState.Cancelled => "The previous request was cancelled. Try again when ready.",
                _ => $"Ready to suggest a name with '{settings.SelectedModel}'. Nothing will be renamed automatically.",
            };
        }
    }

    /// <summary>Gets the current validated rename proposal.</summary>
    public AiFileRenameSuggestion? RenameSuggestion
    {
        get => _renameSuggestion;
        private set
        {
            if (SetProperty(ref _renameSuggestion, value))
            {
                OnPropertyChanged(nameof(HasRenameSuggestion));
                OnPropertyChanged(nameof(RenameReason));
                OnPropertyChanged(nameof(RenameConfidenceText));
                NotifyCommandStates();
            }
        }
    }

    /// <summary>Gets whether one completely validated rename proposal is available.</summary>
    public bool HasRenameSuggestion => RenameSuggestion is not null;

    /// <summary>Gets or sets the editable rename proposal. It is never applied to a file by this view model.</summary>
    public string? ProposedFileName
    {
        get => _proposedFileName;
        set
        {
            if (SetProperty(ref _proposedFileName, value))
            {
                NotifyCommandStates();
            }
        }
    }

    /// <summary>Gets the bounded model reason for the rename proposal.</summary>
    public string? RenameReason => RenameSuggestion?.Reason;

    /// <summary>Gets a non-certain description of the optional model confidence.</summary>
    public string RenameConfidenceText => RenameSuggestion?.Confidence is { } confidence
        ? $"Model confidence estimate: {confidence:P0}. This is not certainty."
        : "The model did not provide a confidence estimate.";

    /// <summary>Gets the current preview-only folder-structure plan.</summary>
    public AiFolderStructurePlan? FolderStructurePlan
    {
        get => _folderStructurePlan;
        private set
        {
            if (SetProperty(ref _folderStructurePlan, value))
            {
                OnPropertyChanged(nameof(HasFolderStructurePlan));
                OnPropertyChanged(nameof(FolderStructureReason));
                NotifyCommandStates();
            }
        }
    }

    /// <summary>Gets whether a completely validated preview-only folder plan is available.</summary>
    public bool HasFolderStructurePlan => FolderStructurePlan is not null;

    /// <summary>Gets validated known-file assignments for the current logical hierarchy.</summary>
    public ReadOnlyObservableCollection<AiFolderStructurePlanItem> StructureItems { get; }

    /// <summary>Gets the bounded model reason for the logical hierarchy.</summary>
    public string? FolderStructureReason => FolderStructurePlan?.Reason;

    /// <summary>Gets the current unverified document-text interpretation proposal.</summary>
    public AiDocumentInterpretationSuggestion? DocumentInterpretation
    {
        get => _documentInterpretation;
        private set
        {
            if (SetProperty(ref _documentInterpretation, value))
            {
                OnPropertyChanged(nameof(HasDocumentInterpretation));
                OnPropertyChanged(nameof(DocumentInterpretationSummary));
                DismissDocumentInterpretationCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether a validated unverified interpretation is ready for review.</summary>
    public bool HasDocumentInterpretation => DocumentInterpretation is not null;

    /// <summary>Gets a concise bounded display of the validated interpretation fields.</summary>
    public string DocumentInterpretationSummary
    {
        get
        {
            if (DocumentInterpretation is not { } value)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            Add("Type", value.DocumentType);
            Add("Title", value.Title);
            Add("Issuer", value.Issuer);
            Add("Folder", value.SuggestedFolder);
            if (value.Dates.Count > 0)
            {
                parts.Add($"Dates: {string.Join(", ", value.Dates)}");
            }

            if (value.Tags.Count > 0)
            {
                parts.Add($"Tags: {string.Join(", ", value.Tags.Select(tag => tag.DisplayName))}");
            }

            parts.Add($"Reason: {value.Reason}");
            parts.Add(value.Confidence is { } confidence
                ? $"Model confidence estimate: {confidence:P0}; not certainty."
                : "No model confidence estimate was supplied.");
            return string.Join(Environment.NewLine, parts);

            void Add(string label, string? text)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add($"{label}: {text}");
                }
            }
        }
    }

    /// <summary>Gets the user-safe workflow status.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets the consistently presented AI workflow status.</summary>
    public StatusPresentation Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Gets the latest truthful typed request stage.</summary>
    public AiRequestStage? ProgressStage
    {
        get => _progressStage;
        private set => SetProperty(ref _progressStage, value);
    }

    /// <summary>Gets the latest progress-stage explanation.</summary>
    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    /// <summary>Gets elapsed-time text updated at stage transitions and completion.</summary>
    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetProperty(ref _elapsedText, value);
    }

    /// <summary>Gets whether one explicit provider or local-review operation is active.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CancelAiOperationCommand.NotifyCanExecuteChanged();
                NotifyCommandStates();
            }
        }
    }

    /// <summary>Gets whether a completed Results snapshot is available.</summary>
    public bool HasContext
    {
        get => _hasContext;
        private set
        {
            if (SetProperty(ref _hasContext, value))
            {
                OnPropertyChanged(nameof(IsVisible));
            }
        }
    }

    /// <summary>Gets the command that requests one validated filename proposal.</summary>
    public IAsyncRelayCommand GenerateSuggestionCommand { get; }

    /// <summary>Gets the command that records acceptance or an edit without renaming a file.</summary>
    public IAsyncRelayCommand AcceptRenameCommand { get; }

    /// <summary>Gets the command that records rejection without changing a file.</summary>
    public IAsyncRelayCommand RejectRenameCommand { get; }

    /// <summary>Gets the command that requests a bounded preview-only hierarchy.</summary>
    public IAsyncRelayCommand GenerateFolderStructureCommand { get; }

    /// <summary>Gets the command that records acceptance without creating folders or moving files.</summary>
    public IAsyncRelayCommand AcceptFolderStructureCommand { get; }

    /// <summary>Gets the command that records rejection of a logical hierarchy.</summary>
    public IAsyncRelayCommand RejectFolderStructureCommand { get; }

    /// <summary>Gets the command for one explicit bounded document-text interpretation request.</summary>
    public IAsyncRelayCommand GenerateDocumentInterpretationCommand { get; }

    /// <summary>Gets the command that dismisses the in-memory interpretation without changing anything.</summary>
    public IRelayCommand DismissDocumentInterpretationCommand { get; }

    /// <summary>Gets the command that cancels the active explicit AI operation.</summary>
    public IRelayCommand CancelAiOperationCommand { get; }

    /// <summary>Gets the explicit bounded local-AI connection retry command.</summary>
    public IAsyncRelayCommand RetryConnectionCommand { get; }

    /// <summary>Replaces in-memory review context without reading file content or retaining paths for provider requests.</summary>
    public void SetContext(ResultFile? selectedFile, ResultsSnapshot? snapshot, IReadOnlyList<ResultFile>? pageFiles)
    {
        if (IsBusy)
        {
            CancelOperation();
        }

        _workflow = snapshot?.Workflow;
        HasContext = snapshot is not null;
        _sourceScanId = snapshot?.SessionId;
        var eligibleSelectedFile = IsWorkflowFileAllowed(selectedFile) ? selectedFile : null;
        var fileChanged = !string.Equals(_selectedFile?.Id, eligibleSelectedFile?.Id, StringComparison.Ordinal);
        _selectedFile = eligibleSelectedFile;
        _pageFiles = pageFiles is null
            ? Array.Empty<ResultFile>()
            : Array.AsReadOnly(pageFiles.Where(IsWorkflowFileAllowed).ToArray());
        OnPropertyChanged(nameof(FolderStructureContextText));
        OnPropertyChanged(nameof(AiRequestContextText));
        _existingFolderNames = snapshot is null
            ? Array.Empty<string>()
            : Array.AsReadOnly(snapshot.Directories
                .Select(directory => directory.DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray());
        _siblingFileNames = selectedFile is null || snapshot is null
            ? Array.Empty<string>()
            : Array.AsReadOnly(snapshot.Files
                .Where(file => !string.Equals(file.Id, selectedFile.Id, StringComparison.Ordinal) &&
                               string.Equals(Path.GetDirectoryName(file.FullPath), Path.GetDirectoryName(selectedFile.FullPath), StringComparison.OrdinalIgnoreCase))
                .Select(file => file.DisplayFileName)
                .ToArray());

        if (fileChanged)
        {
            RenameSuggestion = null;
            ProposedFileName = null;
            DocumentInterpretation = null;
        }

        FolderStructurePlan = null;
        _structureItems.Clear();

        StatusText = IsVisible
            ? "AI capabilities are ready for an explicit review-only request."
            : _workflow?.Ai.Enabled == false ||
              _workflow?.Ai.InvocationPolicy == WorkflowAiInvocationPolicy.Disabled
                ? "The workflow profile disables AI for this scan."
                : "No current file satisfies both the workflow AI policy and global AI settings.";
        Status = StatusPresentation.Information(StatusText);
        ProgressStage = null;
        ProgressText = "No AI request is active.";
        ElapsedText = string.Empty;
        RefreshReadinessFromConfiguration(preserveRetryableState: true);
        NotifyCommandStates();
    }

    /// <summary>Refreshes visibility and command gates after active settings change.</summary>
    public void RefreshFeatureAvailability()
    {
        if (!IsFileRenameVisible)
        {
            RenameSuggestion = null;
            ProposedFileName = null;
        }

        if (!IsFolderStructureVisible)
        {
            FolderStructurePlan = null;
            _structureItems.Clear();
        }

        if (!IsDocumentInterpretationVisible)
        {
            DocumentInterpretation = null;
        }

        if (!IsFileRenameVisible && !IsFolderStructureVisible && !IsDocumentInterpretationVisible)
        {
            CancelOperation();
        }

        OnPropertyChanged(nameof(IsFileRenameVisible));
        OnPropertyChanged(nameof(IsFolderStructureVisible));
        OnPropertyChanged(nameof(IsDocumentInterpretationVisible));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(AiRequestContextText));
        RefreshReadinessFromConfiguration(preserveRetryableState: false);
        NotifyCommandStates();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        CancelOperation();
        _operationCancellation?.Dispose();
        _isDisposed = true;
    }

    private async Task GenerateRenameAsync()
    {
        if (_aiSuggestionService is null || _selectedFile is null || !IsFileRenameVisible)
        {
            return;
        }

        var (cancellation, version) = BeginOperation();
        StatusText = "Requesting an AI-generated rename suggestion...";
        Status = StatusPresentation.Progress(StatusText);
        var progress = new Progress<AiRequestProgress>(value => ApplyProgress(value, cancellation, version));
        try
        {
            var result = await _aiSuggestionService.GenerateFileRenameAsync(
                new AiFileRenameRequest(_selectedFile, _siblingFileNames),
                _configurationService.Current.Ai,
                progress,
                cancellation.Token);
            if (!IsCurrentOperation(cancellation, version))
            {
                return;
            }

            RenameSuggestion = result.Suggestion;
            ProposedFileName = result.Suggestion?.SuggestedFileName;
            ActualModelUsed = result.Suggestion?.Model;
            ReadinessState = MapReadiness(result.State);
            StatusText = result.Message;
            Status = PresentResult(result.State, result.Message);
        }
        catch (OperationCanceledException)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                StatusText = "The AI rename request was cancelled.";
                Status = StatusPresentation.Information(StatusText);
                ReadinessState = AiReadinessState.Cancelled;
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                StatusText = "The AI rename request failed safely. No file was changed.";
                Status = StatusPresentation.Error(StatusText);
                ReadinessState = AiReadinessState.Failed;
            }
        }
        finally
        {
            EndOperation(cancellation, version);
        }
    }

    private async Task GenerateFolderStructureAsync()
    {
        if (_aiSuggestionService is null || _pageFiles.Count == 0 || !IsFolderStructureVisible)
        {
            return;
        }

        if (_pageFiles.Count > AiPromptLimits.MaximumFolderStructureFiles)
        {
            StatusText = FolderStructureContextText;
            Status = StatusPresentation.Warning(StatusText);
            return;
        }

        var (cancellation, version) = BeginOperation();
        StatusText = "Requesting an AI-generated folder-structure suggestion...";
        Status = StatusPresentation.Progress(StatusText);
        var progress = new Progress<AiRequestProgress>(value => ApplyProgress(value, cancellation, version));
        try
        {
            var result = await _aiSuggestionService.GenerateFolderStructureAsync(
                new AiFolderStructureRequest(_pageFiles, _existingFolderNames),
                _configurationService.Current.Ai,
                progress,
                cancellation.Token);
            if (!IsCurrentOperation(cancellation, version))
            {
                return;
            }

            FolderStructurePlan = result.Plan;
            ActualModelUsed = result.Plan?.Model;
            ReadinessState = MapReadiness(result.State);
            _structureItems.Clear();
            if (result.Plan is not null)
            {
                foreach (var item in result.Plan.Items)
                {
                    _structureItems.Add(item);
                }
            }

            StatusText = result.Message;
            Status = PresentResult(result.State, result.Message);
        }
        catch (OperationCanceledException)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                StatusText = "The AI folder-structure request was cancelled.";
                Status = StatusPresentation.Information(StatusText);
                ReadinessState = AiReadinessState.Cancelled;
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                StatusText = "The AI folder-structure request failed safely. No folder or file was changed.";
                Status = StatusPresentation.Error(StatusText);
                ReadinessState = AiReadinessState.Failed;
            }
        }
        finally
        {
            EndOperation(cancellation, version);
        }
    }

    private async Task RecordRenameAsync(AiSuggestionDecisionOutcome requestedOutcome)
    {
        var suggestion = RenameSuggestion;
        if (_aiSuggestionService is null || suggestion is null || _selectedFile is null || !IsFileRenameVisible)
        {
            return;
        }

        var outcome = requestedOutcome;
        string? finalValue = null;
        if (requestedOutcome != AiSuggestionDecisionOutcome.Rejected)
        {
            if (!AiSuggestionValidator.TryNormalizeFileName(
                    ProposedFileName,
                    _selectedFile.NormalizedExtension,
                    _siblingFileNames,
                    out var normalized,
                    out var error))
            {
                StatusText = error;
                Status = StatusPresentation.Error(error);
                return;
            }

            if (string.Equals(normalized, _selectedFile.DisplayFileName, StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "The reviewed filename does not propose a change. No decision was saved and no file was changed.";
                Status = StatusPresentation.Warning(StatusText);
                return;
            }

            finalValue = normalized;
            outcome = string.Equals(normalized, suggestion.SuggestedFileName, StringComparison.Ordinal)
                ? AiSuggestionDecisionOutcome.Accepted
                : AiSuggestionDecisionOutcome.Edited;
        }

        var result = await _aiSuggestionService.RecordDecisionAsync(
            new AiSuggestionDecision(
                AiSuggestionDecisionKind.Rename,
                outcome,
                _selectedFile.NormalizedExtension,
                suggestion.SuggestedFileName,
                finalValue,
                suggestion.Provider,
                suggestion.Model,
                DateTimeOffset.UtcNow),
            _configurationService.Current.Ai,
            CancellationToken.None);
        StatusText = outcome == AiSuggestionDecisionOutcome.Rejected && result.State == AiAvailabilityState.ModelSelected
            ? "The AI-generated rename suggestion was rejected. No file was changed."
            : result.Message;
        Status = PresentResult(result.State, StatusText);
        if (outcome != AiSuggestionDecisionOutcome.Rejected &&
            finalValue is not null &&
            _changePlanFactory is not null &&
            result.State == AiAvailabilityState.ModelSelected)
        {
            try
            {
                var changePlan = await _changePlanFactory.CreateRenamePlanAsync(
                    _selectedFile,
                    suggestion,
                    finalValue,
                    _sourceScanId,
                    CancellationToken.None);
                ChangePlanCreated?.Invoke(this, changePlan);
                StatusText = "The reviewed rename is now a Change Plan. Review and validate it before applying.";
                Status = StatusPresentation.Information(StatusText);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidDataException or
                IOException or
                UnauthorizedAccessException)
            {
                StatusText = "The reviewed rename could not be converted into a safe Change Plan. No file was changed.";
                Status = StatusPresentation.Error(StatusText);
            }
        }
    }

    private async Task GenerateDocumentInterpretationAsync()
    {
        if (_aiSuggestionService is null || _contentStore is null || _selectedFile is null ||
            !IsDocumentInterpretationVisible)
        {
            return;
        }

        var (cancellation, version) = BeginOperation();
        StatusText = "Loading bounded extracted text for an explicit AI interpretation request...";
        Status = StatusPresentation.Progress(StatusText);
        try
        {
            var content = await _contentStore.GetAsync(_selectedFile.FullPath, cancellation.Token);
            if (content is null ||
                string.IsNullOrWhiteSpace(content.NativeText) && string.IsNullOrWhiteSpace(content.OcrText))
            {
                StatusText = "No locally extracted text is available for the selected file. Scan it with content extraction enabled first.";
                Status = StatusPresentation.Warning(StatusText);
                return;
            }

            var result = await _aiSuggestionService.GenerateDocumentInterpretationAsync(
                new AiDocumentTextRequest(
                    _selectedFile.Id,
                    _selectedFile.DisplayFileName,
                    content.NativeText,
                    content.OcrText,
                    content.OcrPages)
                {
                    RelatedDiagnosticSessionId = content.DiagnosticSessionId,
                },
                _configurationService.Current.Ai,
                cancellation.Token);
            if (!IsCurrentOperation(cancellation, version))
            {
                return;
            }

            DocumentInterpretation = result.Suggestion;
            ActualModelUsed = result.Suggestion?.Model;
            ReadinessState = MapReadiness(result.State);
            StatusText = result.Message;
            Status = PresentResult(result.State, result.Message);
        }
        catch (OperationCanceledException)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                StatusText = "The AI document interpretation request was cancelled.";
                Status = StatusPresentation.Information(StatusText);
                ReadinessState = AiReadinessState.Cancelled;
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                StatusText = "The AI document interpretation request failed safely. No source file was changed.";
                Status = StatusPresentation.Error(StatusText);
                ReadinessState = AiReadinessState.Failed;
            }
        }
        finally
        {
            EndOperation(cancellation, version);
        }
    }

    private void DismissDocumentInterpretation()
    {
        DocumentInterpretation = null;
        StatusText = "The AI-generated interpretation was dismissed. Nothing was saved or changed.";
        Status = StatusPresentation.Information(StatusText);
    }

    private async Task RecordFolderStructureAsync(AiSuggestionDecisionOutcome outcome)
    {
        var plan = FolderStructurePlan;
        if (_aiSuggestionService is null || plan is null || !IsFolderStructureVisible)
        {
            return;
        }

        var value = string.Join(';', plan.Folders.Select(folder => folder.Name).Distinct(StringComparer.OrdinalIgnoreCase));
        var result = await _aiSuggestionService.RecordDecisionAsync(
            new AiSuggestionDecision(
                AiSuggestionDecisionKind.FolderStructure,
                outcome,
                null,
                value,
                outcome == AiSuggestionDecisionOutcome.Rejected ? null : value,
                plan.Provider,
                plan.Model,
                DateTimeOffset.UtcNow),
            _configurationService.Current.Ai,
            CancellationToken.None);
        StatusText = outcome == AiSuggestionDecisionOutcome.Rejected && result.State == AiAvailabilityState.ModelSelected
            ? "The AI-generated folder-structure suggestion was rejected. No folder or file was changed."
            : result.Message;
        Status = PresentResult(result.State, StatusText);
        if (outcome != AiSuggestionDecisionOutcome.Rejected &&
            _changePlanFactory is not null &&
            result.State == AiAvailabilityState.ModelSelected)
        {
            try
            {
                var changePlan = await _changePlanFactory.CreateFolderStructurePlanAsync(
                    _pageFiles,
                    plan,
                    _sourceScanId,
                    CancellationToken.None);
                ChangePlanCreated?.Invoke(this, changePlan);
                StatusText = "The reviewed folder suggestion is now a Change Plan. Review and validate it before applying.";
                Status = StatusPresentation.Information(StatusText);
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidDataException or
                IOException or
                UnauthorizedAccessException)
            {
                StatusText = "The folder suggestion could not be converted into a safe Change Plan. No folder or file was changed.";
                Status = StatusPresentation.Error(StatusText);
            }
        }
    }

    private bool CanGenerateRename() =>
        !IsBusy &&
        IsFileRenameVisible &&
        _selectedFile is not null &&
        IsSupportedRenameContext(_selectedFile) &&
        !string.IsNullOrWhiteSpace(_configurationService.Current.Ai.SelectedModel);

    private bool CanReviewRename() =>
        !IsBusy && IsFileRenameVisible && RenameSuggestion is not null;

    private bool CanGenerateFolderStructure() =>
        !IsBusy &&
        IsFolderStructureVisible &&
        _pageFiles.Count is > 0 and <= AiPromptLimits.MaximumFolderStructureFiles &&
        !string.IsNullOrWhiteSpace(_configurationService.Current.Ai.SelectedModel);

    private bool CanReviewFolderStructure() =>
        !IsBusy && IsFolderStructureVisible && FolderStructurePlan is not null;

    private bool CanGenerateDocumentInterpretation() =>
        !IsBusy &&
        IsDocumentInterpretationVisible &&
        _selectedFile is not null &&
        !string.IsNullOrWhiteSpace(_configurationService.Current.Ai.SelectedModel);

    private bool IsWorkflowFileAllowed(ResultFile? file)
    {
        if (_workflow is null)
        {
            return file is not null;
        }

        if (file is null ||
            !_workflow.Ai.Enabled ||
            _workflow.Ai.InvocationPolicy == WorkflowAiInvocationPolicy.Disabled)
        {
            return false;
        }

        return _workflow.Ai.InvocationPolicy switch
        {
            WorkflowAiInvocationPolicy.MissingDeterministicClassificationOnly =>
                file.Category is null or OpenSorSe.Scanner.Models.FileCategory.Unknown,
            WorkflowAiInvocationPolicy.AfterTextExtraction =>
                _workflow.Extraction.TextEnabled || _workflow.Extraction.OcrEnabled,
            WorkflowAiInvocationPolicy.SelectedFileTypesOnly =>
                _workflow.Ai.SelectedFileTypes.Contains(
                    NormalizeExtension(file.NormalizedExtension),
                    StringComparer.OrdinalIgnoreCase),
            WorkflowAiInvocationPolicy.ExplicitRetryOnly => true,
            _ => true,
        };
    }

    private string WorkflowPolicyText() =>
        _workflow is null
            ? "legacy scan (global settings)"
            : $"{_workflow.ProfileName} r{_workflow.ProfileRevision}, {_workflow.Ai.InvocationPolicy}";

    private static string NormalizeExtension(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $".{value.Trim().TrimStart('.').ToLowerInvariant()}";

    private void NotifyCommandStates()
    {
        GenerateSuggestionCommand.NotifyCanExecuteChanged();
        AcceptRenameCommand.NotifyCanExecuteChanged();
        RejectRenameCommand.NotifyCanExecuteChanged();
        GenerateFolderStructureCommand.NotifyCanExecuteChanged();
        AcceptFolderStructureCommand.NotifyCanExecuteChanged();
        RejectFolderStructureCommand.NotifyCanExecuteChanged();
        GenerateDocumentInterpretationCommand.NotifyCanExecuteChanged();
        DismissDocumentInterpretationCommand.NotifyCanExecuteChanged();
        RetryConnectionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsFileRenameVisible));
        OnPropertyChanged(nameof(IsFolderStructureVisible));
        OnPropertyChanged(nameof(IsDocumentInterpretationVisible));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsRenameActionAvailable));
        OnPropertyChanged(nameof(RenameActionAvailabilityText));
    }

    private (CancellationTokenSource Cancellation, long Version) BeginOperation()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _operationCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        var version = Interlocked.Increment(ref _operationVersion);
        IsBusy = true;
        ReadinessState = AiReadinessState.Running;
        return (cancellation, version);
    }

    private bool IsCurrentOperation(CancellationTokenSource cancellation, long version) =>
        !cancellation.IsCancellationRequested &&
        ReferenceEquals(_operationCancellation, cancellation) &&
        version == Volatile.Read(ref _operationVersion);

    private void EndOperation(CancellationTokenSource cancellation, long version)
    {
        if (ReferenceEquals(_operationCancellation, cancellation))
        {
            _operationCancellation = null;
        }

        if (version == Volatile.Read(ref _operationVersion))
        {
            IsBusy = false;
        }

        cancellation.Dispose();
    }

    private void CancelOperation()
    {
        var cancellation = Interlocked.Exchange(ref _operationCancellation, null);
        if (cancellation is null)
        {
            return;
        }

        Interlocked.Increment(ref _operationVersion);
        cancellation.Cancel();
        IsBusy = false;
        ReadinessState = AiReadinessState.Cancelled;
        StatusText = "Suggestion cancelled. Your files were not changed.";
        ProgressStage = AiRequestStage.RequestCancelled;
        ProgressText = StatusText;
        Status = StatusPresentation.Information(StatusText);
    }

    private bool CanRetryConnection() =>
        _aiSuggestionService is not null &&
        !IsBusy &&
        _configurationService.Current.Ai.Enabled;

    private async Task RetryConnectionAsync()
    {
        if (_aiSuggestionService is null || !CanRetryConnection())
        {
            return;
        }

        var (cancellation, version) = BeginOperation();
        StatusText = "Checking your local AI...";
        Status = StatusPresentation.Progress(StatusText);
        try
        {
            var settings = _configurationService.Current;
            var connection = await _aiSuggestionService.TestConnectionAsync(settings, cancellation.Token);
            if (!IsCurrentOperation(cancellation, version))
            {
                return;
            }

            if (connection.State != AiAvailabilityState.Connected)
            {
                ReadinessState = MapReadiness(connection.State);
                StatusText = connection.Message;
                Status = PresentResult(connection.State, connection.Message);
                return;
            }

            ReadinessState = AiReadinessState.ServerAvailable;
            var models = await _aiSuggestionService.DiscoverModelsAsync(settings, cancellation.Token);
            if (!IsCurrentOperation(cancellation, version))
            {
                return;
            }

            var selectedModel = settings.Ai.SelectedModel;
            var selectedAvailable = !string.IsNullOrWhiteSpace(selectedModel) &&
                                    models.Models.Any(model => string.Equals(model.Id, selectedModel, StringComparison.Ordinal));
            ReadinessState = selectedAvailable
                ? AiReadinessState.Ready
                : string.IsNullOrWhiteSpace(selectedModel)
                    ? AiReadinessState.NotConfigured
                    : AiReadinessState.ModelMissing;
            StatusText = selectedAvailable
                ? $"Local AI is ready with '{selectedModel}'."
                : string.IsNullOrWhiteSpace(selectedModel)
                    ? "Choose an installed AI model in Settings."
                    : $"The selected model '{selectedModel}' is not installed.";
            Status = selectedAvailable
                ? StatusPresentation.Success(StatusText)
                : StatusPresentation.Warning(StatusText);
        }
        catch (OperationCanceledException)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                ReadinessState = AiReadinessState.Cancelled;
                StatusText = "Connection check cancelled. You can retry.";
                Status = StatusPresentation.Information(StatusText);
            }
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _operationVersion))
            {
                ReadinessState = AiReadinessState.Failed;
                StatusText = "Your local AI could not be checked. Start Ollama and retry.";
                Status = StatusPresentation.Error(StatusText);
            }
        }
        finally
        {
            EndOperation(cancellation, version);
        }
    }

    private void RefreshReadinessFromConfiguration(bool preserveRetryableState)
    {
        var settings = _configurationService.Current.Ai;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.SelectedModel))
        {
            ReadinessState = AiReadinessState.NotConfigured;
            ActualModelUsed = null;
            return;
        }

        if (!preserveRetryableState ||
            ReadinessState is AiReadinessState.NotConfigured or AiReadinessState.Ready)
        {
            ReadinessState = AiReadinessState.NotChecked;
            ActualModelUsed = null;
        }
    }

    private static bool IsSupportedRenameContext(ResultFile file) =>
        !string.IsNullOrWhiteSpace(file.Id) &&
        !string.IsNullOrWhiteSpace(file.DisplayFileName) &&
        file.DisplayFileName is not "." and not ".." &&
        file.DisplayFileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static AiReadinessState MapReadiness(AiAvailabilityState state) => state switch
    {
        AiAvailabilityState.Disabled or AiAvailabilityState.CapabilityDisabled => AiReadinessState.NotConfigured,
        AiAvailabilityState.Connected => AiReadinessState.ServerAvailable,
        AiAvailabilityState.ModelSelected or AiAvailabilityState.NoSuggestion => AiReadinessState.Ready,
        AiAvailabilityState.ModelUnavailable or AiAvailabilityState.NoModelsAvailable => AiReadinessState.ModelMissing,
        AiAvailabilityState.RequestRunning or AiAvailabilityState.Connecting => AiReadinessState.Running,
        AiAvailabilityState.RequestCancelled => AiReadinessState.Cancelled,
        AiAvailabilityState.Unavailable => AiReadinessState.ServerUnavailable,
        AiAvailabilityState.ResponseInvalid or AiAvailabilityState.InvalidContext => AiReadinessState.Failed,
        _ => AiReadinessState.Failed,
    };

    private void ApplyProgress(
        AiRequestProgress progress,
        CancellationTokenSource cancellation,
        long version)
    {
        if (!IsCurrentOperation(cancellation, version))
        {
            return;
        }

        ProgressStage = progress.Stage;
        ProgressText = progress.Message;
        ElapsedText = $"Elapsed: {progress.Elapsed.TotalSeconds:0.0} seconds";
        StatusText = progress.Message;
        Status = progress.Stage switch
        {
            AiRequestStage.SuggestionReady => StatusPresentation.Success(progress.Message),
            AiRequestStage.RequestCancelled => StatusPresentation.Information(progress.Message),
            AiRequestStage.RequestTimedOut => StatusPresentation.Error(progress.Message),
            AiRequestStage.RequestFailed => StatusPresentation.Error(progress.Message),
            _ => StatusPresentation.Progress(progress.Message),
        };
    }

    private static StatusPresentation PresentResult(AiAvailabilityState state, string message) => state switch
    {
        AiAvailabilityState.ModelSelected => StatusPresentation.Success(message),
        AiAvailabilityState.NoSuggestion or AiAvailabilityState.RequestCancelled => StatusPresentation.Information(message),
        AiAvailabilityState.NoModelsAvailable or AiAvailabilityState.ModelUnavailable or AiAvailabilityState.ResponseInvalid => StatusPresentation.Warning(message),
        AiAvailabilityState.Disabled or AiAvailabilityState.CapabilityDisabled or AiAvailabilityState.InvalidContext => StatusPresentation.Warning(message),
        _ => StatusPresentation.Error(message),
    };
}
