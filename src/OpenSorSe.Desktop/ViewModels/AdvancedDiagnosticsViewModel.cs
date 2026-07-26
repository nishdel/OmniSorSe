using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Desktop.Services;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Defines status filters for the unified advanced-diagnostics viewer.</summary>
public enum AdvancedDiagnosticStatusFilter
{
    /// <summary>Shows every retained status.</summary>
    All,
    /// <summary>Shows active sessions.</summary>
    Active,
    /// <summary>Shows successful sessions.</summary>
    Succeeded,
    /// <summary>Shows partial-success sessions.</summary>
    PartiallySucceeded,
    /// <summary>Shows deliberately skipped sessions.</summary>
    Skipped,
    /// <summary>Shows safely rejected sessions.</summary>
    Rejected,
    /// <summary>Shows cancelled sessions.</summary>
    Cancelled,
    /// <summary>Shows failed sessions.</summary>
    Failed,
}

/// <summary>Describes one category-filter choice and its instrumentation state.</summary>
/// <param name="DisplayName">The user-facing filter label.</param>
/// <param name="Category">The category, or null for all categories.</param>
/// <param name="IsInstrumented">Whether detailed events are implemented in this release.</param>
public sealed record AdvancedDiagnosticCategoryFilter(
    string DisplayName,
    DiagnosticCategory? Category,
    bool IsInstrumented);

/// <summary>Projects one immutable diagnostic session into a concise list row.</summary>
/// <param name="Session">The immutable common session snapshot.</param>
public sealed record AdvancedDiagnosticSessionRow(DiagnosticSession Session)
{
    /// <summary>Gets the request or session identity.</summary>
    public string SessionId => Session.SessionId;

    /// <summary>Gets the user-facing category.</summary>
    public string Category => DiagnosticCategoryRegistry.Get(Session.Category).DisplayName;

    /// <summary>Gets the concrete operation.</summary>
    public string Operation => Session.Operation;

    /// <summary>Gets the current or final status.</summary>
    public string Status => Session.Status.ToString();

    /// <summary>Gets a concise local start time.</summary>
    public string Started => Session.StartedAtUtc.ToLocalTime().ToString("u");

    /// <summary>Gets the latest elapsed duration.</summary>
    public string Elapsed => Session.Elapsed.ToString("c");

    /// <summary>Gets the retained warning and error count.</summary>
    public int WarningAndErrorCount => Session.Events.Count(item =>
        item.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);
}

/// <summary>Presents every supported category through one live, filterable, exportable viewer.</summary>
public sealed class AdvancedDiagnosticsViewModel : ViewModelBase
{
    private readonly IDiagnosticsCollector _collector;
    private readonly IDiagnosticsExportService _exporter;
    private readonly IClipboardService _clipboard;
    private readonly ObservableCollection<AdvancedDiagnosticSessionRow> _sessions = [];
    private readonly ObservableCollection<AdvancedDiagnosticSessionRow> _visibleSessions = [];
    private AdvancedDiagnosticSessionRow? _selectedSession;
    private AdvancedDiagnosticCategoryFilter _selectedCategoryFilter;
    private AdvancedDiagnosticStatusFilter _selectedStatusFilter;
    private bool _autoScroll = true;
    private bool _wordWrap = true;
    private int _selectedTabIndex;
    private string _statusText = "No advanced diagnostic sessions are retained.";

