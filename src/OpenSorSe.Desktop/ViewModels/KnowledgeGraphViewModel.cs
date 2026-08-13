using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Explorer;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Desktop.Services;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Presents the optional Knowledge Graph through bounded provider-neutral pages and explicit user control.
/// </summary>
public sealed class KnowledgeGraphViewModel : ViewModelBase, IDisposable
{
    private static readonly IReadOnlyList<string> NodeKindChoices = Array.AsReadOnly(
        new[] { "All item types", "Files", "Sources", "Folders", "Collections", "Document sets", "Manual entities" });
    private static readonly IReadOnlyList<string> RelationshipTypeChoices = Array.AsReadOnly(new[] { "Manual" });
    private static readonly IReadOnlyList<IndexingResourceMode> ResourceModeChoices =
        Array.AsReadOnly(Enum.GetValues<IndexingResourceMode>());
    private readonly IGraphProjectionCoordinator? _coordinator;
    private readonly IGraphQueryService? _queryService;
    private readonly IGraphPrivacyService? _privacyService;
    private readonly IGraphRepairService? _repairService;
    private readonly IGraphDecisionService? _decisionService;
    private readonly IAdvancedDiagnosticsWindowService? _diagnosticsWindowService;
    private readonly IGraphDiagnosticsService? _graphDiagnosticsService;
    private readonly IExplorerCompanionLaunchService? _companionLaunchService;
    private readonly ObservableCollection<KnowledgeGraphNodeRow> _nodes = [];
    private readonly ObservableCollection<KnowledgeGraphNeighborRow> _neighbors = [];
    private readonly ObservableCollection<KnowledgeGraphEvidenceRow> _evidence = [];
    private readonly ObservableCollection<KnowledgeGraphTimelineRow> _timeline = [];
    private readonly ObservableCollection<KnowledgeGraphFactRow> _facts = [];
    private readonly ObservableCollection<string> _aliases = [];
    private readonly List<GraphPageCursor?> _nodePageCursors = [null];
    private readonly List<GraphPageCursor?> _neighborPageCursors = [null];
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private CancellationTokenSource? _statusCancellation;
    private CancellationTokenSource? _refreshCancellation;
    private CancellationTokenSource? _detailCancellation;
    private CancellationTokenSource? _neighborCancellation;
    private CancellationTokenSource? _evidenceCancellation;
    private CancellationTokenSource? _operationCancellation;
    private CancellationTokenSource? _companionCancellation;
    private GraphCoordinatorStatus? _coordinatorStatus;
    private GraphNodeDetails? _selectedDetails;
    private GraphPageCursor? _nextNodeCursor;
    private GraphPageCursor? _nextNeighborCursor;
    private long? _nodeTotalCount;
    private long? _neighborTotalCount;
    private int _nodePageIndex;
    private int _neighborPageIndex;
    private long _statusVersion;
    private long _refreshVersion;
    private long _detailVersion;
    private long _neighborVersion;
    private long _evidenceVersion;
    private long _focusSequence;
    private int _busyCount;
    private bool _disposed;
    private bool _isEnabled;
    private bool _hasRetainedGraphData;
    private bool _isBusy;
    private bool _isEnableConfirmationPending;
    private bool _isCancelProjectionConfirmationPending;
    private bool _isDisableConfirmationPending;
    private bool _isPrivacyConfirmationPending;
    private bool _isClearConfirmationPending;
    private bool _isRepairConfirmationPending;
    private bool _isDecisionConfirmationPending;
    private bool _isMaintenanceConfirmationPending;
    private PendingPrivacyAction _pendingPrivacyAction;
    private PendingClearAction _pendingClearAction;
    private PendingDecisionAction _pendingDecisionAction;
    private string? _pendingDecisionSubjectId;
    private string? _pendingDecisionTargetId;
    private bool _pendingDecisionPreventRegeneration;
    private GraphRepairRequest? _pendingRepairRequest;
    private StatusPresentation _status = StatusPresentation.Information(
        "Knowledge Graph is optional and has not been enabled.");
    private string _announcementText = "Knowledge Graph state has not been inspected.";
    private string _lastAnnouncementKey = string.Empty;
    private string _runStateText = "Not inspected";
    private string _jobStateText = "Not inspected";
    private string _freshnessText = "Not inspected";
    private string _integrityText = "Not inspected";
    private string _currentStageText = "Stage: none";
    private string _progressText = "Projection progress has not been inspected.";
    private string _coverageText = "Graph projection coverage has not been inspected.";
    private string _storageText = "Graph storage has not been inspected.";
    private string _waitReasonText = string.Empty;
    private double _progressValue;
    private bool _isProgressIndeterminate;
    private int _selectedSectionIndex;
    private string _filterText = string.Empty;
    private string _selectedNodeKind = NodeKindChoices[0];
    private bool _needsAttentionOnly;
    private KnowledgeGraphNodeRow? _selectedNode;
    private KnowledgeGraphNeighborRow? _selectedNeighbor;
    private string _nodeDetailText = "Select one bounded graph item to inspect it.";
    private string _manualEntityLabel = string.Empty;
    private string _aliasLabel = string.Empty;
    private string? _selectedAlias;
    private string _renameLabel = string.Empty;
    private KnowledgeGraphNodeRow? _mergeTarget;
    private KnowledgeGraphNodeRow? _linkTarget;
    private string _selectedRelationshipType = RelationshipTypeChoices[0];
    private string _manualRelationshipLabel = string.Empty;
    private string _privacyInspectionText = "Select an item, then inspect its application-owned graph data.";
    private string _privacyConfirmationText = string.Empty;
    private string _clearConfirmationText = string.Empty;
    private string _repairText = "Selective verification has not been run.";
    private string _repairConfirmationText = string.Empty;
    private string _decisionConfirmationText = string.Empty;
    private string _diagnosticsText = "Graph diagnostics have not been inspected.";
    private IndexingResourceMode _selectedResourceMode = IndexingResourceMode.Balanced;
    private bool _processOnlyWhileIdle;
    private bool _processOnlyWhileConnectedToPower;
    private int? _pauseBelowBatteryPercentage;
    private int? _processingWindowStartHour;
    private int? _processingWindowEndHour;
    private long _resourceSettingsRevision;
    private string _resourceSettingsText = "Background resource settings have not been inspected.";
    private string _maintenanceText = "Graph storage maintenance has not been inspected.";
    private string _maintenanceConfirmationText = string.Empty;
    private KnowledgeGraphFocusRequest? _lastFocusRequest;
    private bool _isCompanionLaunching;
    private string _companionStatusText = "OmniBrille is optional and has not been opened.";

    /// <summary>Initializes a preview/no-provider surface that remains safe and explanatory.</summary>
    public KnowledgeGraphViewModel()
        : this(null, null, null, null, null, null, null, null)
    {
    }

