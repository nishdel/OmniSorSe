using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Application.AI;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Core.Diagnostics;
using LoggingDiagnosticEvent = OpenSorSe.Core.Logging.DiagnosticEvent;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Defines the severity groups offered by Diagnostics.</summary>
public enum DiagnosticSeverityFilter
{
    /// <summary>Shows every retained event.</summary>
    All,
    /// <summary>Shows trace, debug, and informational events.</summary>
    Information,
    /// <summary>Shows warnings.</summary>
    Warning,
    /// <summary>Shows errors and critical events.</summary>
    Error,
}

/// <summary>Projects one retained event into bounded, copy-safe user-facing details.</summary>
public sealed record DiagnosticEventRow(LoggingDiagnosticEvent Event)
{
    /// <summary>Gets the event timestamp in a stable readable form.</summary>
    public string TimestampText => Event.TimestampUtc.ToString("u");

    /// <summary>Gets a textual severity label.</summary>
    public string SeverityLabel => Event.Severity.ToString();

    /// <summary>Gets the category/source label.</summary>
    public string Category => Event.Category;

    /// <summary>Gets the bounded event summary.</summary>
    public string Summary => Event.Summary;

    /// <summary>Gets whether an exception summary is available without a stack trace.</summary>
    public bool HasExceptionSummary => !string.IsNullOrWhiteSpace(Event.ExceptionSummary);

    /// <summary>Formats selected details without raw stack traces or hidden logger state.</summary>
    public string FormatCopyText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Timestamp (UTC): {Event.TimestampUtc:O}");
        builder.AppendLine($"Severity: {SeverityLabel}");
        builder.AppendLine($"Category: {Event.Category}");
        builder.AppendLine($"Message: {Event.Summary}");
        if (Event.EventId != 0 || !string.IsNullOrWhiteSpace(Event.EventName))
        {
            builder.AppendLine($"Event: {Event.EventName ?? "Unnamed"} ({Event.EventId})");
        }

        if (!string.IsNullOrWhiteSpace(Event.ExceptionType))
        {
            builder.AppendLine($"Exception type: {Event.ExceptionType}");
        }

        if (!string.IsNullOrWhiteSpace(Event.ExceptionSummary))
        {
            builder.AppendLine($"Safe exception summary: {Event.ExceptionSummary}");
        }

        return builder.ToString().TrimEnd();
    }
}

/// <summary>Projects one opt-in, session-only AI request diagnostic.</summary>
public sealed record AiRequestDiagnosticRow(AiRequestDiagnostic Record)
{
    /// <summary>Gets the request timestamp.</summary>
    public string TimestampText => Record.RequestedAtUtc.ToString("u");

    /// <summary>Gets the capability label.</summary>
    public string CapabilityText => Record.Capability == AiSuggestionKind.FileRename ? "File rename" : "Folder structure";

    /// <summary>Gets a concise outcome with elapsed time.</summary>
    public string OutcomeText => $"{Record.Outcome} ({Record.Elapsed.TotalSeconds:0.0} s)";

    /// <summary>Gets whether a captured prompt is available.</summary>
    public bool HasPrompt => Record.Prompt.Length > 0;

    /// <summary>Gets whether a captured response is available.</summary>
    public bool HasResponse => Record.Response.Length > 0;

    /// <summary>Gets the complete bounded summary for display.</summary>
    public string SummaryText => FormatSummary();