    /// <summary>Initializes the unified process-session diagnostics presentation.</summary>
    public AdvancedDiagnosticsViewModel(
        IDiagnosticsCollector collector,
        IDiagnosticsExportService exporter,
        IClipboardService clipboard)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        Sessions = new ReadOnlyObservableCollection<AdvancedDiagnosticSessionRow>(_sessions);
        VisibleSessions = new ReadOnlyObservableCollection<AdvancedDiagnosticSessionRow>(_visibleSessions);
        CategoryFilters = Array.AsReadOnly(
            new[]
            {
                new AdvancedDiagnosticCategoryFilter("All categories", null, true),
            }.Concat(DiagnosticCategoryRegistry.All.Select(item =>
                new AdvancedDiagnosticCategoryFilter(
                    item.IsInstrumented
                        ? item.DisplayName
                        : $"{item.DisplayName} (not yet instrumented)",
                    item.Category,
                    item.IsInstrumented))).ToArray());
        _selectedCategoryFilter = CategoryFilters[0];
        StatusFilters = Enum.GetValues<AdvancedDiagnosticStatusFilter>();
        ClearSelectedCommand = new RelayCommand(ClearSelected, () => SelectedSession is not null);
        ClearAllCommand = new RelayCommand(ClearAll, () => _sessions.Count > 0);
        CopyCurrentSectionCommand = new AsyncRelayCommand(
            () => CopyAsync(CurrentSectionText),
            () => SelectedSession is not null);
        CopyCompleteDiagnosticCommand = new AsyncRelayCommand(
            () => CopyAsync(BuildSelectedText()),
            () => SelectedSession is not null);
        Refresh(_collector.GetRecent(), null, true);
    }

    /// <summary>Gets every retained session in reverse chronological order.</summary>
    public ReadOnlyObservableCollection<AdvancedDiagnosticSessionRow> Sessions { get; }

    /// <summary>Gets sessions matching current category and status filters.</summary>
    public ReadOnlyObservableCollection<AdvancedDiagnosticSessionRow> VisibleSessions { get; }

    /// <summary>Gets stable category-filter choices, including truthful planned placeholders.</summary>
    public IReadOnlyList<AdvancedDiagnosticCategoryFilter> CategoryFilters { get; }

    /// <summary>Gets supported status-filter choices.</summary>
    public IReadOnlyList<AdvancedDiagnosticStatusFilter> StatusFilters { get; }

    /// <summary>Gets or sets the active category filter.</summary>
    public AdvancedDiagnosticCategoryFilter SelectedCategoryFilter
    {
        get => _selectedCategoryFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedCategoryFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>Gets or sets the active status filter.</summary>
    public AdvancedDiagnosticStatusFilter SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>Gets or sets the selected retained session.</summary>
    public AdvancedDiagnosticSessionRow? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                NotifySelectedSessionChanged();
                ClearSelectedCommand.NotifyCanExecuteChanged();
                CopyCurrentSectionCommand.NotifyCanExecuteChanged();
                CopyCompleteDiagnosticCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets whether live text surfaces should follow new content.</summary>
    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    /// <summary>Gets or sets whether large text surfaces wrap long lines.</summary>
    public bool WordWrap
    {
        get => _wordWrap;
        set => SetProperty(ref _wordWrap, value);
    }

    /// <summary>Gets or sets the active shared tab index.</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, Math.Clamp(value, 0, 6)))
            {
                OnPropertyChanged(nameof(CurrentSectionText));
            }
        }
    }

    /// <summary>Gets the latest plain status message.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets a privacy reminder for every category.</summary>
    public string PrivacyNotice =>
        "Detailed sessions are memory-only. Redacted mode hides paths, filenames, document/OCR text, metadata, tags, search terms, and AI content. Secrets and authorization values are always removed.";

    /// <summary>Gets whether no sessions match current filters.</summary>
    public bool HasNoVisibleSessions => VisibleSessions.Count == 0;

    /// <summary>Gets the request or session identity.</summary>
    public string SessionId => SelectedSession?.Session.SessionId ?? "—";

    /// <summary>Gets the selected category.</summary>
    public string Category => SelectedSession?.Category ?? "—";

    /// <summary>Gets the selected operation.</summary>
    public string Operation => SelectedSession?.Operation ?? "—";

    /// <summary>Gets the selected start time.</summary>
    public string Started => SelectedSession?.Started ?? "—";

    /// <summary>Gets the selected elapsed time.</summary>
    public string Elapsed => SelectedSession?.Elapsed ?? "—";

    /// <summary>Gets the current or final status.</summary>
    public string FinalStatus => SelectedSession?.Status ?? "—";

    /// <summary>Gets related parent and downstream session identities.</summary>
    public string RelatedSessions => SelectedSession?.Session.RelatedSessionIds.Count > 0
        ? string.Join(Environment.NewLine, SelectedSession.Session.RelatedSessionIds)
        : "None";

    /// <summary>Gets high-level context and terminal facts.</summary>
    public string OverviewText => FormatOverview(SelectedSession?.Session);

    /// <summary>Gets every event in stable sequence order.</summary>
    public string TimelineText => FormatTimeline(SelectedSession?.Session);

    /// <summary>Gets captured inputs.</summary>
    public string InputsText => FormatSection(SelectedSession?.Session, DiagnosticSection.Inputs);

    /// <summary>Gets intermediate extraction, transport, and per-entry results.</summary>
    public string IntermediateResultsText => FormatSection(
        SelectedSession?.Session,
        DiagnosticSection.IntermediateResults);

    /// <summary>Gets final and downstream outputs.</summary>
    public string OutputsText => FormatSection(SelectedSession?.Session, DiagnosticSection.Outputs);

    /// <summary>Gets warnings, controlled errors, and validation failures.</summary>
    public string WarningsAndErrorsText => FormatWarningsAndErrors(SelectedSession?.Session);

    /// <summary>Gets timing, size, count, and memory facts.</summary>
    public string PerformanceText => FormatPerformance(SelectedSession?.Session);

    /// <summary>Gets the text for the active shared tab.</summary>
    public string CurrentSectionText => SelectedTabIndex switch
    {
        0 => OverviewText,
        1 => TimelineText,
        2 => InputsText,
        3 => IntermediateResultsText,
        4 => OutputsText,
        5 => WarningsAndErrorsText,
        _ => PerformanceText,
    };

    /// <summary>Clears one retained session.</summary>
    public IRelayCommand ClearSelectedCommand { get; }

    /// <summary>Clears all retained detailed sessions.</summary>
    public IRelayCommand ClearAllCommand { get; }

    /// <summary>Copies the active shared tab.</summary>
    public IAsyncRelayCommand CopyCurrentSectionCommand { get; }

    /// <summary>Copies the complete selected diagnostic.</summary>
    public IAsyncRelayCommand CopyCompleteDiagnosticCommand { get; }

    /// <summary>Updates or inserts one immutable session snapshot.</summary>
    public void Upsert(DiagnosticSession session, bool select)
    {
        ArgumentNullException.ThrowIfNull(session);
        Refresh(
            _collector.GetRecent(),
            select ? session.SessionId : SelectedSession?.SessionId,
            selectNewest: false);
    }

    /// <summary>Reloads the viewer after the common store was cleared or reconfigured.</summary>
    public void Reload() =>
        Refresh(_collector.GetRecent(), SelectedSession?.SessionId, selectNewest: false);

    /// <summary>Builds the selected retained session as JSON.</summary>
    public string BuildSelectedJson() =>
        SelectedSession is null
            ? "{}"
            : TryExport(() => _exporter.ExportJson(SelectedSession.Session), "{}");

    /// <summary>Builds the selected retained session as plain text.</summary>
    public string BuildSelectedText() =>
        SelectedSession is null
            ? "No advanced diagnostic session is selected."
            : TryExport(
                () => _exporter.ExportText(SelectedSession.Session),
                "The diagnostic export could not be created.");

    /// <summary>Builds all retained sessions as JSON.</summary>
    public string BuildAllJson() => TryExport(
        () => _exporter.ExportAllJson(_sessions.Select(item => item.Session).ToArray()),
        "[]");

    /// <summary>Builds all retained sessions as plain text.</summary>
    public string BuildAllText() => TryExport(
        () => _exporter.ExportAllText(_sessions.Select(item => item.Session).ToArray()),
        "The diagnostic export could not be created.");

    /// <summary>Reports the result of an explicitly initiated file export.</summary>
    public void ReportExportResult(bool succeeded) =>
        StatusText = succeeded
            ? "The diagnostic export was saved."
            : "The diagnostic export could not be saved.";

    private async Task CopyAsync(string value)
    {
        try
        {
            await _clipboard.SetTextAsync(value ?? string.Empty, CancellationToken.None);
            StatusText = "Diagnostic content was copied.";
        }
        catch
        {
            StatusText = "Diagnostic content could not be copied.";
        }
    }

    private string TryExport(Func<string> export, string fallback)
    {
        try
        {
            return export();
        }
        catch
        {
            StatusText = "The diagnostic export could not be created.";
            return fallback;
        }
    }

    private void ClearSelected()
    {
        if (SelectedSession is null)
        {
            return;
        }

        var id = SelectedSession.SessionId;
        _collector.Clear(id);
        var row = _sessions.FirstOrDefault(item =>
            string.Equals(item.SessionId, id, StringComparison.Ordinal));
        if (row is not null)
        {
            _sessions.Remove(row);
        }

        ApplyFilters();
        StatusText = "The selected retained session was cleared; the feature operation was not affected.";
        ClearAllCommand.NotifyCanExecuteChanged();
    }

    private void ClearAll()
    {
        _collector.ClearAll();
        _sessions.Clear();
        _visibleSessions.Clear();
        SelectedSession = null;
        StatusText = "All retained advanced diagnostics were cleared.";
        OnPropertyChanged(nameof(HasNoVisibleSessions));
        ClearAllCommand.NotifyCanExecuteChanged();
    }

    private void Refresh(
        IReadOnlyList<DiagnosticSession> sessions,
        string? selectedId,
        bool selectNewest)
    {
        _sessions.Clear();
        foreach (var session in sessions.OrderByDescending(item => item.StartedAtUtc))
        {
            _sessions.Add(new AdvancedDiagnosticSessionRow(session));
        }

        ApplyFilters(selectNewest ? _sessions.FirstOrDefault()?.SessionId : selectedId);
        StatusText = _sessions.Count == 0
            ? "No advanced diagnostic sessions are retained."
            : $"{_sessions.Count} advanced diagnostic session(s) retained in memory.";
        ClearAllCommand.NotifyCanExecuteChanged();
    }

    private void ApplyFilters(string? selectedId = null)
    {
        selectedId ??= SelectedSession?.SessionId;
        _visibleSessions.Clear();
        foreach (var row in _sessions.Where(MatchesFilters))
        {
            _visibleSessions.Add(row);
        }

        SelectedSession = selectedId is null
            ? _visibleSessions.FirstOrDefault()
            : _visibleSessions.FirstOrDefault(item =>
                  string.Equals(item.SessionId, selectedId, StringComparison.Ordinal)) ??
              _visibleSessions.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoVisibleSessions));
    }

    private bool MatchesFilters(AdvancedDiagnosticSessionRow row) =>
        (SelectedCategoryFilter.Category is null ||
         row.Session.Category == SelectedCategoryFilter.Category) &&
        SelectedStatusFilter switch
        {
            AdvancedDiagnosticStatusFilter.All => true,
            AdvancedDiagnosticStatusFilter.Active => row.Session.Status == DiagnosticStatus.Active,
            AdvancedDiagnosticStatusFilter.Succeeded => row.Session.Status == DiagnosticStatus.Succeeded,
            AdvancedDiagnosticStatusFilter.PartiallySucceeded => row.Session.Status == DiagnosticStatus.PartiallySucceeded,
            AdvancedDiagnosticStatusFilter.Skipped => row.Session.Status == DiagnosticStatus.Skipped,
            AdvancedDiagnosticStatusFilter.Rejected => row.Session.Status == DiagnosticStatus.Rejected,
            AdvancedDiagnosticStatusFilter.Cancelled => row.Session.Status == DiagnosticStatus.Cancelled,
            AdvancedDiagnosticStatusFilter.Failed => row.Session.Status == DiagnosticStatus.Failed,
            _ => false,
        };

    private void NotifySelectedSessionChanged()
    {
        foreach (var property in new[]
        {
            nameof(SessionId), nameof(Category), nameof(Operation), nameof(Started), nameof(Elapsed),
            nameof(FinalStatus), nameof(RelatedSessions), nameof(OverviewText), nameof(TimelineText),
            nameof(InputsText), nameof(IntermediateResultsText), nameof(OutputsText),
            nameof(WarningsAndErrorsText), nameof(PerformanceText), nameof(CurrentSectionText),
        })
        {
            OnPropertyChanged(property);
        }
    }

    private static string FormatOverview(DiagnosticSession? session)
    {
        if (session is null)
        {
            return "Select a retained diagnostic session.";
        }

        var output = new StringBuilder();
        output.AppendLine($"Session ID: {session.SessionId}");
        output.AppendLine($"Category: {DiagnosticCategoryRegistry.Get(session.Category).DisplayName}");
        output.AppendLine($"Operation: {session.Operation}");
        output.AppendLine($"Started (UTC): {session.StartedAtUtc:O}");
        output.AppendLine($"Elapsed: {session.Elapsed:c}");
        output.AppendLine($"Status: {session.Status}");
        output.AppendLine($"Content retention mode: {session.ContentRetentionMode}");
        output.AppendLine($"Related sessions: {(session.RelatedSessionIds.Count == 0 ? "None" : string.Join(", ", session.RelatedSessionIds))}");
        output.AppendLine($"Warnings: {session.Events.Count(item => item.Severity == DiagnosticSeverity.Warning)}");
        output.AppendLine($"Errors: {session.Events.Count(item => item.Severity == DiagnosticSeverity.Error)}");
        output.AppendLine($"Dropped events: {session.DroppedEventCount}");
        output.AppendLine($"Truncated or sampled: {session.WasTruncated}");
        if (session.Context.Count > 0)
        {
            output.AppendLine().AppendLine("Context:");
            AppendFields(output, session.Context);
        }

        return output.ToString().TrimEnd();
    }

    private static string FormatTimeline(DiagnosticSession? session) =>
        session is null
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                session.Events.OrderBy(item => item.Sequence).Select(item =>
                    $"{item.TimestampUtc.ToLocalTime():HH:mm:ss.fff}  {item.Severity,-11}  {item.Status,-18}  {item.Stage}: {item.Message}"));

    private static string FormatSection(DiagnosticSession? session, DiagnosticSection section)
    {
        if (session is null)
        {
            return string.Empty;
        }

        var output = new StringBuilder();
        foreach (var item in session.Events
                     .Where(item => item.Section == section)
                     .OrderBy(item => item.Sequence))
        {
            AppendEvent(output, item);
        }

        return output.Length == 0 ? "No data was retained for this section." : output.ToString().TrimEnd();
    }

    private static string FormatWarningsAndErrors(DiagnosticSession? session)
    {
        if (session is null)
        {
            return string.Empty;
        }

        var events = session.Events
            .Where(item =>
                item.Section == DiagnosticSection.WarningsAndErrors ||
                item.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .DistinctBy(item => item.Sequence)
            .OrderBy(item => item.Sequence)
            .ToArray();
        if (events.Length == 0)
        {
            return "No warnings or errors were retained.";
        }

        var output = new StringBuilder();
        foreach (var item in events)
        {
            AppendEvent(output, item);
        }

        return output.ToString().TrimEnd();
    }

    private static string FormatPerformance(DiagnosticSession? session)
    {
        if (session is null)
        {
            return string.Empty;
        }

        var output = new StringBuilder();
        output.AppendLine($"Elapsed: {session.Elapsed:c}");
        output.AppendLine($"Retained events: {session.Events.Count}");
        output.AppendLine($"Dropped events: {session.DroppedEventCount}");
        output.AppendLine($"Approximate retained bytes: {session.ApproximateRetainedBytes}");
        foreach (var item in session.Events
                     .Where(item => item.Section == DiagnosticSection.Performance)
                     .OrderBy(item => item.Sequence))
        {
            AppendEvent(output, item);
        }

        return output.ToString().TrimEnd();
    }

    private static void AppendEvent(StringBuilder output, DiagnosticEvent item)
    {
        if (output.Length > 0)
        {
            output.AppendLine();
        }

        output.AppendLine($"{item.TimestampUtc:O}  {item.Severity}  {item.Status}");
        output.AppendLine($"{item.Stage}: {item.Message}");
        AppendFields(output, item.Fields);
    }

    private static void AppendFields(StringBuilder output, IReadOnlyList<DiagnosticField> fields)
    {
        foreach (var field in fields)
        {
            output.AppendLine($"{field.Name}: {field.Value}");
        }
    }
}
