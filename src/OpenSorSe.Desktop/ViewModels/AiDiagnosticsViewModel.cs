using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.AI;
using OpenSorSe.Desktop.Services;

#pragma warning disable CS1591 // Binding surface is self-describing and documented by the ViewModel type.
namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Presents immutable live diagnostic snapshots without owning transport behavior.</summary>
public sealed class AiDiagnosticsViewModel : ViewModelBase
{
    private readonly IAiDiagnosticsCollector _collector;
    private readonly IClipboardService _clipboard;
    private readonly ObservableCollection<AiDiagnosticSession> _sessions = [];
    private AiDiagnosticSession? _selectedSession;
    private bool _autoScroll = true;
    private bool _wordWrap = true;
    private string _statusText = "Waiting for an AI request.";

    public AiDiagnosticsViewModel(IAiDiagnosticsCollector collector, IClipboardService clipboard)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        Sessions = new ReadOnlyObservableCollection<AiDiagnosticSession>(_sessions);
        ClearCurrentCommand = new RelayCommand(ClearCurrent, () => SelectedSession is not null);
        ClearAllCommand = new RelayCommand(ClearAll, () => _sessions.Count > 0);
        CopyCompleteReportCommand = new AsyncRelayCommand(() => CopyAsync(BuildTextReport()));
        Refresh(collector.GetRecent(), selectNewest: true);
    }

    public ReadOnlyObservableCollection<AiDiagnosticSession> Sessions { get; }

    public AiDiagnosticSession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                NotifySelection();
                ClearCurrentCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool AutoScroll { get => _autoScroll; set => SetProperty(ref _autoScroll, value); }
    public bool WordWrap { get => _wordWrap; set => SetProperty(ref _wordWrap, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public string RequestId => SelectedSession?.RequestId ?? "—";
    public string OperationType => SelectedSession?.OperationType.ToString() ?? "—";
    public string Model => SelectedSession?.Model ?? "—";
    public string Endpoint => SelectedSession?.Endpoint ?? "—";
    public string StartedAt => SelectedSession?.StartedAtUtc.ToLocalTime().ToString("u") ?? "—";
    public string Elapsed => SelectedSession?.Elapsed.ToString("g") ?? "—";
    public string FinalStatus => SelectedSession?.Status.ToString() ?? "—";
    public string HttpStatus => SelectedSession?.HttpStatusCode?.ToString() ?? "—";
    public string CancellationStatus => SelectedSession?.WasCancelled == true ? "Cancelled" : "Not cancelled";
    public string RetryAttempt => SelectedSession?.RetryAttempt.ToString() ?? "—";
    public string TransportSummary => SelectedSession is null ? "—" :
        $"Content type: {SelectedSession.ContentType}\nResponse size: {SelectedSession.ResponseSizeBytes} bytes\nComplete: {SelectedSession.ResponseComplete}\nStreaming: {SelectedSession.WasStreaming}";
    public string SystemPrompt => SelectedSession?.SystemPrompt ?? "";
    public string UserPrompt => SelectedSession?.UserPrompt ?? "";
    public string RequestJson => SelectedSession?.RequestJson ?? "";
    public string RawHttpResponse => SelectedSession?.RawHttpResponse ?? "";
    public string ExtractedAssistantResponse => SelectedSession?.ExtractedAssistantResponse ?? "";
    public string ParsedStructuredResponse => SelectedSession?.ParsedStructuredResponse ?? "";
    public string StageText => SelectedSession is null ? "" : string.Join(Environment.NewLine,
        SelectedSession.Stages.Select(stage =>
            $"{stage.TimestampUtc.ToLocalTime():HH:mm:ss.fff}  {stage.State,-9}  {stage.Elapsed.TotalMilliseconds,8:0} ms  {stage.Name}{(string.IsNullOrEmpty(stage.Error) ? "" : $" — {stage.Error}")}"));
    public string ValidationText => SelectedSession is null ? "" : string.Join(Environment.NewLine,
        SelectedSession.Validation.Select(check =>
            $"{(check.Passed ? "PASS" : "FAIL")}  {check.PropertyName} | {(check.Required ? "required" : "optional")} | expected {check.ExpectedType}{(check.AllowedValues is null ? "" : $" [{check.AllowedValues}]")} | actual {check.ActualType}: {check.ActualValue}\n  {check.Message}")
        .Concat(SelectedSession.Errors.Select(error => $"ERROR  {error}")));

    public IRelayCommand ClearCurrentCommand { get; }
    public IRelayCommand ClearAllCommand { get; }
    public IAsyncRelayCommand CopyCompleteReportCommand { get; }

    public void Upsert(AiDiagnosticSession session, bool select)
    {
        var index = _sessions.ToList().FindIndex(item => item.RequestId == session.RequestId);
        if (index >= 0) _sessions[index] = session;
        else _sessions.Insert(0, session);
        while (_sessions.Count > AiRequestDiagnosticLimits.MaximumRetainedRequests) _sessions.RemoveAt(_sessions.Count - 1);
        if (select || SelectedSession?.RequestId == session.RequestId) SelectedSession = session;
        ClearAllCommand.NotifyCanExecuteChanged();
        StatusText = $"{_sessions.Count} request diagnostic(s) retained in memory.";
    }

    public Task CopyAsync(string text) => _clipboard.SetTextAsync(text ?? "", CancellationToken.None);

    public string BuildJsonReport() =>
        SelectedSession is null ? "{}" : JsonSerializer.Serialize(SelectedSession, new JsonSerializerOptions { WriteIndented = true });

    public string BuildTextReport()
    {
        if (SelectedSession is null) return "No AI diagnostic request is selected.";
        var builder = new StringBuilder();
        builder.AppendLine("OpenSorSe AI Request Diagnostic");
        builder.AppendLine($"Request ID: {RequestId}");
        builder.AppendLine($"Operation: {OperationType}");
        builder.AppendLine($"Model: {Model}");
        builder.AppendLine($"Endpoint: {Endpoint}");
        builder.AppendLine($"Started: {StartedAt}");
        builder.AppendLine($"Elapsed: {Elapsed}");
        builder.AppendLine($"Status: {FinalStatus}; HTTP: {HttpStatus}; {CancellationStatus}; attempt {RetryAttempt}");
        Add("Stages", StageText); Add("System prompt", SystemPrompt); Add("User prompt", UserPrompt);
        Add("Request JSON", RequestJson); Add("Raw HTTP response", RawHttpResponse);
        Add("Extracted assistant response", ExtractedAssistantResponse);
        Add("Parsed structured response", ParsedStructuredResponse); Add("Validation and errors", ValidationText);
        return builder.ToString();

        void Add(string heading, string value)
        {
            builder.AppendLine().AppendLine($"== {heading} ==").AppendLine(value);
        }
    }

    private void ClearCurrent()
    {
        if (SelectedSession is null) return;
        _collector.Clear(SelectedSession.RequestId);
        _sessions.Remove(SelectedSession);
        SelectedSession = _sessions.FirstOrDefault();
        StatusText = "Current display and retained request cleared.";
        ClearAllCommand.NotifyCanExecuteChanged();
    }

    private void ClearAll()
    {
        _collector.Clear();
        _sessions.Clear();
        SelectedSession = null;
        StatusText = "All retained AI diagnostics cleared.";
        ClearAllCommand.NotifyCanExecuteChanged();
    }

    private void Refresh(IReadOnlyList<AiDiagnosticSession> sessions, bool selectNewest)
    {
        _sessions.Clear();
        foreach (var session in sessions) _sessions.Add(session);
        if (selectNewest) SelectedSession = _sessions.FirstOrDefault();
    }

    private void NotifySelection()
    {
        foreach (var property in new[]
        {
            nameof(RequestId), nameof(OperationType), nameof(Model), nameof(Endpoint), nameof(StartedAt),
            nameof(Elapsed), nameof(FinalStatus), nameof(HttpStatus), nameof(CancellationStatus), nameof(RetryAttempt),
            nameof(TransportSummary), nameof(SystemPrompt), nameof(UserPrompt), nameof(RequestJson),
            nameof(RawHttpResponse), nameof(ExtractedAssistantResponse), nameof(ParsedStructuredResponse),
            nameof(StageText), nameof(ValidationText),
        }) OnPropertyChanged(property);
    }
}
#pragma warning restore CS1591