    /// <summary>Formats bounded request facts without adding unrelated settings.</summary>
    public string FormatSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Request: {Record.RequestId}");
        builder.AppendLine($"Timestamp (UTC): {Record.RequestedAtUtc:O}");
        builder.AppendLine($"Capability: {CapabilityText}");
        builder.AppendLine($"Endpoint: {Record.NormalizedEndpoint}");
        builder.AppendLine($"Model: {Record.Model}");
        builder.AppendLine($"Effective timeout: {Record.EffectiveTimeoutSeconds} seconds");
        builder.AppendLine($"Elapsed: {Record.Elapsed.TotalSeconds:0.000} seconds");
        builder.AppendLine($"Outcome: {Record.Outcome}");
        builder.AppendLine($"HTTP status: {Record.HttpStatusCode?.ToString() ?? "Not available"}");
        builder.AppendLine($"Provider failure: {Record.FailureKind}");
        builder.AppendLine($"Prompt size: {Record.PromptCharacterCount} characters / {Record.PromptByteCount} bytes");
        builder.AppendLine($"Response size: {Record.ResponseCharacterCount} characters / {Record.ResponseByteCount} bytes");
        builder.AppendLine($"Validation: {Record.ValidationOutcome}");
        builder.AppendLine($"Input items: {Record.IncludedInputCount} included, {Record.OmittedInputCount} omitted, {Record.TotalInputCount} total");
        if (Record.ValidationIssues.Count > 0)
        {
            builder.AppendLine("Validation issues:");
            foreach (var issue in Record.ValidationIssues)
            {
                builder.AppendLine($"- {issue}");
            }
        }

        builder.AppendLine("Stages:");
        foreach (var stage in Record.Stages)
        {
            builder.AppendLine($"- {stage.TimestampUtc:O} {stage.Stage}: {stage.Message}");
        }

        return builder.ToString().TrimEnd();
    }
}

/// <summary>
/// Presents logging health plus a bounded, filterable master-detail view of session events.
/// </summary>
public sealed class LogViewerViewModel : ViewModelBase
{
    private const string AllCategories = "All categories";
    private readonly ILoggingService _loggingService;
    private readonly IClipboardService? _clipboardService;
    private readonly IAiRequestDiagnosticsStore? _aiRequestDiagnosticsStore;
    private readonly IDiagnosticsCollector? _advancedDiagnosticsCollector;
    private readonly IAdvancedDiagnosticsWindowService? _advancedDiagnosticsWindowService;
    private readonly ObservableCollection<DiagnosticEventRow> _events = [];
    private readonly ObservableCollection<DiagnosticEventRow> _visibleEvents = [];
    private readonly ObservableCollection<string> _categories = [AllCategories];
    private readonly ObservableCollection<AiRequestDiagnosticRow> _aiRequests = [];
    private LoggingStatistics _statistics = LoggingStatistics.Empty;
    private DiagnosticSeverityFilter _severityFilter;
    private string _selectedCategory = AllCategories;
    private DiagnosticEventRow? _selectedEvent;
    private AiRequestDiagnosticRow? _selectedAiRequest;
    private string _statusText = "No diagnostic events have been recorded in this application session.";
    private StatusPresentation _status = StatusPresentation.Information("No diagnostic events have been recorded in this application session.");