    /// <summary>Initializes the bounded Knowledge Graph presentation service.</summary>
    public KnowledgeGraphViewModel(
        IGraphProjectionCoordinator? coordinator,
        IGraphQueryService? queryService,
        IGraphPrivacyService? privacyService,
        IGraphRepairService? repairService,
        IGraphDecisionService? decisionService,
        IAdvancedDiagnosticsWindowService? diagnosticsWindowService = null,
        IGraphDiagnosticsService? graphDiagnosticsService = null,
        IExplorerCompanionLaunchService? companionLaunchService = null)
    {
        _coordinator = coordinator;
        _queryService = queryService;
        _privacyService = privacyService;
        _repairService = repairService;
        _decisionService = decisionService;
        _diagnosticsWindowService = diagnosticsWindowService;
        _graphDiagnosticsService = graphDiagnosticsService;
        _companionLaunchService = companionLaunchService;
        Nodes = new ReadOnlyObservableCollection<KnowledgeGraphNodeRow>(_nodes);
        Neighbors = new ReadOnlyObservableCollection<KnowledgeGraphNeighborRow>(_neighbors);
        Evidence = new ReadOnlyObservableCollection<KnowledgeGraphEvidenceRow>(_evidence);
        Timeline = new ReadOnlyObservableCollection<KnowledgeGraphTimelineRow>(_timeline);
        Facts = new ReadOnlyObservableCollection<KnowledgeGraphFactRow>(_facts);
        Aliases = new ReadOnlyObservableCollection<string>(_aliases);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _coordinator is not null && !IsBusy);
        OpenInOmniBrilleCommand = new AsyncRelayCommand(OpenInOmniBrilleAsync, () => _companionLaunchService is not null && !IsCompanionLaunching);
        ReconcileCommand = new AsyncRelayCommand(ReconcileAsync, CanReconcile);
        CancelCurrentCommand = new RelayCommand(CancelCurrent, () => IsBusy);
        RequestEnableCommand = new RelayCommand(RequestEnable, () => _coordinator is not null && !IsBusy && !IsEnabled);
        ConfirmEnableCommand = new AsyncRelayCommand(ConfirmEnableAsync, () => _coordinator is not null && !IsBusy && IsEnableConfirmationPending);
        CancelEnableCommand = new RelayCommand(CancelEnable, () => IsEnableConfirmationPending && !IsBusy);
        PauseCommand = new AsyncRelayCommand(PauseAsync, CanPause);
        ResumeCommand = new AsyncRelayCommand(ResumeAsync, CanResume);
        RequestCancelProjectionCommand = new RelayCommand(RequestCancelProjection, CanRequestProjectionCancellation);
        ConfirmCancelProjectionCommand = new AsyncRelayCommand(ConfirmCancelProjectionAsync, () => IsCancelProjectionConfirmationPending && !IsBusy);
        CancelCancelProjectionCommand = new RelayCommand(CancelCancelProjection, () => IsCancelProjectionConfirmationPending && !IsBusy);
        RetryCommand = new AsyncRelayCommand(RetryAsync, CanRetry);
        RequestDisableCommand = new RelayCommand(RequestDisable, () => IsEnabled && !IsBusy && !IsDisableConfirmationPending);
        ConfirmDisableCommand = new AsyncRelayCommand(ConfirmDisableAsync, () => IsDisableConfirmationPending && !IsBusy);
        CancelDisableCommand = new RelayCommand(CancelDisable, () => IsDisableConfirmationPending && !IsBusy);
        ApplyFiltersCommand = new AsyncRelayCommand(ApplyFiltersAsync, () => IsEnabled && _queryService is not null && !IsBusy);
        PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => _nodePageIndex > 0 && !IsBusy);
        NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => _nextNodeCursor is not null && !IsBusy);
        PreviousNeighborPageCommand = new AsyncRelayCommand(PreviousNeighborPageAsync, () => _neighborPageIndex > 0 && !IsBusy);
        NextNeighborPageCommand = new AsyncRelayCommand(NextNeighborPageAsync, () => _nextNeighborCursor is not null && !IsBusy);
        CreateManualEntityCommand = new AsyncRelayCommand(CreateManualEntityAsync, CanCreateManualEntity);
        RenameSelectedCommand = new AsyncRelayCommand(RenameSelectedAsync, CanRenameSelected);
        AddAliasCommand = new AsyncRelayCommand(AddAliasAsync, CanAddAlias);
        RemoveAliasCommand = new AsyncRelayCommand(RemoveAliasAsync, CanRemoveAlias);
        MergeSelectedCommand = new AsyncRelayCommand(MergeSelectedAsync, CanMergeSelected);
        SplitSelectedCommand = new AsyncRelayCommand(SplitSelectedAsync, CanSplitSelected);
        LinkSelectedCommand = new AsyncRelayCommand(LinkSelectedAsync, CanLinkSelected);
        UnlinkSelectedCommand = new AsyncRelayCommand(UnlinkSelectedAsync, CanUnlinkSelected);
        RejectSelectedSuggestionCommand = new AsyncRelayCommand(RejectSelectedSuggestionAsync, CanRejectSuggestion);
        ConfirmDecisionActionCommand = new AsyncRelayCommand(
            ConfirmDecisionActionAsync,
            () => IsDecisionConfirmationPending && !IsBusy);
        CancelDecisionActionCommand = new RelayCommand(
            CancelDecisionAction,
            () => IsDecisionConfirmationPending && !IsBusy);
        InspectSelectedCommand = new AsyncRelayCommand(InspectSelectedAsync, () => _privacyService is not null && SelectedNode is not null && !IsBusy);
        RequestExcludeSelectedCommand = new RelayCommand(RequestExcludeSelected, () => _privacyService is not null && SelectedNode is not null && !IsBusy);
        RequestForgetSelectedCommand = new RelayCommand(RequestForgetSelected, () => _privacyService is not null && SelectedNode is not null && !IsBusy);
        RequestForgetSourceCommand = new RelayCommand(RequestForgetSource, CanForgetSource);
        ConfirmPrivacyActionCommand = new AsyncRelayCommand(ConfirmPrivacyActionAsync, () => IsPrivacyConfirmationPending && !IsBusy);
        CancelPrivacyActionCommand = new RelayCommand(CancelPrivacyAction, () => IsPrivacyConfirmationPending && !IsBusy);
        RequestClearDerivedCommand = new RelayCommand(RequestClearDerived, () => _privacyService is not null && !IsBusy);
        RequestClearDecisionsCommand = new RelayCommand(RequestClearDecisions, () => _privacyService is not null && !IsBusy);
        ConfirmClearCommand = new AsyncRelayCommand(ConfirmClearAsync, () => IsClearConfirmationPending && !IsBusy);
        CancelClearCommand = new RelayCommand(CancelClear, () => IsClearConfirmationPending && !IsBusy);
        VerifySelectedCommand = new AsyncRelayCommand(VerifySelectedAsync, () => _repairService is not null && SelectedNode is not null && !IsBusy);
        RequestRepairSelectedCommand = new RelayCommand(RequestRepairSelected, () => _repairService is not null && SelectedNode is not null && !IsBusy);
        RequestRebuildSelectedCommand = new RelayCommand(RequestRebuildSelected, () => _repairService is not null && SelectedNode is not null && !IsBusy);
        RequestFullRebuildCommand = new RelayCommand(RequestFullRebuild, () => _repairService is not null && !IsBusy);
        ConfirmRepairActionCommand = new AsyncRelayCommand(ConfirmRepairActionAsync, () => IsRepairConfirmationPending && !IsBusy);
        CancelRepairActionCommand = new RelayCommand(CancelRepairAction, () => IsRepairConfirmationPending && !IsBusy);
        OpenDiagnosticsCommand = new RelayCommand(
            () => _diagnosticsWindowService?.Show(),
            () => _diagnosticsWindowService is not null);
        SaveResourceSettingsCommand = new AsyncRelayCommand(SaveResourceSettingsAsync, CanSaveResourceSettings);
        RequestMaintenanceCommand = new RelayCommand(RequestMaintenance, CanRequestMaintenance);
        ConfirmMaintenanceCommand = new AsyncRelayCommand(
            ConfirmMaintenanceAsync,
            () => IsMaintenanceConfirmationPending && !IsBusy);
        CancelMaintenanceCommand = new RelayCommand(
            CancelMaintenance,
            () => IsMaintenanceConfirmationPending && !IsBusy);

        if (_coordinator is not null)
        {
            _coordinator.StatusChanged += OnCoordinatorStatusChanged;
        }

        NotifyCommands();
    }

    /// <summary>Raised when the View should move focus after a bounded state change.</summary>
    public event Action<KnowledgeGraphFocusRequest>? FocusRequested;

    /// <summary>Gets the provider-enforced ordinary page size.</summary>
    public const int PageSize = GraphLimits.DefaultPageSize;

    /// <summary>Gets the provider hard page ceiling.</summary>
    public const int MaximumPageSize = GraphLimits.MaximumPageSize;

    /// <summary>Gets the current status presentation.</summary>
    public StatusPresentation Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Gets the coalesced screen-reader announcement.</summary>
    public string AnnouncementText
    {
        get => _announcementText;
        private set => SetProperty(ref _announcementText, value);
    }

    /// <summary>Gets whether the optional graph is enabled.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(ShowEnablement));
                OnPropertyChanged(nameof(ShowGraphWorkspace));
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets whether enablement information should be displayed.</summary>
    public bool ShowEnablement => !IsEnabled;

    /// <summary>Gets whether graph pages may be displayed.</summary>
    public bool ShowGraphWorkspace => IsEnabled || _hasRetainedGraphData;

    /// <summary>Gets the current enablement explanation.</summary>
    public string EnablementText => _coordinator is null
        ? "Knowledge Graph services are unavailable in this application configuration. Existing Search and indexing remain available."
        : "Knowledge Graph is off by default. Review the local privacy and storage explanation before enabling it.";

    /// <summary>Gets whether any current UI request or mutation is active.</summary>
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

    /// <summary>Gets whether active graph progress lacks a meaningful denominator.</summary>
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    /// <summary>Gets the graph run-control axis text.</summary>
    public string RunStateText
    {
        get => _runStateText;
        private set => SetProperty(ref _runStateText, value);
    }

    /// <summary>Gets the active graph job-execution axis text.</summary>
    public string JobStateText
    {
        get => _jobStateText;
        private set => SetProperty(ref _jobStateText, value);
    }

    /// <summary>Gets the graph freshness axis text.</summary>
    public string FreshnessText
    {
        get => _freshnessText;
        private set => SetProperty(ref _freshnessText, value);
    }

    /// <summary>Gets the graph integrity axis text.</summary>
    public string IntegrityText
    {
        get => _integrityText;
        private set => SetProperty(ref _integrityText, value);
    }

    /// <summary>Gets the current durable stage and content-free work label.</summary>
    public string CurrentStageText
    {
        get => _currentStageText;
        private set => SetProperty(ref _currentStageText, value);
    }

    /// <summary>Gets normalized graph projection progress.</summary>
    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    /// <summary>Gets projection counts and an explicitly labelled estimate when meaningful.</summary>
    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    /// <summary>Gets graph projection coverage independently from deep-index coverage.</summary>
    public string CoverageText
    {
        get => _coverageText;
        private set => SetProperty(ref _coverageText, value);
    }

    /// <summary>Gets current graph storage and its configured ceiling.</summary>
    public string StorageText
    {
        get => _storageText;
        private set => SetProperty(ref _storageText, value);
    }

    /// <summary>Gets an actionable dependency, resource, failure, or bound reason.</summary>
    public string WaitReasonText
    {
        get => _waitReasonText;
        private set
        {
            if (SetProperty(ref _waitReasonText, value))
            {
                OnPropertyChanged(nameof(HasWaitReason));
            }
        }
    }

    /// <summary>Gets whether an actionable wait or limit reason is present.</summary>
    public bool HasWaitReason => !string.IsNullOrWhiteSpace(WaitReasonText);

    /// <summary>Gets supported bounded background resource modes.</summary>
    public IReadOnlyList<IndexingResourceMode> AvailableResourceModes => ResourceModeChoices;

    /// <summary>Gets or sets the graph background resource mode.</summary>
    public IndexingResourceMode SelectedResourceMode
    {
        get => _selectedResourceMode;
        set
        {
            if (SetProperty(ref _selectedResourceMode, value))
            {
                SaveResourceSettingsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets whether graph processing requires an idle host where supported.</summary>
    public bool ProcessOnlyWhileIdle
    {
        get => _processOnlyWhileIdle;
        set => SetProperty(ref _processOnlyWhileIdle, value);
    }

    /// <summary>Gets or sets whether graph processing requires external power where supported.</summary>
    public bool ProcessOnlyWhileConnectedToPower
    {
        get => _processOnlyWhileConnectedToPower;
        set => SetProperty(ref _processOnlyWhileConnectedToPower, value);
    }

    /// <summary>Gets or sets the optional battery pause threshold.</summary>
    public int? PauseBelowBatteryPercentage
    {
        get => _pauseBelowBatteryPercentage;
        set => SetProperty(ref _pauseBelowBatteryPercentage, value);
    }

    /// <summary>Gets or sets the optional inclusive local processing-window start hour.</summary>
    public int? ProcessingWindowStartHour
    {
        get => _processingWindowStartHour;
        set => SetProperty(ref _processingWindowStartHour, value);
    }

    /// <summary>Gets or sets the optional exclusive local processing-window end hour.</summary>
    public int? ProcessingWindowEndHour
    {
        get => _processingWindowEndHour;
        set => SetProperty(ref _processingWindowEndHour, value);
    }

    /// <summary>Gets a plain-language summary of persisted resource controls and platform degradation.</summary>
    public string ResourceSettingsText
    {
        get => _resourceSettingsText;
        private set => SetProperty(ref _resourceSettingsText, value);
    }

    /// <summary>Gets the latest privacy-minimized graph diagnostics snapshot.</summary>
    public string DiagnosticsText
    {
        get => _diagnosticsText;
        private set => SetProperty(ref _diagnosticsText, value);
    }

    /// <summary>Gets the latest bounded graph storage-maintenance state.</summary>
    public string MaintenanceText
    {
        get => _maintenanceText;
        private set => SetProperty(ref _maintenanceText, value);
    }

    /// <summary>Gets reviewed graph storage-maintenance confirmation wording.</summary>
    public string MaintenanceConfirmationText
    {
        get => _maintenanceConfirmationText;
        private set => SetProperty(ref _maintenanceConfirmationText, value);
    }

    /// <summary>Gets whether graph-only storage maintenance awaits explicit confirmation.</summary>
    public bool IsMaintenanceConfirmationPending
    {
        get => _isMaintenanceConfirmationPending;
        private set
        {
            if (SetProperty(ref _isMaintenanceConfirmationPending, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets or sets the selected stable page section.</summary>
    public int SelectedSectionIndex
    {
        get => _selectedSectionIndex;
        set => SetProperty(ref _selectedSectionIndex, Math.Clamp(value, 0, 3));
    }

    /// <summary>Gets available stable node-kind filters.</summary>
    public IReadOnlyList<string> AvailableNodeKinds => NodeKindChoices;

    /// <summary>Gets or sets the selected stable node-kind filter.</summary>
    public string SelectedNodeKind
    {
        get => _selectedNodeKind;
        set => SetProperty(ref _selectedNodeKind, value ?? NodeKindChoices[0]);
    }

    /// <summary>Gets or sets a bounded label-prefix filter.</summary>
    public string FilterText
    {
        get => _filterText;
        set => SetProperty(ref _filterText, value ?? string.Empty);
    }

    /// <summary>Gets or sets whether only repair-required items are requested.</summary>
    public bool NeedsAttentionOnly
    {
        get => _needsAttentionOnly;
        set => SetProperty(ref _needsAttentionOnly, value);
    }

    /// <summary>Gets the current bounded node page.</summary>
    public ReadOnlyObservableCollection<KnowledgeGraphNodeRow> Nodes { get; }

    /// <summary>Gets the current bounded direct-neighbor page.</summary>
    public ReadOnlyObservableCollection<KnowledgeGraphNeighborRow> Neighbors { get; }

    /// <summary>Gets evidence for the selected actual edge.</summary>
    public ReadOnlyObservableCollection<KnowledgeGraphEvidenceRow> Evidence { get; }

    /// <summary>Gets bounded timestamped facts for the selected node.</summary>
    public ReadOnlyObservableCollection<KnowledgeGraphTimelineRow> Timeline { get; }

    /// <summary>Gets or sets the selected node page row.</summary>
    public KnowledgeGraphNodeRow? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                RenameLabel = value?.Title ?? string.Empty;
                MergeTarget = null;
                LinkTarget = null;
                ClearNodeInspection();
                NotifySelectionChanged();
                var version = Interlocked.Increment(ref _detailVersion);
                CancelAndDispose(ref _detailCancellation);
                CancelAndDispose(ref _neighborCancellation);
                CancelAndDispose(ref _evidenceCancellation);
                if (value is not null && _queryService is not null)
                {
                    _ = LoadNodeDetailAsync(value.Id, version);
                }
            }
        }
    }

    /// <summary>Gets or sets the selected direct-neighbor edge.</summary>
    public KnowledgeGraphNeighborRow? SelectedNeighbor
    {
        get => _selectedNeighbor;
        set
        {
            if (SetProperty(ref _selectedNeighbor, value))
            {
                Replace(_evidence, []);
                OnPropertyChanged(nameof(HasSelectedNeighbor));
                NotifyCommands();
                var version = Interlocked.Increment(ref _evidenceVersion);
                CancelAndDispose(ref _evidenceCancellation);
                if (value is not null && _queryService is not null)
                {
                    _ = LoadEvidenceAsync(value.EdgeId, version);
                }
            }
        }
    }

    /// <summary>Gets whether a node is selected.</summary>
    public bool HasSelectedNode => SelectedNode is not null;

    /// <summary>Gets whether a direct edge is selected.</summary>
    public bool HasSelectedNeighbor => SelectedNeighbor is not null;

    /// <summary>Gets whether the selected node has timestamped retained facts.</summary>
    public bool HasTimeline => Timeline.Count > 0;

    /// <summary>Gets whether the selected node has bounded evidence-backed facts.</summary>
    public bool HasFacts => Facts.Count > 0;

    /// <summary>Gets bounded node-inspector facts.</summary>
    public string NodeDetailText
    {
        get => _nodeDetailText;
        private set => SetProperty(ref _nodeDetailText, value);
    }

    /// <summary>Gets the current node-page position.</summary>
    public string PageText => FormatPageText(_nodePageIndex, Nodes.Count, _nodeTotalCount);

    /// <summary>Gets the current direct-neighbor-page position.</summary>
    public string NeighborPageText => FormatPageText(_neighborPageIndex, Neighbors.Count, _neighborTotalCount, "related items");

    /// <summary>Gets or sets a bounded new manual-entity label.</summary>
    public string ManualEntityLabel
    {
        get => _manualEntityLabel;
        set
        {
            if (SetProperty(ref _manualEntityLabel, value ?? string.Empty))
            {
                CreateManualEntityCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets a bounded alias for the selected manual entity.</summary>
    public string AliasLabel
    {
        get => _aliasLabel;
        set
        {
            if (SetProperty(ref _aliasLabel, value ?? string.Empty))
            {
                AddAliasCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets bounded aliases retained for the selected node.</summary>
    public ReadOnlyObservableCollection<string> Aliases { get; }

    /// <summary>Gets bounded evidence-backed facts for the selected graph item.</summary>
    public ReadOnlyObservableCollection<KnowledgeGraphFactRow> Facts { get; }

    /// <summary>Gets or sets the alias selected for reviewed removal.</summary>
    public string? SelectedAlias
    {
        get => _selectedAlias;
        set
        {
            if (SetProperty(ref _selectedAlias, value))
            {
                RemoveAliasCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets a bounded replacement label for a manual entity.</summary>
    public string RenameLabel
    {
        get => _renameLabel;
        set
        {
            if (SetProperty(ref _renameLabel, value ?? string.Empty))
            {
                RenameSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the reviewed compatible merge target.</summary>
    public KnowledgeGraphNodeRow? MergeTarget
    {
        get => _mergeTarget;
        set
        {
            if (SetProperty(ref _mergeTarget, value))
            {
                MergeSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the manual-link target.</summary>
    public KnowledgeGraphNodeRow? LinkTarget
    {
        get => _linkTarget;
        set
        {
            if (SetProperty(ref _linkTarget, value))
            {
                LinkSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets the stable relationship-type choices for manual links.</summary>
    public IReadOnlyList<string> AvailableRelationshipTypes => RelationshipTypeChoices;

    /// <summary>Gets or sets the selected manual relationship type.</summary>
    public string SelectedRelationshipType
    {
        get => _selectedRelationshipType;
        set => SetProperty(ref _selectedRelationshipType, value ?? RelationshipTypeChoices[0]);
    }

    /// <summary>Gets or sets a bounded manual-link reason.</summary>
    public string ManualRelationshipLabel
    {
        get => _manualRelationshipLabel;
        set
        {
            if (SetProperty(ref _manualRelationshipLabel, value ?? string.Empty))
            {
                LinkSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets content-free counts for the currently inspected privacy scope.</summary>
    public string PrivacyInspectionText
    {
        get => _privacyInspectionText;
        private set => SetProperty(ref _privacyInspectionText, value);
    }

    /// <summary>Gets the current explicit privacy confirmation wording.</summary>
    public string PrivacyConfirmationText
    {
        get => _privacyConfirmationText;
        private set => SetProperty(ref _privacyConfirmationText, value);
    }

    /// <summary>Gets the current explicit clear confirmation wording.</summary>
    public string ClearConfirmationText
    {
        get => _clearConfirmationText;
        private set => SetProperty(ref _clearConfirmationText, value);
    }

    /// <summary>Gets the latest selective repair result.</summary>
    public string RepairText
    {
        get => _repairText;
        private set => SetProperty(ref _repairText, value);
    }

    /// <summary>Gets the current reviewed repair confirmation.</summary>
    public string RepairConfirmationText
    {
        get => _repairConfirmationText;
        private set => SetProperty(ref _repairConfirmationText, value);
    }

    /// <summary>Gets the reviewed manual-correction confirmation wording.</summary>
    public string DecisionConfirmationText
    {
        get => _decisionConfirmationText;
        private set => SetProperty(ref _decisionConfirmationText, value);
    }

    /// <summary>Gets whether enablement awaits explicit consent.</summary>
    public bool IsEnableConfirmationPending
    {
        get => _isEnableConfirmationPending;
        private set
        {
            if (SetProperty(ref _isEnableConfirmationPending, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets whether durable projection cancellation awaits confirmation.</summary>
    public bool IsCancelProjectionConfirmationPending
    {
        get => _isCancelProjectionConfirmationPending;
        private set
        {
            if (SetProperty(ref _isCancelProjectionConfirmationPending, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets whether disable-and-retain awaits confirmation.</summary>
    public bool IsDisableConfirmationPending
    {
        get => _isDisableConfirmationPending;
        private set
        {
            if (SetProperty(ref _isDisableConfirmationPending, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets whether a scoped privacy action awaits confirmation.</summary>
    public bool IsPrivacyConfirmationPending
    {
        get => _isPrivacyConfirmationPending;
        private set
        {
            if (SetProperty(ref _isPrivacyConfirmationPending, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets whether a graph clear action awaits confirmation.</summary>
    public bool IsClearConfirmationPending
    {
        get => _isClearConfirmationPending;
        private set
        {
            if (SetProperty(ref _isClearConfirmationPending, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets whether a selective or full repair awaits confirmation.</summary>
    public bool IsRepairConfirmationPending
    {
        get => _isRepairConfirmationPending;
        private set
        {
            if (SetProperty(ref _isRepairConfirmationPending, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets whether a merge, split, unlink, or rejection awaits explicit confirmation.</summary>
    public bool IsDecisionConfirmationPending
    {
        get => _isDecisionConfirmationPending;
        private set
        {
            if (SetProperty(ref _isDecisionConfirmationPending, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets the most recent deterministic focus request for tests and View consumption.</summary>
    public KnowledgeGraphFocusRequest? LastFocusRequest
    {
        get => _lastFocusRequest;
        private set => SetProperty(ref _lastFocusRequest, value);
    }

    /// <summary>Gets whether one bounded companion launch is awaiting acknowledgement.</summary>
    public bool IsCompanionLaunching
    {
        get => _isCompanionLaunching;
        private set
        {
            if (SetProperty(ref _isCompanionLaunching, value))
            {
                OpenInOmniBrilleCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets the current optional-companion launch state.</summary>
    public string CompanionStatusText
    {
        get => _companionStatusText;
        private set => SetProperty(ref _companionStatusText, value);
    }

    /// <summary>Gets the bounded refresh command.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }
    /// <summary>Gets the lazy, scoped OmniBrille launch command.</summary>
    public IAsyncRelayCommand OpenInOmniBrilleCommand { get; }
    /// <summary>Gets the explicit durable projection reconciliation command.</summary>
    public IAsyncRelayCommand ReconcileCommand { get; }
    /// <summary>Gets the cancellation command for the current UI request.</summary>
    public IRelayCommand CancelCurrentCommand { get; }
    /// <summary>Gets the first-enable consent request command.</summary>
    public IRelayCommand RequestEnableCommand { get; }
    /// <summary>Gets the confirmed first-enable command.</summary>
    public IAsyncRelayCommand ConfirmEnableCommand { get; }
    /// <summary>Gets the enable-consent cancellation command.</summary>
    public IRelayCommand CancelEnableCommand { get; }
    /// <summary>Gets the durable pause command.</summary>
    public IAsyncRelayCommand PauseCommand { get; }
    /// <summary>Gets the durable resume command.</summary>
    public IAsyncRelayCommand ResumeCommand { get; }
    /// <summary>Gets the projection-cancellation request command.</summary>
    public IRelayCommand RequestCancelProjectionCommand { get; }
    /// <summary>Gets the confirmed durable projection-cancellation command.</summary>
    public IAsyncRelayCommand ConfirmCancelProjectionCommand { get; }
    /// <summary>Gets the projection-cancellation confirmation dismissal command.</summary>
    public IRelayCommand CancelCancelProjectionCommand { get; }
    /// <summary>Gets the eligible-failure retry command.</summary>
    public IAsyncRelayCommand RetryCommand { get; }
    /// <summary>Gets the disable-and-retain request command.</summary>
    public IRelayCommand RequestDisableCommand { get; }
    /// <summary>Gets the confirmed disable-and-retain command.</summary>
    public IAsyncRelayCommand ConfirmDisableCommand { get; }
    /// <summary>Gets the disable confirmation dismissal command.</summary>
    public IRelayCommand CancelDisableCommand { get; }
    /// <summary>Gets the node-filter application command.</summary>
    public IAsyncRelayCommand ApplyFiltersCommand { get; }
    /// <summary>Gets the previous node-page command.</summary>
    public IAsyncRelayCommand PreviousPageCommand { get; }
    /// <summary>Gets the next node-page command.</summary>
    public IAsyncRelayCommand NextPageCommand { get; }
    /// <summary>Gets the previous neighbor-page command.</summary>
    public IAsyncRelayCommand PreviousNeighborPageCommand { get; }
    /// <summary>Gets the next neighbor-page command.</summary>
    public IAsyncRelayCommand NextNeighborPageCommand { get; }
    /// <summary>Gets the manual-entity creation command.</summary>
    public IAsyncRelayCommand CreateManualEntityCommand { get; }
    /// <summary>Gets the manual-entity rename command.</summary>
    public IAsyncRelayCommand RenameSelectedCommand { get; }
    /// <summary>Gets the bounded add-alias command.</summary>
    public IAsyncRelayCommand AddAliasCommand { get; }
    /// <summary>Gets the reviewed remove-alias command.</summary>
    public IAsyncRelayCommand RemoveAliasCommand { get; }
    /// <summary>Gets the compatible reviewed merge command.</summary>
    public IAsyncRelayCommand MergeSelectedCommand { get; }
    /// <summary>Gets the compatible reviewed split command.</summary>
    public IAsyncRelayCommand SplitSelectedCommand { get; }
    /// <summary>Gets the manual-link command.</summary>
    public IAsyncRelayCommand LinkSelectedCommand { get; }
    /// <summary>Gets the selected-edge unlink command.</summary>
    public IAsyncRelayCommand UnlinkSelectedCommand { get; }
    /// <summary>Gets the inactive-suggestion rejection command.</summary>
    public IAsyncRelayCommand RejectSelectedSuggestionCommand { get; }
    /// <summary>Gets the confirmed reviewed graph-correction command.</summary>
    public IAsyncRelayCommand ConfirmDecisionActionCommand { get; }
    /// <summary>Gets the reviewed graph-correction confirmation dismissal command.</summary>
    public IRelayCommand CancelDecisionActionCommand { get; }
    /// <summary>Gets the privacy inspection command.</summary>
    public IAsyncRelayCommand InspectSelectedCommand { get; }
    /// <summary>Gets the selected-node exclusion request.</summary>
    public IRelayCommand RequestExcludeSelectedCommand { get; }
    /// <summary>Gets the selected-node forget request.</summary>
    public IRelayCommand RequestForgetSelectedCommand { get; }
    /// <summary>Gets the selected-source forget request.</summary>
    public IRelayCommand RequestForgetSourceCommand { get; }
    /// <summary>Gets the confirmed scoped privacy mutation.</summary>
    public IAsyncRelayCommand ConfirmPrivacyActionCommand { get; }
    /// <summary>Gets the scoped privacy confirmation dismissal.</summary>
    public IRelayCommand CancelPrivacyActionCommand { get; }
    /// <summary>Gets the clear-derived request.</summary>
    public IRelayCommand RequestClearDerivedCommand { get; }
    /// <summary>Gets the irreversible clear-decisions request.</summary>
    public IRelayCommand RequestClearDecisionsCommand { get; }
    /// <summary>Gets the confirmed graph clear command.</summary>
    public IAsyncRelayCommand ConfirmClearCommand { get; }
    /// <summary>Gets the graph clear confirmation dismissal.</summary>
    public IRelayCommand CancelClearCommand { get; }
    /// <summary>Gets the selected-component verify command.</summary>
    public IAsyncRelayCommand VerifySelectedCommand { get; }
    /// <summary>Gets the selective repair request.</summary>
    public IRelayCommand RequestRepairSelectedCommand { get; }
    /// <summary>Gets the selective rebuild request.</summary>
    public IRelayCommand RequestRebuildSelectedCommand { get; }
    /// <summary>Gets the last-resort full derived rebuild request.</summary>
    public IRelayCommand RequestFullRebuildCommand { get; }
    /// <summary>Gets the confirmed repair/rebuild command.</summary>
    public IAsyncRelayCommand ConfirmRepairActionCommand { get; }
    /// <summary>Gets the repair confirmation dismissal.</summary>
    public IRelayCommand CancelRepairActionCommand { get; }
    /// <summary>Gets the privacy-safe advanced diagnostics command.</summary>
    public IRelayCommand OpenDiagnosticsCommand { get; }
    /// <summary>Gets the command that saves reviewed graph background resource controls.</summary>
    public IAsyncRelayCommand SaveResourceSettingsCommand { get; }
    /// <summary>Gets the reviewed graph-only maintenance request command.</summary>
    public IRelayCommand RequestMaintenanceCommand { get; }
    /// <summary>Gets the confirmed graph-only maintenance command.</summary>
    public IAsyncRelayCommand ConfirmMaintenanceCommand { get; }
    /// <summary>Gets the graph-only maintenance confirmation dismissal command.</summary>
    public IRelayCommand CancelMaintenanceCommand { get; }

    private async Task OpenInOmniBrilleAsync()
    {
        if (_companionLaunchService is null || IsCompanionLaunching)
        {
            return;
        }

        var cancellation = ReplaceCancellation(ref _companionCancellation);
        IsCompanionLaunching = true;
        CompanionStatusText = "Opening OmniBrille and authorizing the current indexed sources...";
        try
        {
            var result = await _companionLaunchService.LaunchAsync(cancellation.Token).ConfigureAwait(false);
            await ApplyOnUiThreadAsync(() => CompanionStatusText = result.Message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await ApplyOnUiThreadAsync(() => CompanionStatusText = "Opening OmniBrille was cancelled.");
        }
        catch (Exception)
        {
            await ApplyOnUiThreadAsync(() => CompanionStatusText = "OmniBrille could not be opened safely. OmniSorSe remains available.");
        }
        finally
        {
            ReleaseCancellation(ref _companionCancellation, cancellation);
            await ApplyOnUiThreadAsync(() => IsCompanionLaunching = false);
        }
    }

    /// <summary>Refreshes durable state and the current bounded first page.</summary>
    public async Task RefreshAsync()
    {
        if (_coordinator is null)
        {
            Status = StatusPresentation.Warning(
                "Knowledge Graph services are unavailable. Existing Search and indexing remain available.");
            IsEnabled = false;
            RequestFocus(KnowledgeGraphFocusTarget.EnableControl);
            return;
        }

        var version = Interlocked.Increment(ref _statusVersion);
        var cancellation = ReplaceCancellation(ref _statusCancellation);
        BeginBusy();
        try
        {
            var status = await _coordinator.GetStatusAsync(cancellation.Token);
            if (!IsCurrent(version, Volatile.Read(ref _statusVersion), cancellation))
            {
                return;
            }

            await ApplyOnUiThreadAsync(() => ApplyCoordinatorStatus(status, announce: true));
            await RefreshAuxiliaryStateAsync(cancellation.Token);
            if (!status.IsEnabled)
            {
                await ApplyOnUiThreadAsync(ClearAllPages);
                return;
            }

            ResetNodePaging();
            await LoadNodePageAsync(null, 0, SelectedNode?.Id, requestFocus: false, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer refresh or explicit cancellation owns presentation now.
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _statusVersion))
            {
                await ApplyOnUiThreadAsync(() =>
                {
                    Status = StatusPresentation.Warning(
                        "Knowledge Graph state could not be refreshed safely. Existing Search and indexing remain available.");
                    Announce("Knowledge Graph refresh failed safely.", force: true);
                });
            }
        }
        finally
        {
            ReleaseCancellation(ref _statusCancellation, cancellation);
            EndBusy();
        }
    }

    private async Task RefreshAuxiliaryStateAsync(CancellationToken cancellationToken)
    {
        if (_coordinator is not null)
        {
            try
            {
                var settings = await _coordinator.GetControlSettingsAsync(cancellationToken);
                await ApplyOnUiThreadAsync(() => ApplyResourceSettings(settings));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                await ApplyOnUiThreadAsync(() =>
                    ResourceSettingsText = "Background resource settings are temporarily unavailable; no setting was changed.");
            }
        }

        if (_graphDiagnosticsService is null)
        {
            return;
        }

        try
        {
            var diagnostics = await _graphDiagnosticsService.GetSnapshotAsync(cancellationToken);
            await ApplyOnUiThreadAsync(() => DiagnosticsText = FormatDiagnostics(diagnostics));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await ApplyOnUiThreadAsync(() =>
                DiagnosticsText = "Knowledge Graph diagnostics are temporarily unavailable. No cached diagnostic content is shown.");
        }
    }

    private void ApplyResourceSettings(GraphControlSettings settings)
    {
        SelectedResourceMode = settings.ResourceMode;
        ProcessOnlyWhileIdle = settings.ProcessOnlyWhileIdle;
        ProcessOnlyWhileConnectedToPower = settings.ProcessOnlyWhileConnectedToPower;
        PauseBelowBatteryPercentage = settings.PauseBelowBatteryPercentage;
        ProcessingWindowStartHour = settings.ProcessingWindowStartHour;
        ProcessingWindowEndHour = settings.ProcessingWindowEndHour;
        _resourceSettingsRevision = settings.Revision;
        ResourceSettingsText = FormatResourceSettings(settings);
        SaveResourceSettingsCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadNodePageAsync(
        GraphPageCursor? cursor,
        int pageIndex,
        string? preferredNodeId,
        bool requestFocus,
        CancellationToken outerCancellation = default)
    {
        if (_queryService is null || !IsEnabled)
        {
            return;
        }

        if (!TryNormalizeFilter(out var normalizedFilter))
        {
            return;
        }

        var version = Interlocked.Increment(ref _refreshVersion);
        var cancellation = ReplaceCancellation(ref _refreshCancellation, outerCancellation);
        BeginBusy();
        try
        {
            var query = new GraphNodeQuery(
                ResolveNodeKind(SelectedNodeKind),
                normalizedFilter,
                Integrity: NeedsAttentionOnly ? GraphIntegrityState.RepairRequired : null,
                Cursor: cursor,
                PageSize: PageSize);
            var page = await _queryService.GetNodesPageAsync(query, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(version, _refreshVersion, cancellation))
            {
                return;
            }

            await ApplyOnUiThreadAsync(() => ApplyNodePage(page, cursor, pageIndex, preferredNodeId, requestFocus));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Superseded pages never replace a newer immutable snapshot.
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _refreshVersion))
            {
                await ApplyOnUiThreadAsync(() =>
                {
                    ClearAllPages();
                    Status = StatusPresentation.Warning("The requested graph page is temporarily unavailable. No partial page was shown.");
                    Announce("Graph page unavailable; cached graph data was hidden until authority is current.", force: true);
                });
            }
        }
        finally
        {
            ReleaseCancellation(ref _refreshCancellation, cancellation);
            EndBusy();
        }
    }

    private async Task LoadNodeDetailAsync(string nodeId, long version)
    {
        if (_queryService is null)
        {
            return;
        }

        var cancellation = ReplaceCancellation(ref _detailCancellation);
        BeginBusy();
        try
        {
            var details = await _queryService.GetNodeDetailAsync(nodeId, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(version, _detailVersion, cancellation) ||
                !string.Equals(SelectedNode?.Id, nodeId, StringComparison.Ordinal))
            {
                return;
            }

            if (details is null)
            {
                await ApplyOnUiThreadAsync(() =>
                {
                    NodeDetailText = "The selected graph item is no longer available. Refresh the current page.";
                    Status = StatusPresentation.Warning("The selected graph item changed before it could be inspected.");
                });
                return;
            }

            await ApplyOnUiThreadAsync(() => ApplyNodeDetails(details));
            await LoadFactsAndTimelineAsync(nodeId, version, cancellation.Token);
            ResetNeighborPaging();
            await LoadNeighborPageAsync(nodeId, null, 0, requestFocus: false, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A later selection owns the inspector.
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _detailVersion))
            {
                await ApplyOnUiThreadAsync(() =>
                {
                    ClearNodeInspection();
                    NodeDetailText = "The selected graph item could not be inspected safely.";
                    Status = StatusPresentation.Warning("Graph item inspection failed safely. Original files were not affected.");
                });
            }
        }
        finally
        {
            ReleaseCancellation(ref _detailCancellation, cancellation);
            EndBusy();
        }
    }

    private async Task LoadFactsAndTimelineAsync(
        string nodeId,
        long version,
        CancellationToken cancellationToken)
    {
        if (_queryService is null)
        {
            return;
        }

        var facts = await _queryService.GetFactsPageAsync(
            new GraphFactQuery(nodeId, PageSize: PageSize),
            cancellationToken);
        var timeline = await _queryService.GetTimelinePageAsync(
            new GraphTimelineQuery(nodeId, PageSize: PageSize),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (version != Volatile.Read(ref _detailVersion) ||
            !string.Equals(SelectedNode?.Id, nodeId, StringComparison.Ordinal))
        {
            return;
        }

        await ApplyOnUiThreadAsync(() =>
        {
            Replace(_facts, facts.Items.Take(PageSize).Select(ToFactRow));
            Replace(_timeline, timeline.Items.Take(PageSize).Select(ToTimelineRow));
            OnPropertyChanged(nameof(HasFacts));
            OnPropertyChanged(nameof(HasTimeline));
        });
    }

    private async Task LoadNeighborPageAsync(
        string nodeId,
        GraphPageCursor? cursor,
        int pageIndex,
        bool requestFocus,
        CancellationToken outerCancellation = default)
    {
        if (_queryService is null)
        {
            return;
        }

        var version = Interlocked.Increment(ref _neighborVersion);
        var cancellation = ReplaceCancellation(ref _neighborCancellation, outerCancellation);
        BeginBusy();
        try
        {
            var page = await _queryService.GetNeighborsPageAsync(
                new GraphNeighborQuery(
                    nodeId,
                    Cursor: cursor,
                    PageSize: PageSize,
                    Depth: GraphLimits.StableTraversalDepth,
                    ExperimentalTraversal: false),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(version, _neighborVersion, cancellation) ||
                !string.Equals(SelectedNode?.Id, nodeId, StringComparison.Ordinal))
            {
                return;
            }

            await ApplyOnUiThreadAsync(() => ApplyNeighborPage(page, cursor, pageIndex, requestFocus));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A later selection/page owns the direct-neighbor list.
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _neighborVersion))
            {
                await ApplyOnUiThreadAsync(() =>
                {
                    SelectedNeighbor = null;
                    Replace(_neighbors, []);
                    Replace(_evidence, []);
                    ResetNeighborPaging();
                    OnPropertyChanged(nameof(HasSelectedNeighbor));
                    OnPropertyChanged(nameof(NeighborPageText));
                    Status = StatusPresentation.Warning("Direct related items are temporarily unavailable. No partial graph was shown.");
                    Announce("Direct related items are unavailable; cached relationships were hidden.", force: true);
                });
            }
        }
        finally
        {
            ReleaseCancellation(ref _neighborCancellation, cancellation);
            EndBusy();
        }
    }

    private async Task LoadEvidenceAsync(string edgeId, long version)
    {
        if (_queryService is null)
        {
            return;
        }

        var cancellation = ReplaceCancellation(ref _evidenceCancellation);
        BeginBusy();
        try
        {
            var evidence = await _queryService.GetEvidenceAsync(edgeId, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(version, _evidenceVersion, cancellation) ||
                !string.Equals(SelectedNeighbor?.EdgeId, edgeId, StringComparison.Ordinal))
            {
                return;
            }

            await ApplyOnUiThreadAsync(() =>
            {
                Replace(_evidence, evidence.Take(GraphLimits.MaximumEvidencePerEdge).Select(ToEvidenceRow));
                if (evidence.Count > GraphLimits.MaximumEvidencePerEdge)
                {
                    Status = StatusPresentation.Warning("Additional malformed or excessive evidence was not displayed.");
                }
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A later edge owns evidence inspection.
        }
        catch (Exception)
        {
            if (version == Volatile.Read(ref _evidenceVersion))
            {
                await ApplyOnUiThreadAsync(() =>
                {
                    Replace(_evidence, []);
                    Status = StatusPresentation.Warning("Evidence for this relationship could not be inspected safely.");
                });
            }
        }
        finally
        {
            ReleaseCancellation(ref _evidenceCancellation, cancellation);
            EndBusy();
        }
    }

    private void ApplyCoordinatorStatus(GraphCoordinatorStatus status, bool announce)
    {
        _coordinatorStatus = status;
        _hasRetainedGraphData = status.StorageBreakdown.TotalBytes > 0 || status.StorageSizeBytes > 0;
        OnPropertyChanged(nameof(ShowGraphWorkspace));
        IsEnabled = status.IsEnabled;
        RunStateText = FormatRunState(status.RunControl);
        JobStateText = FormatJobState(status.ActiveJobState, status);
        FreshnessText = FormatFreshness(status.Freshness);
        IntegrityText = FormatIntegrity(status.Integrity);
        CurrentStageText = FormatStage(status.CurrentStage, status.CurrentWorkLabel);
        var total = Math.Max(0, status.TotalObservationCount);
        var processed = Math.Clamp(status.ProcessedObservationCount, 0, total == 0 ? long.MaxValue : total);
        ProgressValue = total > 0 ? Math.Clamp(processed / (double)total, 0, 1) : 0;
        IsProgressIndeterminate = status.IsEnabled && total == 0 &&
            status.RunControl is GraphRunControlState.Pending or GraphRunControlState.Running;
        ProgressText = total > 0
            ? $"Processed {processed:N0} of {total:N0} observations; {Math.Max(0, status.RemainingObservationCount):N0} remaining." +
              (status.EstimatedRemaining is { } estimate ? $" Estimated time remaining: {FormatDuration(estimate)}." : string.Empty)
            : "Projection totals will appear after a complete input manifest is available.";
        CoverageText = FormatCoverage(status.Coverage);
        StorageText = FormatStorage(status);
        MaintenanceText = FormatMaintenance(status.Maintenance);
        WaitReasonText = FormatWaitReason(status);
        Status = PresentStatus(status);
        if (announce)
        {
            AnnounceStatus(status);
        }

        if (!status.IsEnabled || status.Freshness != GraphFreshnessState.Current ||
            status.Integrity != GraphIntegrityState.Valid || !status.Coverage.IsAvailable || status.Coverage.IsStale)
        {
            ClearAllPages();
        }

        NotifyCommands();
    }

    private void ApplyNodePage(
        GraphPage<GraphNode> page,
        GraphPageCursor? cursor,
        int pageIndex,
        string? preferredNodeId,
        bool requestFocus)
    {
        var rows = page.Items.Take(PageSize).Select(ToNodeRow).ToArray();
        Replace(_nodes, rows);
        _nodePageIndex = Math.Max(0, pageIndex);
        _nodeTotalCount = page.TotalCount;
        _nextNodeCursor = page.NextCursor;
        RecordCursor(_nodePageCursors, _nodePageIndex, cursor);
        var preserved = preferredNodeId is null
            ? null
            : rows.FirstOrDefault(item => string.Equals(item.Id, preferredNodeId, StringComparison.Ordinal));
        SelectedNode = preserved ?? rows.FirstOrDefault();
        OnPropertyChanged(nameof(PageText));
        if (page.Items.Count > PageSize)
        {
            Status = StatusPresentation.Warning(
                $"The provider returned more than the {PageSize} row UI page limit. Excess rows were ignored safely.");
        }
        else
        {
            Status = rows.Length == 0
                ? StatusPresentation.Information("No graph items match this bounded page and filter.")
                : StatusPresentation.Success($"Loaded {rows.Length:N0} graph item(s) without materializing the complete graph.");
        }

        NotifyCommands();
        if (requestFocus)
        {
            RequestFocus(KnowledgeGraphFocusTarget.NodeList, SelectedNode?.Id);
        }
    }

    private void ApplyNodeDetails(GraphNodeDetails details)
    {
        _selectedDetails = details;
        var aliases = details.Aliases.Take(GraphLimits.MaximumAliasesPerNode).ToArray();
        var aliasesText = aliases.Length == 0 ? "No aliases are stored." : $"Aliases: {string.Join(", ", aliases)}.";
        NodeDetailText =
            $"{aliasesText} Incoming connections: {details.IncomingEdgeCount:N0}; outgoing connections: {details.OutgoingEdgeCount:N0}. " +
            $"Origin: {FormatOrigin(details.Node.Origin)}. Algorithm: {details.Node.Algorithm} {details.Node.AlgorithmVersion}. " +
            $"Created {FormatTime(details.Node.CreatedAtUtc)}; last validated {FormatTime(details.Node.LastValidatedAtUtc)}.";
        Replace(_aliases, aliases);
        SelectedAlias = null;
        NotifyCommands();
    }

    private void ApplyNeighborPage(
        GraphPage<GraphNeighbor> page,
        GraphPageCursor? cursor,
        int pageIndex,
        bool requestFocus)
    {
        var bounded = page.Items.Take(PageSize).ToArray();
        Replace(_neighbors, bounded.Select(ToNeighborRow));
        _neighborPageIndex = Math.Max(0, pageIndex);
        _neighborTotalCount = page.TotalCount;
        _nextNeighborCursor = page.NextCursor;
        RecordCursor(_neighborPageCursors, _neighborPageIndex, cursor);
        SelectedNeighbor = null;
        OnPropertyChanged(nameof(NeighborPageText));
        if (page.Items.Count > PageSize)
        {
            Status = StatusPresentation.Warning(
                $"The provider returned more than the {PageSize} direct-neighbor UI limit. Excess rows were ignored safely.");
        }

        NotifyCommands();
        if (requestFocus)
        {
            RequestFocus(KnowledgeGraphFocusTarget.NeighborList);
        }
    }

    private void RequestEnable()
    {
        IsEnableConfirmationPending = true;
        Status = StatusPresentation.Information("Review and confirm local Knowledge Graph storage and processing.");
        RequestFocus(KnowledgeGraphFocusTarget.EnableControl);
    }

    private async Task ConfirmEnableAsync()
    {
        if (_coordinator is null)
        {
            return;
        }

        IsEnableConfirmationPending = false;
        await RunMutationAsync(
            token => RunThenReconcileAsync(
                innerToken => _coordinator.EnableAsync(consentConfirmed: true, innerToken),
                token),
            KnowledgeGraphFocusTarget.PageHeading,
            refreshAfterSuccess: true);
    }

    private void CancelEnable()
    {
        IsEnableConfirmationPending = false;
        Status = StatusPresentation.Information("Knowledge Graph remains disabled. Existing features are unchanged.");
        RequestFocus(KnowledgeGraphFocusTarget.EnableControl);
    }

    private async Task SaveResourceSettingsAsync()
    {
        if (_coordinator is null || !CanSaveResourceSettings())
        {
            return;
        }

        if (!Enum.IsDefined(SelectedResourceMode) || PauseBelowBatteryPercentage is < 1 or > 100 ||
            ProcessingWindowStartHour is < 0 or > 23 || ProcessingWindowEndHour is < 0 or > 23 ||
            ProcessingWindowStartHour.HasValue != ProcessingWindowEndHour.HasValue ||
            (ProcessingWindowStartHour.HasValue && ProcessingWindowStartHour == ProcessingWindowEndHour))
        {
            Status = StatusPresentation.Warning(
                "Review resource settings: battery threshold must be 1–100, and a processing window needs distinct start and end hours from 0–23.");
            return;
        }

        GraphControlSettings? saved = null;
        var result = await RunMutationAsync(
            async token =>
            {
                saved = await _coordinator.UpdateResourceSettingsAsync(
                    new GraphResourceControlUpdate
                    {
                        ResourceMode = SelectedResourceMode,
                        ProcessOnlyWhileIdle = ProcessOnlyWhileIdle,
                        ProcessOnlyWhileConnectedToPower = ProcessOnlyWhileConnectedToPower,
                        PauseBelowBatteryPercentage = PauseBelowBatteryPercentage,
                        ProcessingWindowStartHour = ProcessingWindowStartHour,
                        ProcessingWindowEndHour = ProcessingWindowEndHour,
                        ExpectedRevision = _resourceSettingsRevision,
                    },
                    token);
                return new GraphOperationResult(
                    true,
                    "Knowledge Graph background resource settings were saved.",
                    1);
            },
            KnowledgeGraphFocusTarget.PageHeading,
            refreshAfterSuccess: false);
        if (result?.Succeeded == true && saved is not null)
        {
            await ApplyOnUiThreadAsync(() => ApplyResourceSettings(saved));
        }
    }

    private void RequestMaintenance()
    {
        MaintenanceConfirmationText =
            "Run bounded graph-only cleanup and eligible SQLite compaction? Expired operational history and verified orphaned derived records may be removed; decisions, current evidence, the deep index, and original files remain unchanged.";
        IsMaintenanceConfirmationPending = true;
        SelectedSectionIndex = 3;
        RequestFocus(KnowledgeGraphFocusTarget.RepairHeading);
    }

    private async Task ConfirmMaintenanceAsync()
    {
        if (_coordinator is null || !IsMaintenanceConfirmationPending)
        {
            return;
        }

        IsMaintenanceConfirmationPending = false;
        var configuredMaximum = _coordinatorStatus?.MaximumStorageSizeBytes ?? 0;
        var maximum = configuredMaximum is >= GraphLimits.MinimumStorageQuotaBytes and <= GraphLimits.MaximumStorageQuotaBytes
            ? configuredMaximum
            : 512L * 1024L * 1024L;
        GraphMaintenanceResult? maintenance = null;
        var result = await RunMutationAsync(
            async token =>
            {
                maintenance = await _coordinator.MaintainAsync(
                    new GraphMaintenanceRequest(maximum, GraphMaintenanceTrigger.UserRequested, AllowCompaction: true),
                    token);
                return new GraphOperationResult(
                    true,
                    maintenance.Message,
                    maintenance.RecordsRemoved);
            },
            KnowledgeGraphFocusTarget.RepairHeading,
            refreshAfterSuccess: true);
        if (result?.Succeeded == true && maintenance is not null)
        {
            RepairText =
                $"Storage maintenance removed {maintenance.RecordsRemoved:N0} eligible record(s); " +
                $"size changed from {FormatBytes(maintenance.BytesBefore)} to {FormatBytes(maintenance.BytesAfter)}. " +
                (maintenance.QuotaBlocked ? "The configured quota still prevents additional graph work." : "The quota is not blocking graph work.");
        }
    }

    private void CancelMaintenance()
    {
        IsMaintenanceConfirmationPending = false;
        MaintenanceConfirmationText = string.Empty;
        Status = StatusPresentation.Information("Graph storage maintenance was cancelled; no graph or source data changed.");
        RequestFocus(KnowledgeGraphFocusTarget.RepairHeading);
    }

    private Task PauseAsync() => _coordinator is null
        ? Task.CompletedTask
        : RunMutationAsync(_coordinator.PauseAsync, KnowledgeGraphFocusTarget.PageHeading, refreshAfterSuccess: true);

    private Task ResumeAsync() => _coordinator is null
        ? Task.CompletedTask
        : RunMutationAsync(
            token => RunThenReconcileAsync(_coordinator.ResumeAsync, token),
            KnowledgeGraphFocusTarget.PageHeading,
            refreshAfterSuccess: true);

    private Task ReconcileAsync() => _coordinator is null
        ? Task.CompletedTask
        : RunMutationAsync(
            _coordinator.ReconcileAsync,
            KnowledgeGraphFocusTarget.PageHeading,
            refreshAfterSuccess: true);

    private void RequestCancelProjection()
    {
        IsCancelProjectionConfirmationPending = true;
        Status = StatusPresentation.Warning("Confirm durable cancellation. The current valid graph remains readable.");
    }

    private async Task ConfirmCancelProjectionAsync()
    {
        if (_coordinator is null)
        {
            return;
        }

        IsCancelProjectionConfirmationPending = false;
        await RunMutationAsync(
            token => _coordinator.CancelAsync("user-requested-from-knowledge-graph", token),
            KnowledgeGraphFocusTarget.PageHeading,
            refreshAfterSuccess: true);
    }

    private void CancelCancelProjection()
    {
        IsCancelProjectionConfirmationPending = false;
        Status = StatusPresentation.Information("Graph projection cancellation was not requested.");
        RequestFocus(KnowledgeGraphFocusTarget.InitiatingControl);
    }

    private Task RetryAsync() => _coordinator is null
        ? Task.CompletedTask
        : RunMutationAsync(
            token => RunThenReconcileAsync(
                innerToken => _coordinator.RetryAsync(null, innerToken),
                token),
            KnowledgeGraphFocusTarget.PageHeading,
            refreshAfterSuccess: true);

    private async Task<GraphOperationResult> RunThenReconcileAsync(
        Func<CancellationToken, Task<GraphOperationResult>> transition,
        CancellationToken cancellationToken)
    {
        if (_coordinator is null)
        {
            return new GraphOperationResult(false, "Knowledge Graph services are unavailable.", 0);
        }

        var transitionResult = await transition(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!transitionResult.Succeeded)
        {
            return transitionResult;
        }

        var reconciliationResult = await _coordinator.ReconcileAsync(cancellationToken);
        return reconciliationResult with
        {
            Message = reconciliationResult.Succeeded
                ? $"{transitionResult.Message} {reconciliationResult.Message}"
                : reconciliationResult.Message,
            AffectedCount = transitionResult.AffectedCount + reconciliationResult.AffectedCount,
        };
    }

    private void RequestDisable()
    {
        IsDisableConfirmationPending = true;
        Status = StatusPresentation.Information("Confirm disabling graph work and queries while retaining graph-owned data.");
    }

    private async Task ConfirmDisableAsync()
    {
        if (_coordinator is null)
        {
            return;
        }

        IsDisableConfirmationPending = false;
        await RunMutationAsync(_coordinator.DisableAsync, KnowledgeGraphFocusTarget.EnableControl, refreshAfterSuccess: true);
    }

    private void CancelDisable()
    {
        IsDisableConfirmationPending = false;
        Status = StatusPresentation.Information("Knowledge Graph remains enabled.");
        RequestFocus(KnowledgeGraphFocusTarget.PageHeading);
    }

    private async Task ApplyFiltersAsync()
    {
        ResetNodePaging();
        await LoadNodePageAsync(null, 0, null, requestFocus: true);
    }

    private Task PreviousPageAsync()
    {
        var targetIndex = Math.Max(0, _nodePageIndex - 1);
        return LoadNodePageAsync(_nodePageCursors[targetIndex], targetIndex, null, requestFocus: true);
    }

    private Task NextPageAsync()
    {
        if (_nextNodeCursor is null)
        {
            return Task.CompletedTask;
        }

        return LoadNodePageAsync(_nextNodeCursor, _nodePageIndex + 1, null, requestFocus: true);
    }

    private Task PreviousNeighborPageAsync()
    {
        if (SelectedNode is null)
        {
            return Task.CompletedTask;
        }

        var targetIndex = Math.Max(0, _neighborPageIndex - 1);
        return LoadNeighborPageAsync(
            SelectedNode.Id,
            _neighborPageCursors[targetIndex],
            targetIndex,
            requestFocus: true);
    }

    private Task NextNeighborPageAsync()
    {
        if (SelectedNode is null || _nextNeighborCursor is null)
        {
            return Task.CompletedTask;
        }

        return LoadNeighborPageAsync(
            SelectedNode.Id,
            _nextNeighborCursor,
            _neighborPageIndex + 1,
            requestFocus: true);
    }

    private async Task CreateManualEntityAsync()
    {
        if (_decisionService is null || !TryValidateLabel(ManualEntityLabel, out var label))
        {
            return;
        }

        var entityId = $"manual:{Guid.NewGuid():N}";
        await RunMutationAsync(
            token => _decisionService.CreateManualEntityAsync(entityId, label, token),
            KnowledgeGraphFocusTarget.NodeList,
            refreshAfterSuccess: true);
        ManualEntityLabel = string.Empty;
    }

    private async Task RenameSelectedAsync()
    {
        if (_decisionService is null || SelectedNode is null || !TryValidateLabel(RenameLabel, out var label))
        {
            return;
        }

        await RunMutationAsync(
            token => _decisionService.RenameManualEntityAsync(SelectedNode.Id, label, token),
            KnowledgeGraphFocusTarget.NodeList,
            refreshAfterSuccess: true);
    }

    private async Task AddAliasAsync()
    {
        if (_decisionService is null || SelectedNode is null || !CanAddAlias() ||
            !TryValidateLabel(AliasLabel, out var alias))
        {
            return;
        }

        await RunMutationAsync(
            token => _decisionService.AddAliasAsync(SelectedNode.Id, alias, token),
            KnowledgeGraphFocusTarget.NodeList,
            refreshAfterSuccess: true);
        AliasLabel = string.Empty;
    }

    private Task RemoveAliasAsync()
    {
        if (_decisionService is null || SelectedNode is null || !CanRemoveAlias())
        {
            return Task.CompletedTask;
        }

        RequestDecisionConfirmation(
            PendingDecisionAction.RemoveAlias,
            SelectedNode.Id,
            SelectedAlias,
            $"Remove the alias '{SelectedAlias}' from '{SelectedNode.Title}'? Only OmniSorSe-owned graph decisions change; original files remain unchanged.");
        return Task.CompletedTask;
    }

    private Task MergeSelectedAsync()
    {
        if (_decisionService is null || SelectedNode is null || MergeTarget is null || !CanMergeSelected())
        {
            return Task.CompletedTask;
        }

        RequestDecisionConfirmation(
            PendingDecisionAction.Merge,
            SelectedNode.Id,
            MergeTarget.Id,
            $"Merge '{SelectedNode.Title}' into '{MergeTarget.Title}'? The reviewed graph decision persists across rebuilds; original files remain unchanged.");
        return Task.CompletedTask;
    }

    private Task SplitSelectedAsync()
    {
        if (_decisionService is null || SelectedNode is null || SelectedNeighbor is null || !CanSplitSelected())
        {
            return Task.CompletedTask;
        }

        RequestDecisionConfirmation(
            PendingDecisionAction.Split,
            SelectedNode.Id,
            SelectedNeighbor.NodeId,
            $"Split '{SelectedNeighbor.Title}' from '{SelectedNode.Title}'? Only OmniSorSe-owned graph decisions change; original files remain unchanged.");
        return Task.CompletedTask;
    }

    private async Task LinkSelectedAsync()
    {
        if (_decisionService is null || SelectedNode is null || LinkTarget is null || !CanLinkSelected())
        {
            return;
        }

        var reason = string.IsNullOrWhiteSpace(ManualRelationshipLabel)
            ? "Manual link created by the user"
            : ManualRelationshipLabel.Trim();
        await RunMutationAsync(
            token => _decisionService.LinkAsync(SelectedNode.Id, LinkTarget.Id, reason, token),
            KnowledgeGraphFocusTarget.NeighborList,
            refreshAfterSuccess: true);
    }

    private Task UnlinkSelectedAsync()
    {
        if (_decisionService is null || SelectedNeighbor is null)
        {
            return Task.CompletedTask;
        }

        RequestDecisionConfirmation(
            PendingDecisionAction.Unlink,
            SelectedNeighbor.EdgeId,
            SelectedNeighbor.NodeId,
            $"Unlink '{SelectedNeighbor.Title}'? " +
            (SelectedNeighbor.IsLegacyOwned
                ? "The existing v1.9 relationship authority will be updated first; the Knowledge Graph will then reconcile that change."
                : SelectedNeighbor.IsManual
                ? "The manual graph link will be removed."
                : "The evidence-backed graph link will be rejected so automatic rebuilds do not recreate it.") +
            " Original files remain unchanged.",
            preventRegeneration: !SelectedNeighbor.IsManual);
        return Task.CompletedTask;
    }

    private Task RejectSelectedSuggestionAsync()
    {
        if (_decisionService is null || SelectedNode is null || !CanRejectSuggestion())
        {
            return Task.CompletedTask;
        }

        RequestDecisionConfirmation(
            PendingDecisionAction.RejectSuggestion,
            SelectedNode.Id,
            null,
            $"Ignore the suggestion '{SelectedNode.Title}'? The rejection persists across graph rebuilds; original files remain unchanged.");
        return Task.CompletedTask;
    }

    private void RequestDecisionConfirmation(
        PendingDecisionAction action,
        string subjectId,
        string? targetId,
        string explanation,
        bool preventRegeneration = false)
    {
        _pendingDecisionAction = action;
        _pendingDecisionSubjectId = subjectId;
        _pendingDecisionTargetId = targetId;
        _pendingDecisionPreventRegeneration = preventRegeneration;
        DecisionConfirmationText = explanation;
        IsDecisionConfirmationPending = true;
        RequestFocus(KnowledgeGraphFocusTarget.DecisionConfirmation, subjectId);
    }

    private async Task ConfirmDecisionActionAsync()
    {
        if (_decisionService is null || _pendingDecisionAction == PendingDecisionAction.None ||
            string.IsNullOrWhiteSpace(_pendingDecisionSubjectId))
        {
            return;
        }

        var action = _pendingDecisionAction;
        var subjectId = _pendingDecisionSubjectId;
        var targetId = _pendingDecisionTargetId;
        var preventRegeneration = _pendingDecisionPreventRegeneration;
        ClearDecisionConfirmation();

        Func<CancellationToken, Task<GraphOperationResult>> operation = action switch
        {
            PendingDecisionAction.Merge when targetId is not null =>
                token => _decisionService.MergeAsync(targetId, subjectId, token),
            PendingDecisionAction.Split when targetId is not null =>
                token => _decisionService.SplitAsync(subjectId, targetId, token),
            PendingDecisionAction.Unlink =>
                token => _decisionService.UnlinkAsync(subjectId, preventRegeneration, token),
            PendingDecisionAction.RejectSuggestion =>
                token => _decisionService.RejectSuggestionAsync(subjectId, token),
            PendingDecisionAction.RemoveAlias when targetId is not null =>
                token => _decisionService.RemoveAliasAsync(subjectId, targetId, token),
            _ => throw new InvalidOperationException("The reviewed graph correction is incomplete."),
        };
        var focus = action == PendingDecisionAction.Unlink
            ? KnowledgeGraphFocusTarget.NeighborList
            : KnowledgeGraphFocusTarget.NodeList;
        await RunMutationAsync(operation, focus, refreshAfterSuccess: true);
    }

    private void CancelDecisionAction()
    {
        ClearDecisionConfirmation();
        Status = StatusPresentation.Information("The graph correction was cancelled. No graph or source data changed.");
        RequestFocus(SelectedNeighbor is null
            ? KnowledgeGraphFocusTarget.NodeList
            : KnowledgeGraphFocusTarget.NeighborList);
    }

    private void ClearDecisionConfirmation()
    {
        _pendingDecisionAction = PendingDecisionAction.None;
        _pendingDecisionSubjectId = null;
        _pendingDecisionTargetId = null;
        _pendingDecisionPreventRegeneration = false;
        DecisionConfirmationText = string.Empty;
        IsDecisionConfirmationPending = false;
    }

    private async Task InspectSelectedAsync()
    {
        if (_privacyService is null || SelectedNode is null)
        {
            return;
        }

        var selectedId = SelectedNode.Id;
        var scope = ScopeFor(SelectedNode);
        var cancellation = ReplaceCancellation(ref _operationCancellation);
        BeginBusy();
        try
        {
            var inspection = await _privacyService.InspectAsync(scope, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!string.Equals(SelectedNode?.Id, selectedId, StringComparison.Ordinal))
            {
                return;
            }

            PrivacyInspectionText =
                $"Stored for this scope: {inspection.NodeCount:N0} node(s), {inspection.EdgeCount:N0} edge(s), " +
                $"{inspection.EvidenceCount:N0} evidence reference(s), {inspection.AliasCount:N0} alias(es), " +
                $"and {inspection.DecisionCount:N0} graph-native decision(s). " +
                $"Excluded from future projection: {(inspection.IsExcluded ? "yes" : "no")}. {inspection.Message}";
            Status = StatusPresentation.Success("Graph-owned data was inspected. Original files were not opened or changed.");
            SelectedSectionIndex = 2;
            RequestFocus(KnowledgeGraphFocusTarget.PrivacyHeading, selectedId);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = StatusPresentation.Information("Knowledge Graph privacy inspection was cancelled safely.");
        }
        catch (Exception)
        {
            PrivacyInspectionText = "Graph-owned data could not be inspected safely.";
            Status = StatusPresentation.Warning("Privacy inspection failed safely. Original files were not affected.");
        }
        finally
        {
            ReleaseCancellation(ref _operationCancellation, cancellation);
            EndBusy();
        }
    }

    private void RequestExcludeSelected()
    {
        if (SelectedNode is null)
        {
            return;
        }

        _pendingPrivacyAction = PendingPrivacyAction.Exclude;
        PrivacyConfirmationText = $"Exclude {SelectedNode.Title} from future graph projection and suppress its current graph visibility?";
        IsPrivacyConfirmationPending = true;
        SelectedSectionIndex = 2;
        RequestFocus(KnowledgeGraphFocusTarget.PrivacyHeading, SelectedNode.Id);
    }

    private void RequestForgetSelected()
    {
        if (SelectedNode is null)
        {
            return;
        }

        _pendingPrivacyAction = PendingPrivacyAction.ForgetNode;
        PrivacyConfirmationText = $"Forget graph-derived data for {SelectedNode.Title}? Graph-native choices are retained unless separately cleared.";
        IsPrivacyConfirmationPending = true;
        SelectedSectionIndex = 2;
        RequestFocus(KnowledgeGraphFocusTarget.PrivacyHeading, SelectedNode.Id);
    }

    private void RequestForgetSource()
    {
        if (SelectedNode?.SourceId is not { Length: > 0 })
        {
            return;
        }

        _pendingPrivacyAction = PendingPrivacyAction.ForgetSource;
        PrivacyConfirmationText = "Forget graph-derived data for the selected indexed source? Source ownership and original files remain unchanged.";
        IsPrivacyConfirmationPending = true;
        SelectedSectionIndex = 2;
        RequestFocus(KnowledgeGraphFocusTarget.PrivacyHeading, SelectedNode.Id);
    }

    private async Task ConfirmPrivacyActionAsync()
    {
        if (_privacyService is null || SelectedNode is null || _pendingPrivacyAction == PendingPrivacyAction.None)
        {
            return;
        }

        var selected = SelectedNode;
        var change = _pendingPrivacyAction switch
        {
            PendingPrivacyAction.Exclude => new GraphPrivacyChange(
                ScopeFor(selected),
                GraphPrivacyAction.ExcludeFromProjection,
                true,
                "Original files remain unchanged"),
            PendingPrivacyAction.ForgetSource => new GraphPrivacyChange(
                new GraphPrivacyScope(GraphPrivacyScopeKind.Source, selected.SourceId!),
                GraphPrivacyAction.ForgetDerivedData,
                true,
                "Original files remain unchanged"),
            _ => new GraphPrivacyChange(
                ScopeFor(selected),
                GraphPrivacyAction.ForgetDerivedData,
                true,
                "Original files remain unchanged"),
        };
        _pendingPrivacyAction = PendingPrivacyAction.None;
        IsPrivacyConfirmationPending = false;
        await RunMutationAsync(
            token => _privacyService.ApplyAsync(change, token),
            KnowledgeGraphFocusTarget.NodeList,
            refreshAfterSuccess: true,
            clearPagesAfterSuccess: true);
    }

    private void CancelPrivacyAction()
    {
        _pendingPrivacyAction = PendingPrivacyAction.None;
        IsPrivacyConfirmationPending = false;
        PrivacyConfirmationText = string.Empty;
        Status = StatusPresentation.Information("The privacy action was cancelled. No graph or source data changed.");
        RequestFocus(KnowledgeGraphFocusTarget.PrivacyHeading, SelectedNode?.Id);
    }

    private void RequestClearDerived()
    {
        _pendingClearAction = PendingClearAction.Derived;
        ClearConfirmationText = "Clear the rebuildable derived graph, retain graph-native decisions for future reapplication, and leave Knowledge Graph disabled until you explicitly enable it again?";
        IsClearConfirmationPending = true;
        SelectedSectionIndex = 2;
        RequestFocus(KnowledgeGraphFocusTarget.PrivacyHeading);
    }

    private void RequestClearDecisions()
    {
        _pendingClearAction = PendingClearAction.Decisions;
        ClearConfirmationText = "Irreversibly clear the derived graph, graph-native decisions, and their managed decision backups, then leave Knowledge Graph disabled?";
        IsClearConfirmationPending = true;
        SelectedSectionIndex = 2;
        RequestFocus(KnowledgeGraphFocusTarget.PrivacyHeading);
    }

    private async Task ConfirmClearAsync()
    {
        if (_privacyService is null || _pendingClearAction == PendingClearAction.None)
        {
            return;
        }

        var action = _pendingClearAction == PendingClearAction.Decisions
            ? GraphPrivacyAction.ClearAllDecisions
            : GraphPrivacyAction.ClearAllDerivedData;
        var change = new GraphPrivacyChange(
            new GraphPrivacyScope(GraphPrivacyScopeKind.All, "graph"),
            action,
            true,
            _pendingClearAction == PendingClearAction.Decisions
                ? "CLEAR GRAPH DECISIONS"
                : "Clear derived graph; retain decisions; leave Knowledge Graph disabled; original files remain unchanged");
        _pendingClearAction = PendingClearAction.None;
        IsClearConfirmationPending = false;
        await RunMutationAsync(
            token => _privacyService.ApplyAsync(change, token),
            action == GraphPrivacyAction.ClearAllDecisions
                ? KnowledgeGraphFocusTarget.EnableControl
                : KnowledgeGraphFocusTarget.PageHeading,
            refreshAfterSuccess: true,
            clearPagesAfterSuccess: true);
    }

    private void CancelClear()
    {
        _pendingClearAction = PendingClearAction.None;
        IsClearConfirmationPending = false;
        ClearConfirmationText = string.Empty;
        Status = StatusPresentation.Information("The graph clear action was cancelled. No data changed.");
        RequestFocus(KnowledgeGraphFocusTarget.PrivacyHeading);
    }

    private async Task VerifySelectedAsync()
    {
        if (_repairService is null || SelectedNode is null)
        {
            return;
        }

        var result = await RunMutationAsync(
            token => _repairService.ExecuteAsync(
                new GraphRepairRequest(GraphRepairKind.Verify, SelectedNode.Id, true),
                token),
            KnowledgeGraphFocusTarget.RepairHeading,
            refreshAfterSuccess: false);
        if (result is not null)
        {
            RepairText = result.Message;
            SelectedSectionIndex = 3;
        }
    }

    private void RequestRepairSelected()
    {
        if (SelectedNode is null)
        {
            return;
        }

        _pendingRepairRequest = new GraphRepairRequest(GraphRepairKind.RepairEvidence, SelectedNode.Id, true);
        RepairConfirmationText = $"Repair missing or stale evidence for {SelectedNode.Title} using retained indexed facts?";
        IsRepairConfirmationPending = true;
        SelectedSectionIndex = 3;
        RequestFocus(KnowledgeGraphFocusTarget.RepairHeading, SelectedNode.Id);
    }

    private void RequestRebuildSelected()
    {
        if (SelectedNode is null)
        {
            return;
        }

        _pendingRepairRequest = new GraphRepairRequest(GraphRepairKind.ReprojectComponent, SelectedNode.Id, true);
        RepairConfirmationText = $"Rebuild only the derived component for {SelectedNode.Title} while retaining graph-native decisions?";
        IsRepairConfirmationPending = true;
        SelectedSectionIndex = 3;
        RequestFocus(KnowledgeGraphFocusTarget.RepairHeading, SelectedNode.Id);
    }

    private void RequestFullRebuild()
    {
        _pendingRepairRequest = new GraphRepairRequest(GraphRepairKind.RebuildDerivedGraph, null, true);
        RepairConfirmationText = "Use the last-resort full derived-graph rebuild while preserving graph-native decisions and the existing deep index?";
        IsRepairConfirmationPending = true;
        SelectedSectionIndex = 3;
        RequestFocus(KnowledgeGraphFocusTarget.RepairHeading);
    }

    private async Task ConfirmRepairActionAsync()
    {
        if (_repairService is null || _pendingRepairRequest is null)
        {
            return;
        }

        var request = _pendingRepairRequest;
        _pendingRepairRequest = null;
        IsRepairConfirmationPending = false;
        var result = await RunMutationAsync(
            token => _repairService.ExecuteAsync(request, token),
            KnowledgeGraphFocusTarget.RepairHeading,
            refreshAfterSuccess: true);
        if (result is not null)
        {
            RepairText = result.Message;
        }
    }

    private void CancelRepairAction()
    {
        _pendingRepairRequest = null;
        IsRepairConfirmationPending = false;
        RepairConfirmationText = string.Empty;
        Status = StatusPresentation.Information("The graph repair action was cancelled. No data changed.");
        RequestFocus(KnowledgeGraphFocusTarget.RepairHeading, SelectedNode?.Id);
    }

    private async Task<GraphOperationResult?> RunMutationAsync(
        Func<CancellationToken, Task<GraphOperationResult>> operation,
        KnowledgeGraphFocusTarget focusTarget,
        bool refreshAfterSuccess,
        bool clearPagesAfterSuccess = false)
    {
        if (!await _mutationGate.WaitAsync(0))
        {
            Status = StatusPresentation.Information("Another Knowledge Graph action is already active.");
            return null;
        }

        var cancellation = ReplaceCancellation(ref _operationCancellation);
        BeginBusy();
        try
        {
            Status = StatusPresentation.Progress("Applying the reviewed Knowledge Graph action...");
            var result = await operation(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Status = result.Succeeded
                ? StatusPresentation.Success(result.Message)
                : StatusPresentation.Warning(result.Message);
            Announce(result.Message, force: true);
            if (result.Succeeded && clearPagesAfterSuccess)
            {
                await ApplyOnUiThreadAsync(ClearAllPages);
            }

            if (result.Succeeded && refreshAfterSuccess)
            {
                await RefreshAsync();
                Status = StatusPresentation.Success(result.Message);
            }

            RequestFocus(focusTarget, SelectedNode?.Id);
            return result;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Status = StatusPresentation.Information("The current Knowledge Graph request was cancelled safely. Durable projection state was not falsely changed.");
            Announce("Knowledge Graph request cancelled safely.", force: true);
            return null;
        }
        catch (Exception)
        {
            Status = StatusPresentation.Warning("The Knowledge Graph action failed safely. Original files were not affected.");
            Announce("Knowledge Graph action failed safely; original files were not affected.", force: true);
            return null;
        }
        finally
        {
            ReleaseCancellation(ref _operationCancellation, cancellation);
            EndBusy();
            _mutationGate.Release();
        }
    }

    private void OnCoordinatorStatusChanged(object? sender, GraphCoordinatorStatus status) =>
        ApplyOnUiThread(() =>
        {
            if (!_disposed)
            {
                ApplyCoordinatorStatus(status, announce: true);
            }
        });

    private void AnnounceStatus(GraphCoordinatorStatus status)
    {
        var progressBucket = status.TotalObservationCount > 0
            ? (int)Math.Floor(Math.Clamp(status.ProcessedObservationCount / (double)status.TotalObservationCount, 0, 1) * 10)
            : -1;
        var key = string.Join(
            '|',
            status.IsEnabled,
            status.RunControl,
            status.ActiveJobState,
            status.Freshness,
            status.Integrity,
            status.CurrentStage,
            progressBucket);
        if (string.Equals(key, _lastAnnouncementKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastAnnouncementKey = key;
        Announce(
            $"Knowledge Graph {RunStateText}. {JobStateText}. Freshness {FreshnessText}. Integrity {IntegrityText}. {ProgressText}",
            force: true);
    }

    private void Announce(string message, bool force)
    {
        if (!force && string.Equals(AnnouncementText, message, StringComparison.Ordinal))
        {
            return;
        }

        AnnouncementText = message;
    }

    private bool CanPause() => _coordinator is not null && !IsBusy &&
        _coordinatorStatus?.RunControl == GraphRunControlState.Running;

    private bool CanSaveResourceSettings() => _coordinator is not null && IsEnabled && !IsBusy;

    private bool CanRequestMaintenance() => _coordinator is not null && IsEnabled && !IsBusy &&
        _coordinatorStatus?.RunningCount == 0;

    private bool CanReconcile() => _coordinator is not null && IsEnabled && !IsBusy &&
        _coordinatorStatus?.RunControl is GraphRunControlState.Pending or
            GraphRunControlState.Running or
            GraphRunControlState.Complete;

    private bool CanResume() => _coordinator is not null && !IsBusy &&
        _coordinatorStatus?.RunControl == GraphRunControlState.Paused;

    private bool CanRequestProjectionCancellation() => _coordinator is not null && !IsBusy &&
        _coordinatorStatus?.RunControl is GraphRunControlState.Pending or
            GraphRunControlState.Running or
            GraphRunControlState.PauseRequested or
            GraphRunControlState.Paused;

    private bool CanRetry() => _coordinator is not null && !IsBusy && _coordinatorStatus is { } status &&
        (status.RetryableFailureCount > 0 || status.RunControl == GraphRunControlState.Cancelled);

    private bool CanCreateManualEntity() => _decisionService is not null && !IsBusy && IsValidLabel(ManualEntityLabel);

    private bool CanRenameSelected() => _decisionService is not null && !IsBusy &&
        SelectedNode?.IsManual == true && IsValidLabel(RenameLabel);

    private bool CanAddAlias() => _decisionService is not null && !IsBusy &&
        SelectedNode?.IsManual == true && IsValidLabel(AliasLabel) &&
        _aliases.Count < GraphLimits.MaximumAliasesPerNode;

    private bool CanRemoveAlias() => _decisionService is not null && !IsBusy &&
        SelectedNode?.IsManual == true && !string.IsNullOrWhiteSpace(SelectedAlias);

    private bool CanMergeSelected() => _decisionService is not null && !IsBusy &&
        SelectedNode?.CanMerge == true && MergeTarget?.CanMerge == true &&
        !string.Equals(SelectedNode.Id, MergeTarget.Id, StringComparison.Ordinal);

    private bool CanSplitSelected() => _decisionService is not null && !IsBusy &&
        SelectedNode?.CanSplit == true && SelectedNeighbor is not null;

    private bool CanUnlinkSelected() => _decisionService is not null && !IsBusy &&
        SelectedNeighbor?.CanUnlink == true;

    private bool CanLinkSelected() => _decisionService is not null && !IsBusy &&
        SelectedNode is not null && LinkTarget is not null &&
        !string.Equals(SelectedNode.Id, LinkTarget.Id, StringComparison.Ordinal) &&
        ManualRelationshipLabel.Length <= GraphLimits.MaximumDecisionReasonCharacters;

    private bool CanRejectSuggestion() => _decisionService is not null && !IsBusy &&
        _selectedDetails?.Node.Origin == GraphOrigin.ExperimentalSuggestion;

    private bool CanForgetSource() => _privacyService is not null && !IsBusy &&
        SelectedNode?.SourceId is { Length: > 0 };

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedNode));
        OnPropertyChanged(nameof(HasTimeline));
        InspectSelectedCommand.NotifyCanExecuteChanged();
        RequestExcludeSelectedCommand.NotifyCanExecuteChanged();
        RequestForgetSelectedCommand.NotifyCanExecuteChanged();
        RequestForgetSourceCommand.NotifyCanExecuteChanged();
        VerifySelectedCommand.NotifyCanExecuteChanged();
        RequestRepairSelectedCommand.NotifyCanExecuteChanged();
        RequestRebuildSelectedCommand.NotifyCanExecuteChanged();
        RenameSelectedCommand.NotifyCanExecuteChanged();
        AddAliasCommand.NotifyCanExecuteChanged();
        RemoveAliasCommand.NotifyCanExecuteChanged();
        MergeSelectedCommand.NotifyCanExecuteChanged();
        SplitSelectedCommand.NotifyCanExecuteChanged();
        LinkSelectedCommand.NotifyCanExecuteChanged();
        RejectSelectedSuggestionCommand.NotifyCanExecuteChanged();
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ReconcileCommand.NotifyCanExecuteChanged();
        CancelCurrentCommand.NotifyCanExecuteChanged();
        RequestEnableCommand.NotifyCanExecuteChanged();
        ConfirmEnableCommand.NotifyCanExecuteChanged();
        CancelEnableCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        RequestCancelProjectionCommand.NotifyCanExecuteChanged();
        ConfirmCancelProjectionCommand.NotifyCanExecuteChanged();
        CancelCancelProjectionCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        RequestDisableCommand.NotifyCanExecuteChanged();
        ConfirmDisableCommand.NotifyCanExecuteChanged();
        CancelDisableCommand.NotifyCanExecuteChanged();
        ApplyFiltersCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        PreviousNeighborPageCommand.NotifyCanExecuteChanged();
        NextNeighborPageCommand.NotifyCanExecuteChanged();
        CreateManualEntityCommand.NotifyCanExecuteChanged();
        RenameSelectedCommand.NotifyCanExecuteChanged();
        AddAliasCommand.NotifyCanExecuteChanged();
        RemoveAliasCommand.NotifyCanExecuteChanged();
        MergeSelectedCommand.NotifyCanExecuteChanged();
        SplitSelectedCommand.NotifyCanExecuteChanged();
        LinkSelectedCommand.NotifyCanExecuteChanged();
        UnlinkSelectedCommand.NotifyCanExecuteChanged();
        RejectSelectedSuggestionCommand.NotifyCanExecuteChanged();
        ConfirmDecisionActionCommand.NotifyCanExecuteChanged();
        CancelDecisionActionCommand.NotifyCanExecuteChanged();
        InspectSelectedCommand.NotifyCanExecuteChanged();
        RequestExcludeSelectedCommand.NotifyCanExecuteChanged();
        RequestForgetSelectedCommand.NotifyCanExecuteChanged();
        RequestForgetSourceCommand.NotifyCanExecuteChanged();
        ConfirmPrivacyActionCommand.NotifyCanExecuteChanged();
        CancelPrivacyActionCommand.NotifyCanExecuteChanged();
        RequestClearDerivedCommand.NotifyCanExecuteChanged();
        RequestClearDecisionsCommand.NotifyCanExecuteChanged();
        ConfirmClearCommand.NotifyCanExecuteChanged();
        CancelClearCommand.NotifyCanExecuteChanged();
        VerifySelectedCommand.NotifyCanExecuteChanged();
        RequestRepairSelectedCommand.NotifyCanExecuteChanged();
        RequestRebuildSelectedCommand.NotifyCanExecuteChanged();
        RequestFullRebuildCommand.NotifyCanExecuteChanged();
        ConfirmRepairActionCommand.NotifyCanExecuteChanged();
        CancelRepairActionCommand.NotifyCanExecuteChanged();
        SaveResourceSettingsCommand.NotifyCanExecuteChanged();
        RequestMaintenanceCommand.NotifyCanExecuteChanged();
        ConfirmMaintenanceCommand.NotifyCanExecuteChanged();
        CancelMaintenanceCommand.NotifyCanExecuteChanged();
    }

    private void CancelCurrent()
    {
        _statusCancellation?.Cancel();
        _refreshCancellation?.Cancel();
        _detailCancellation?.Cancel();
        _neighborCancellation?.Cancel();
        _evidenceCancellation?.Cancel();
        _operationCancellation?.Cancel();
        Status = StatusPresentation.Information("Cancelling the current Knowledge Graph request...");
    }

    private void BeginBusy()
    {
        if (Interlocked.Increment(ref _busyCount) == 1)
        {
            ApplyOnUiThread(() => IsBusy = true);
        }
    }

    private void EndBusy()
    {
        var remaining = Interlocked.Decrement(ref _busyCount);
        if (remaining <= 0)
        {
            Interlocked.Exchange(ref _busyCount, 0);
            ApplyOnUiThread(() => IsBusy = false);
        }
    }

    private void ClearNodeInspection()
    {
        _selectedDetails = null;
        _selectedNeighbor = null;
        OnPropertyChanged(nameof(SelectedNeighbor));
        Replace(_neighbors, []);
        Replace(_evidence, []);
        Replace(_timeline, []);
        Replace(_facts, []);
        Replace(_aliases, []);
        SelectedAlias = null;
        NodeDetailText = SelectedNode is null
            ? "Select one bounded graph item to inspect it."
            : "Loading bounded graph details...";
        PrivacyInspectionText = "Select Inspect stored graph data to review retained categories for this item.";
        ResetNeighborPaging();
        OnPropertyChanged(nameof(HasSelectedNeighbor));
        OnPropertyChanged(nameof(HasTimeline));
        OnPropertyChanged(nameof(HasFacts));
        OnPropertyChanged(nameof(NeighborPageText));
    }

    private void ClearAllPages()
    {
        SelectedNode = null;
        Replace(_nodes, []);
        ClearNodeInspection();
        ResetNodePaging();
        OnPropertyChanged(nameof(PageText));
    }

    private void ResetNodePaging()
    {
        _nodePageCursors.Clear();
        _nodePageCursors.Add(null);
        _nodePageIndex = 0;
        _nextNodeCursor = null;
        _nodeTotalCount = null;
        OnPropertyChanged(nameof(PageText));
        NotifyCommands();
    }

    private void ResetNeighborPaging()
    {
        _neighborPageCursors.Clear();
        _neighborPageCursors.Add(null);
        _neighborPageIndex = 0;
        _nextNeighborCursor = null;
        _neighborTotalCount = null;
        OnPropertyChanged(nameof(NeighborPageText));
        NotifyCommands();
    }

    private bool TryNormalizeFilter(out string? normalized)
    {
        var value = FilterText.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        if (value.Length > GraphLimits.MaximumLabelCharacters)
        {
            normalized = null;
            Status = StatusPresentation.Warning(
                $"Graph filters may contain at most {GraphLimits.MaximumLabelCharacters:N0} characters.");
            return false;
        }

        normalized = string.IsNullOrEmpty(value)
            ? null
            : value.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return true;
    }

    private bool TryValidateLabel(string input, out string label)
    {
        label = input.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        if (!IsValidLabel(label))
        {
            Status = StatusPresentation.Warning(
                $"Enter a label between 1 and {GraphLimits.MaximumLabelCharacters:N0} characters.");
            return false;
        }

        return true;
    }

    private static bool IsValidLabel(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= GraphLimits.MaximumLabelCharacters;

    private static GraphPrivacyScope ScopeFor(KnowledgeGraphNodeRow row)
    {
        var kind = row.KindText switch
        {
            "File" => GraphPrivacyScopeKind.File,
            "Source" => GraphPrivacyScopeKind.Source,
            "Collection" => GraphPrivacyScopeKind.Collection,
            _ => GraphPrivacyScopeKind.Node,
        };
        return new GraphPrivacyScope(kind, row.PrivacyStableId ?? row.Id);
    }

    private static KnowledgeGraphNodeRow ToNodeRow(GraphNode node)
    {
        var kind = FormatNodeKind(node.Identity.Kind);
        var isManual = node.Origin == GraphOrigin.Manual || node.Identity.Kind == GraphNodeKind.ManualEntity;
        var canCorrectEntity = node.Identity.Kind == GraphNodeKind.ManualEntity ||
            node.Origin == GraphOrigin.ExperimentalSuggestion;
        var sourceId = node.OwningSourceId;
        return new KnowledgeGraphNodeRow(
            node.Identity.NodeId,
            node.DisplayLabel,
            kind,
            $"Origin: {FormatOrigin(node.Origin)}; last validated {FormatTime(node.LastValidatedAtUtc)}",
            FormatFreshness(node.Freshness),
            FormatIntegrity(node.Integrity),
            isManual,
            canCorrectEntity,
            canCorrectEntity,
            sourceId,
            node.Identity.CanonicalKey);
    }

    private static KnowledgeGraphNeighborRow ToNeighborRow(GraphNeighbor neighbor)
    {
        var isLegacyOwned = neighbor.Edge.Origin is GraphOrigin.LegacyRelationship or GraphOrigin.LegacyCollection;
        var canUnlink = neighbor.Edge.IsManual || neighbor.Edge.Kind == GraphEdgeKind.RelatedFile ||
            neighbor.Edge.Origin == GraphOrigin.LegacyCollection && neighbor.Edge.Kind == GraphEdgeKind.MemberOf;
        var evidenceSummary = neighbor.Evidence.FirstOrDefault()?.Explanation ??
            (neighbor.Edge.IsManual ? "Manual link created by the user" : "Evidence is unavailable; repair may be required");
        return new KnowledgeGraphNeighborRow(
            neighbor.Edge.Id,
            neighbor.Node.Identity.NodeId,
            neighbor.Node.DisplayLabel,
            FormatEdgeKind(neighbor.Edge.Kind),
            FormatConfidence(neighbor.Edge.Confidence),
            neighbor.Edge.IsManual
                ? "Manual"
                : $"{FormatOrigin(neighbor.Edge.Origin)}; {neighbor.Edge.Algorithm} {neighbor.Edge.AlgorithmVersion}",
            evidenceSummary,
            FormatFreshness(neighbor.Edge.Freshness),
            FormatIntegrity(neighbor.Edge.Integrity),
            neighbor.Edge.IsManual,
            isLegacyOwned,
            canUnlink);
    }

    private static KnowledgeGraphEvidenceRow ToEvidenceRow(GraphEvidenceReference evidence) => new(
        evidence.Id,
        FormatEvidenceKind(evidence.Kind),
        evidence.Explanation,
        evidence.ExplanationTemplateCode,
        "Validated with the current retained projection evidence");

    private static KnowledgeGraphFactRow ToFactRow(GraphFact fact)
    {
        var kind = fact.Kind == GraphFactKind.FileSize
            ? "File size"
            : fact.Kind == GraphFactKind.CreatedTimestamp
                ? "Created timestamp"
                : fact.Kind == GraphFactKind.ModifiedTimestamp
                    ? "Modified timestamp"
                    : "Bounded fact";
        var value = fact.Kind == GraphFactKind.FileSize &&
                    long.TryParse(fact.CanonicalValue, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) && bytes >= 0
            ? FormatBytes(bytes)
            : DateTimeOffset.TryParseExact(
                fact.CanonicalValue,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp)
                ? FormatTime(timestamp)
                : "Validated value unavailable";
        return new KnowledgeGraphFactRow(
            fact.Id,
            kind,
            value,
            $"{fact.EvidenceIds.Count:N0} retained evidence reference(s)",
            fact.AlgorithmVersion);
    }

    private static KnowledgeGraphTimelineRow ToTimelineRow(GraphTimelineEntry entry) => new(
        entry.FactId,
        FormatTime(entry.OccurredAtUtc),
        entry.Kind == GraphFactKind.CreatedTimestamp ? "Indexed creation timestamp" : "Indexed modification timestamp",
        $"Supported by {entry.EvidenceIds.Count:N0} retained evidence reference(s); algorithm version {entry.AlgorithmVersion}.");

    private static GraphNodeKind? ResolveNodeKind(string value) => value switch
    {
        "Files" => GraphNodeKind.File,
        "Sources" => GraphNodeKind.Source,
        "Folders" => GraphNodeKind.Folder,
        "Collections" => GraphNodeKind.Collection,
        "Document sets" => GraphNodeKind.DocumentSet,
        "Manual entities" => GraphNodeKind.ManualEntity,
        _ => null,
    };

    private static string FormatNodeKind(GraphNodeKind kind) => kind == GraphNodeKind.File ? "File"
        : kind == GraphNodeKind.Source ? "Source"
        : kind == GraphNodeKind.Folder ? "Folder"
        : kind == GraphNodeKind.Collection ? "Collection"
        : kind == GraphNodeKind.DocumentSet ? "Document set"
        : kind == GraphNodeKind.ManualEntity ? "Manual entity"
        : "Unsupported item type";

    private static string FormatEdgeKind(GraphEdgeKind kind) => kind == GraphEdgeKind.RelatedFile ? "Related file"
        : kind == GraphEdgeKind.OwnedBySource ? "Owned by source"
        : kind == GraphEdgeKind.LocatedInFolder ? "Located in folder"
        : kind == GraphEdgeKind.MemberOf ? "Member of"
        : kind == GraphEdgeKind.SameDocumentSet ? "Same document set"
        : kind == GraphEdgeKind.Manual ? "Manual link"
        : "Unsupported relationship";

    private static string FormatEvidenceKind(GraphEvidenceKind kind) => kind == GraphEvidenceKind.StableIdentity ? "Stable identity"
        : kind == GraphEvidenceKind.SourceOwnership ? "Source ownership"
        : kind == GraphEvidenceKind.RelativeFolder ? "Relative folder"
        : kind == GraphEvidenceKind.ExactContentHash ? "Exact content hash"
        : kind == GraphEvidenceKind.LegacyRelationship ? "Existing relationship"
        : kind == GraphEvidenceKind.CollectionMembership ? "Collection membership"
        : kind == GraphEvidenceKind.Manual ? "Manual"
        : "Unsupported evidence";

    private static string FormatRunState(GraphRunControlState state) => state switch
    {
        GraphRunControlState.Pending => "Pending",
        GraphRunControlState.Running => "Running",
        GraphRunControlState.PauseRequested => "Pause requested; active claims are draining",
        GraphRunControlState.Paused => "Paused",
        GraphRunControlState.CancelRequested => "Cancellation requested; acknowledgement is pending",
        GraphRunControlState.Cancelled => "Cancelled",
        GraphRunControlState.Complete => "Complete",
        _ => "Unknown run state",
    };

    private static string FormatJobState(GraphJobExecutionState? state, GraphCoordinatorStatus status) => state switch
    {
        GraphJobExecutionState.Pending => "Pending",
        GraphJobExecutionState.Running => "Running",
        GraphJobExecutionState.Complete => "Complete",
        GraphJobExecutionState.Cancelled => "Cancelled",
        GraphJobExecutionState.RetryableFailure => "Retryable failure",
        GraphJobExecutionState.PermanentFailure => "Permanent failure; review or repair is required",
        GraphJobExecutionState.WaitingForDependency => "Waiting for an optional dependency",
        GraphJobExecutionState.WaitingForResources => "Waiting for resources or policy",
        null when status.WaitingCount > 0 => $"Waiting: {status.WaitingCount:N0} job(s)",
        null when status.PendingCount > 0 => $"Pending: {status.PendingCount:N0} job(s)",
        null => "No active job",
        _ => "Unknown job state",
    };

    private static string FormatFreshness(GraphFreshnessState state) => state switch
    {
        GraphFreshnessState.Current => "Current",
        GraphFreshnessState.Stale => "Stale; prior valid data may remain visible while replacement is pending",
        _ => "Unknown freshness",
    };

    private static string FormatIntegrity(GraphIntegrityState state) => state switch
    {
        GraphIntegrityState.Valid => "Valid",
        GraphIntegrityState.RepairRequired => "Repair required",
        _ => "Unknown integrity",
    };

    private static string FormatConfidence(GraphConfidenceLevel confidence) => confidence switch
    {
        GraphConfidenceLevel.Low => "Low",
        GraphConfidenceLevel.Medium => "Medium",
        GraphConfidenceLevel.High => "High",
        GraphConfidenceLevel.Confirmed => "Confirmed by user",
        _ => "Unknown",
    };

    private static string FormatOrigin(GraphOrigin origin) => origin switch
    {
        GraphOrigin.Mechanical => "Deterministic projection",
        GraphOrigin.LegacyRelationship => "Existing relationship",
        GraphOrigin.LegacyCollection => "Existing Smart Collection",
        GraphOrigin.Manual => "Manual",
        GraphOrigin.ExperimentalSuggestion => "Confirmation-required suggestion",
        _ => "Unknown origin",
    };

    private static string FormatStage(string? stage, string? workLabel)
    {
        var stageText = string.IsNullOrWhiteSpace(stage) ? "none" : stage;
        return string.IsNullOrWhiteSpace(workLabel)
            ? $"Stage: {stageText}"
            : $"Stage: {stageText}; current work: {workLabel}";
    }

    private static string FormatCoverage(GraphProjectionCoverage coverage)
    {
        if (!coverage.IsEnabled)
        {
            return "Graph projection coverage is disabled. Deep Search-index coverage is independent.";
        }

        if (!coverage.IsAvailable)
        {
            return $"Graph projection coverage is unavailable. Existing Search and indexing remain independent. {coverage.Message}";
        }

        var completeness = coverage.IsComplete ? "complete" : "still being built";
        var stale = coverage.IsStale ? " Some projected data is stale." : string.Empty;
        return
            $"Graph projection coverage is {completeness}: {coverage.ProjectedObservationCount:N0} of {coverage.TotalObservationCount:N0} observations, " +
            $"{coverage.FailedCount:N0} failed and {coverage.WaitingCount:N0} waiting.{stale} {coverage.Message}";
    }

    private static string FormatResourceSettings(GraphControlSettings settings)
    {
        var restrictions = new List<string>();
        if (settings.ProcessOnlyWhileIdle)
        {
            restrictions.Add("idle host");
        }

        if (settings.ProcessOnlyWhileConnectedToPower)
        {
            restrictions.Add("external power");
        }

        if (settings.PauseBelowBatteryPercentage is { } battery)
        {
            restrictions.Add($"battery at least {battery}%");
        }

        if (settings.ProcessingWindowStartHour is { } start && settings.ProcessingWindowEndHour is { } end)
        {
            restrictions.Add($"local time window {start:00}:00–{end:00}:00");
        }

        var restrictionText = restrictions.Count == 0
            ? "No additional scheduling restrictions."
            : $"Requires {string.Join(", ", restrictions)}. Unsupported platform signals pause processing with an actionable message.";
        return $"{settings.ResourceMode} mode uses at most {settings.MaximumConcurrency:N0} bounded worker(s). {restrictionText}";
    }

    private static string FormatStorage(GraphCoordinatorStatus status)
    {
        var storage = status.StorageBreakdown;
        if (storage.IsInventoryVerified)
        {
            if (storage.TotalBytes == 0 && status.StorageSizeBytes > 0)
            {
                return status.MaximumStorageSizeBytes > 0
                    ? $"Graph storage: {FormatBytes(status.StorageSizeBytes)} of {FormatBytes(status.MaximumStorageSizeBytes)}; provider breakdown is pending."
                    : $"Graph storage: {FormatBytes(status.StorageSizeBytes)}; provider breakdown is pending.";
            }

            var maximum = storage.MaximumBytes > 0
                ? $" of {FormatBytes(storage.MaximumBytes)}"
                : string.Empty;
            return
                $"Graph-owned storage: {FormatBytes(storage.TotalBytes)}{maximum}; " +
                $"derived data {FormatBytes(storage.DerivedStoreBytes)}, decision ledger {FormatBytes(storage.DecisionLedgerBytes)}, " +
                $"verified backups {FormatBytes(storage.VerifiedBackupBytes)}, recovery reserve {FormatBytes(storage.RequiredReserveBytes)}.";
        }

        return "Graph storage inventory could not be verified; retained graph data is hidden until provider inventory succeeds.";
    }

    private static string FormatMaintenance(GraphMaintenanceStatus maintenance)
    {
        if (maintenance.IsRunning)
        {
            return "Bounded graph storage maintenance is running; Search falls back safely if the graph is temporarily unavailable.";
        }

        if (maintenance.LastCompletedAtUtc is null)
        {
            return string.IsNullOrWhiteSpace(maintenance.Message)
                ? "No graph storage maintenance result is recorded yet."
                : maintenance.Message;
        }

        return
            $"Last maintenance completed {FormatTime(maintenance.LastCompletedAtUtc.Value)} and removed " +
            $"{maintenance.LastRecordsRemoved:N0} eligible record(s). " +
            (maintenance.QuotaBlocked ? "The configured storage quota remains blocking." : "The storage quota is not blocking.");
    }

    private static string FormatDiagnostics(GraphDiagnosticsSnapshot diagnostics)
    {
        var run = string.IsNullOrWhiteSpace(diagnostics.RunId) ? "none" : diagnostics.RunId;
        var failure = string.IsNullOrWhiteSpace(diagnostics.LastFailureCategory)
            ? "none"
            : diagnostics.LastFailureCategory;
        return
            $"Run {run}; projection revision {diagnostics.ProjectionRevision:N0}; " +
            $"{diagnostics.NodeCount:N0} nodes, {diagnostics.EdgeCount:N0} edges, {diagnostics.EvidenceCount:N0} evidence records, " +
            $"{diagnostics.DecisionCount:N0} graph decisions; queue {diagnostics.QueueLength:N0}; " +
            $"repair required {diagnostics.RepairRequiredCount:N0}; recovered claims {diagnostics.RecoveredClaimCount:N0}; " +
            $"last failure category {failure}. Diagnostic text excludes source paths and document content.";
    }

    private static string FormatWaitReason(GraphCoordinatorStatus status)
    {
        if (status.ActiveJobState == GraphJobExecutionState.WaitingForDependency)
        {
            return "Waiting for an optional dependency. Stable deterministic graph data and ordinary Search remain available.";
        }

        if (status.ActiveJobState == GraphJobExecutionState.WaitingForResources)
        {
            return "Waiting for storage, database availability, power, time-window, or another configured resource policy. Review diagnostics for the exact safe category.";
        }

        if (status.PermanentFailureCount > 0)
        {
            return $"{status.PermanentFailureCount:N0} permanent failure(s) require inspection or selective repair.";
        }

        if (status.RetryableFailureCount > 0)
        {
            return $"{status.RetryableFailureCount:N0} retryable failure(s) are retained.";
        }

        return string.Empty;
    }

    private static StatusPresentation PresentStatus(GraphCoordinatorStatus status)
    {
        if (!status.IsEnabled)
        {
            return StatusPresentation.Information("Knowledge Graph is disabled. Existing Search and indexing remain available.");
        }

        if (status.Integrity == GraphIntegrityState.RepairRequired)
        {
            return StatusPresentation.Warning("Knowledge Graph requires selective repair. Original files and the deep index are unaffected.");
        }

        if (!status.Coverage.IsAvailable)
        {
            return StatusPresentation.Warning("Knowledge Graph is temporarily unavailable. Existing Search and indexing remain available.");
        }

        return status.RunControl switch
        {
            GraphRunControlState.Running or GraphRunControlState.Pending or GraphRunControlState.PauseRequested or GraphRunControlState.CancelRequested =>
                StatusPresentation.Progress(string.IsNullOrWhiteSpace(status.Message) ? "Knowledge Graph background work is active." : status.Message),
            GraphRunControlState.Complete => StatusPresentation.Success(string.IsNullOrWhiteSpace(status.Message) ? "Knowledge Graph projection is complete." : status.Message),
            _ => StatusPresentation.Information(string.IsNullOrWhiteSpace(status.Message) ? $"Knowledge Graph is {FormatRunState(status.RunControl).ToLowerInvariant()}." : status.Message),
        };
    }

    private static string FormatPageText(int pageIndex, int count, long? total, string noun = "items")
    {
        if (count == 0)
        {
            return $"Page {pageIndex + 1}; no {noun}";
        }

        var first = (long)pageIndex * PageSize + 1;
        var last = first + count - 1;
        return total is { } known
            ? $"Showing {first:N0}–{last:N0} of {known:N0} {noun}"
            : $"Showing {first:N0}–{last:N0} {noun}; total not reported";
    }

    private static string FormatBytes(long bytes)
    {
        var value = Math.Max(0, bytes);
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var amount = (double)value;
        var unit = 0;
        while (amount >= 1024 && unit < units.Length - 1)
        {
            amount /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.CurrentCulture, $"{amount:N1} {units[unit]}");
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var safe = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        if (safe.TotalHours >= 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{safe.TotalHours:N1} hours (estimate)");
        }

        if (safe.TotalMinutes >= 1)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{safe.TotalMinutes:N0} minutes (estimate)");
        }

        return string.Create(CultureInfo.CurrentCulture, $"{safe.TotalSeconds:N0} seconds (estimate)");
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static void RecordCursor(List<GraphPageCursor?> cursors, int index, GraphPageCursor? cursor)
    {
        while (cursors.Count <= index)
        {
            cursors.Add(null);
        }

        cursors[index] = cursor;
        if (cursors.Count > index + 1)
        {
            cursors.RemoveRange(index + 1, cursors.Count - index - 1);
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private void RequestFocus(KnowledgeGraphFocusTarget target, string? itemId = null)
    {
        var request = new KnowledgeGraphFocusRequest(
            Interlocked.Increment(ref _focusSequence),
            target,
            itemId);
        LastFocusRequest = request;
        FocusRequested?.Invoke(request);
    }

    private static bool IsCurrent(long version, long currentVersion, CancellationTokenSource cancellation) =>
        version == currentVersion && !cancellation.IsCancellationRequested;

    private static CancellationTokenSource ReplaceCancellation(
        ref CancellationTokenSource? field,
        CancellationToken outerCancellation = default)
    {
        var next = outerCancellation.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(outerCancellation)
            : new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref field, next);
        previous?.Cancel();
        previous?.Dispose();
        return next;
    }

    private static void ReleaseCancellation(
        ref CancellationTokenSource? field,
        CancellationTokenSource cancellation)
    {
        Interlocked.CompareExchange(ref field, null, cancellation);
        cancellation.Dispose();
    }

    private static void CancelAndDispose(ref CancellationTokenSource? field)
    {
        var cancellation = Interlocked.Exchange(ref field, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private static void ApplyOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current is null)
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private static Task ApplyOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess() || Avalonia.Application.Current is null)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                action();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_coordinator is not null)
        {
            _coordinator.StatusChanged -= OnCoordinatorStatusChanged;
        }

        CancelAndDispose(ref _statusCancellation);
        CancelAndDispose(ref _refreshCancellation);
        CancelAndDispose(ref _detailCancellation);
        CancelAndDispose(ref _neighborCancellation);
        CancelAndDispose(ref _evidenceCancellation);
        CancelAndDispose(ref _operationCancellation);
        CancelAndDispose(ref _companionCancellation);
        // Do not dispose the gate here: an already-cancelled command continuation may still
        // execute its finally block and release it after this synchronous teardown returns.
    }

    private enum PendingPrivacyAction
    {
        None,
        Exclude,
        ForgetNode,
        ForgetSource,
    }

    private enum PendingClearAction
    {
        None,
        Derived,
        Decisions,
    }

    private enum PendingDecisionAction
    {
        None,
        Merge,
        Split,
        Unlink,
        RejectSuggestion,
        RemoveAlias,
    }
}