    /// <summary>Initializes inspectable session diagnostics.</summary>
    public LogViewerViewModel(
        ILoggingService loggingService,
        IClipboardService? clipboardService = null,
        IConfigurationService? configurationService = null,
        IAiRequestDiagnosticsStore? aiRequestDiagnosticsStore = null,
        IDiagnosticsCollector? advancedDiagnosticsCollector = null,
        IAdvancedDiagnosticsWindowService? advancedDiagnosticsWindowService = null)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _clipboardService = clipboardService;
        _ = configurationService;
        _aiRequestDiagnosticsStore = aiRequestDiagnosticsStore;
        _advancedDiagnosticsCollector = advancedDiagnosticsCollector;
        _advancedDiagnosticsWindowService = advancedDiagnosticsWindowService;
        Events = new ReadOnlyObservableCollection<DiagnosticEventRow>(_events);
        VisibleEvents = new ReadOnlyObservableCollection<DiagnosticEventRow>(_visibleEvents);
        Categories = new ReadOnlyObservableCollection<string>(_categories);
        AiRequests = new ReadOnlyObservableCollection<AiRequestDiagnosticRow>(_aiRequests);
        RefreshCommand = new RelayCommand(Refresh);
        CopyDiagnosticDetailsCommand = new AsyncRelayCommand(CopyDiagnosticDetailsAsync, () => SelectedEvent is not null && _clipboardService is not null);
        CopyAiPromptCommand = new AsyncRelayCommand(() => CopyAiTextAsync(SelectedAiRequest?.Record.Prompt, "AI prompt"), () => SelectedAiRequest?.HasPrompt == true && _clipboardService is not null);
        CopyAiResponseCommand = new AsyncRelayCommand(() => CopyAiTextAsync(SelectedAiRequest?.Record.Response, "AI response"), () => SelectedAiRequest?.HasResponse == true && _clipboardService is not null);
        CopyAiRequestDiagnosticsCommand = new AsyncRelayCommand(() => CopyAiTextAsync(SelectedAiRequest?.FormatSummary(), "AI request diagnostics"), () => SelectedAiRequest is not null && _clipboardService is not null);
        ClearAiDiagnosticsCommand = new RelayCommand(ClearAiDiagnostics, () => AiRequests.Count > 0 && _aiRequestDiagnosticsStore is not null);
        OpenAdvancedDiagnosticsCommand = new RelayCommand(
            () => _advancedDiagnosticsWindowService?.Show(),
            () => _advancedDiagnosticsWindowService is not null);
        Refresh();
    }

    /// <summary>Gets all retained events, newest first.</summary>
    public ReadOnlyObservableCollection<DiagnosticEventRow> Events { get; }

    /// <summary>Gets events passing current filters.</summary>
    public ReadOnlyObservableCollection<DiagnosticEventRow> VisibleEvents { get; }

    /// <summary>Gets available category filters.</summary>
    public ReadOnlyObservableCollection<string> Categories { get; }

    /// <summary>Gets newest-first opt-in AI request records.</summary>
    public ReadOnlyObservableCollection<AiRequestDiagnosticRow> AiRequests { get; }

    /// <summary>Gets whether the active settings authorize the raw AI diagnostics section.</summary>
    public bool IsAiDiagnosticsVisible => false;

    /// <summary>Gets whether the visible AI diagnostics collection is empty.</summary>
    public bool HasNoAiRequests => AiRequests.Count == 0;

    /// <summary>Gets the explicit filename privacy notice.</summary>
    public string AiDiagnosticsPrivacyNotice => "AI request diagnostics may contain filenames and relative folder metadata. They are retained only for this session and never include file contents or authorization headers.";

    /// <summary>Gets the retained common advanced-diagnostic session count.</summary>
    public int AdvancedDiagnosticsRetainedCount => _advancedDiagnosticsCollector?.GetRecent().Count ?? 0;

    /// <summary>Gets a truthful instrumentation summary for System Check.</summary>
    public string AdvancedDiagnosticsSummary =>
        "AI, OCR and text extraction, and scanning are instrumented. Duplicate detection, search and indexing, rules and organisation, file operations, and standalone performance sessions are planned.";

    /// <summary>Gets or sets the AI request selected for prompt/response inspection.</summary>
    public AiRequestDiagnosticRow? SelectedAiRequest
    {
        get => _selectedAiRequest;
        set
        {
            if (SetProperty(ref _selectedAiRequest, value))
            {
                OnPropertyChanged(nameof(HasSelectedAiRequest));
                CopyAiPromptCommand.NotifyCanExecuteChanged();
                CopyAiResponseCommand.NotifyCanExecuteChanged();
                CopyAiRequestDiagnosticsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether an AI request is selected.</summary>
    public bool HasSelectedAiRequest => SelectedAiRequest is not null;

    /// <summary>Gets supported severity filters.</summary>
    public IReadOnlyList<DiagnosticSeverityFilter> SeverityFilters { get; } = Enum.GetValues<DiagnosticSeverityFilter>();

    /// <summary>Gets or sets the active severity filter.</summary>
    public DiagnosticSeverityFilter SeverityFilter
    {
        get => _severityFilter;
        set
        {
            if (SetProperty(ref _severityFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>Gets or sets the active category filter.</summary>
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? AllCategories : value;
            if (SetProperty(ref _selectedCategory, normalized))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>Gets or sets the event selected for safe detail inspection.</summary>
    public DiagnosticEventRow? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (SetProperty(ref _selectedEvent, value))
            {
                OnPropertyChanged(nameof(HasSelectedEvent));
                OnPropertyChanged(nameof(SelectedEventDetails));
                CopyDiagnosticDetailsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether an event is selected.</summary>
    public bool HasSelectedEvent => SelectedEvent is not null;

    /// <summary>Gets bounded details for the selected event.</summary>
    public string SelectedEventDetails => SelectedEvent?.FormatCopyText() ?? "Select an event to inspect its safe details.";

    /// <summary>Gets process-lifetime logging counters.</summary>
    public LoggingStatistics Statistics
    {
        get => _statistics;
        private set
        {
            if (SetProperty(ref _statistics, value))
            {
                OnPropertyChanged(nameof(RecordedEventCount));
                OnPropertyChanged(nameof(HasRecordedEvents));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(LoggingStatus));
            }
        }
    }

    /// <summary>Gets the count accepted by configured loggers during this process.</summary>
    public long RecordedEventCount => Statistics.TraceEntries + Statistics.DebugEntries + Statistics.InformationEntries +
        Statistics.WarningEntries + Statistics.ErrorEntries + Statistics.CriticalEntries;

    /// <summary>Gets whether any inspectable event is retained.</summary>
    public bool HasRecordedEvents => Events.Count > 0;

    /// <summary>Gets whether the retained event list is empty.</summary>
    public bool IsEmpty => !HasRecordedEvents;

    /// <summary>Gets whether filters have no matches while retained events exist.</summary>
    public bool HasNoFilterMatches => HasRecordedEvents && VisibleEvents.Count == 0;

    /// <summary>Gets a plain-language logging-health summary.</summary>
    public string LoggingStatus => Statistics.FileWriteFailures == 0
        ? "Healthy: no diagnostic log write failures have been recorded."
        : "Attention needed: OpenSorSe could not write one or more diagnostic log entries.";

    /// <summary>Gets the empty-state explanation.</summary>
    public string EmptyStateMessage => "No diagnostic events have been recorded in this application session.";

    /// <summary>Gets the latest plain status text for compatibility.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets consistent accessible status presentation.</summary>
    public StatusPresentation Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Refreshes counts and event snapshots.</summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>Copies only selected bounded diagnostic details.</summary>
    public IAsyncRelayCommand CopyDiagnosticDetailsCommand { get; }

    /// <summary>Copies the selected redacted prompt.</summary>
    public IAsyncRelayCommand CopyAiPromptCommand { get; }

    /// <summary>Copies the selected redacted response.</summary>
    public IAsyncRelayCommand CopyAiResponseCommand { get; }

    /// <summary>Copies selected AI request facts and stages.</summary>
    public IAsyncRelayCommand CopyAiRequestDiagnosticsCommand { get; }

    /// <summary>Clears only session AI request diagnostics.</summary>
    public IRelayCommand ClearAiDiagnosticsCommand { get; }

    /// <summary>Opens the shared non-modal advanced-diagnostics viewer.</summary>
    public IRelayCommand OpenAdvancedDiagnosticsCommand { get; }

    /// <summary>Refreshes the bounded process-session projection without reading log files.</summary>
    public void Refresh()
    {
        var selectedSequence = SelectedEvent?.Event.Sequence;
        Statistics = _loggingService.GetStatistics();
        _events.Clear();
        foreach (var diagnosticEvent in _loggingService.GetRecentEvents()
                     .OrderByDescending(item => item.Sequence))
        {
            _events.Add(new DiagnosticEventRow(diagnosticEvent));
        }

        RebuildCategories();
        ApplyFilters(selectedSequence);
        RefreshAiDiagnostics();
        OnPropertyChanged(nameof(AdvancedDiagnosticsRetainedCount));
        StatusText = HasRecordedEvents ? "Diagnostics updated." : EmptyStateMessage;
        Status = !HasRecordedEvents
            ? StatusPresentation.Information(StatusText)
            : Statistics.FileWriteFailures == 0
                ? StatusPresentation.Success(StatusText)
                : StatusPresentation.Warning("Diagnostics updated, but one or more owned log writes failed.");
    }

    private void RefreshAiDiagnostics()
    {
        var selectedId = SelectedAiRequest?.Record.RequestId;
        _aiRequests.Clear();
        if (IsAiDiagnosticsVisible && _aiRequestDiagnosticsStore is not null)
        {
            foreach (var request in _aiRequestDiagnosticsStore.GetRecent())
            {
                _aiRequests.Add(new AiRequestDiagnosticRow(request));
            }
        }

        SelectedAiRequest = selectedId is null
            ? null
            : AiRequests.FirstOrDefault(item => string.Equals(item.Record.RequestId, selectedId, StringComparison.Ordinal));
        OnPropertyChanged(nameof(IsAiDiagnosticsVisible));
        OnPropertyChanged(nameof(HasNoAiRequests));
        ClearAiDiagnosticsCommand.NotifyCanExecuteChanged();
    }

    private void RebuildCategories()
    {
        var previous = SelectedCategory;
        _categories.Clear();
        _categories.Add(AllCategories);
        foreach (var category in Events.Select(item => item.Category).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            _categories.Add(category);
        }

        if (!_categories.Contains(previous))
        {
            _selectedCategory = AllCategories;
            OnPropertyChanged(nameof(SelectedCategory));
        }
    }

    private void ApplyFilters(long? selectedSequence = null)
    {
        selectedSequence ??= SelectedEvent?.Event.Sequence;
        _visibleEvents.Clear();
        foreach (var row in Events.Where(MatchesFilters))
        {
            _visibleEvents.Add(row);
        }

        SelectedEvent = selectedSequence is null
            ? null
            : VisibleEvents.FirstOrDefault(item => item.Event.Sequence == selectedSequence);
        OnPropertyChanged(nameof(HasRecordedEvents));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoFilterMatches));
    }

    private bool MatchesFilters(DiagnosticEventRow row) =>
        (SelectedCategory == AllCategories || string.Equals(row.Category, SelectedCategory, StringComparison.Ordinal)) &&
        SeverityFilter switch
        {
            DiagnosticSeverityFilter.All => true,
            DiagnosticSeverityFilter.Information => row.Event.Severity is LogLevel.Trace or LogLevel.Debug or LogLevel.Information,
            DiagnosticSeverityFilter.Warning => row.Event.Severity == LogLevel.Warning,
            DiagnosticSeverityFilter.Error => row.Event.Severity is LogLevel.Error or LogLevel.Critical,
            _ => false,
        };

    private async Task CopyDiagnosticDetailsAsync()
    {
        if (SelectedEvent is null || _clipboardService is null)
        {
            return;
        }

        try
        {
            await _clipboardService.SetTextAsync(SelectedEvent.FormatCopyText(), CancellationToken.None);
            StatusText = "Diagnostic details copied.";
            Status = StatusPresentation.Success(StatusText);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            StatusText = "Diagnostic details could not be copied. The selected event remains available.";
            Status = StatusPresentation.Error(StatusText);
        }
    }

    private async Task CopyAiTextAsync(string? text, string label)
    {
        if (string.IsNullOrEmpty(text) || _clipboardService is null)
        {
            return;
        }

        try
        {
            await _clipboardService.SetTextAsync(text, CancellationToken.None);
            StatusText = $"{label} copied.";
            Status = StatusPresentation.Success(StatusText);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            StatusText = $"{label} could not be copied. The request remains available.";
            Status = StatusPresentation.Error(StatusText);
        }
    }

    private void ClearAiDiagnostics()
    {
        _aiRequestDiagnosticsStore?.Clear();
        RefreshAiDiagnostics();
        StatusText = "Session AI request diagnostics cleared. No files or settings were changed.";
        Status = StatusPresentation.Success(StatusText);
    }
}
