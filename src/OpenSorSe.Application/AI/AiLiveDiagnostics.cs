using System.Text.Json;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Application.AI;

/// <summary>Identifies the lifecycle state of one diagnostic stage or request.</summary>
public enum AiDiagnosticState
{
    /// <summary>Stage has not started.</summary>
    Pending,
    /// <summary>Stage is running.</summary>
    Active,
    /// <summary>Stage or request completed successfully.</summary>
    Succeeded,
    /// <summary>Response was safely rejected.</summary>
    Rejected,
    /// <summary>Request was cancelled.</summary>
    Cancelled,
    /// <summary>Stage or request failed.</summary>
    Failed,
}

/// <summary>Identifies captured text without coupling the collector to a UI.</summary>
public enum AiDiagnosticContentKind
{
    /// <summary>Final system prompt.</summary>
    SystemPrompt,
    /// <summary>Final user prompt.</summary>
    UserPrompt,
    /// <summary>Serialized HTTP request body.</summary>
    RequestJson,
    /// <summary>Complete HTTP response body.</summary>
    RawHttpResponse,
    /// <summary>Assistant text extracted from the provider envelope.</summary>
    ExtractedAssistantResponse,
    /// <summary>Pretty-printed structured response.</summary>
    ParsedStructuredResponse,
}

/// <summary>Describes one live processing stage.</summary>
public sealed record AiDiagnosticStage(
    string Name,
    AiDiagnosticState State,
    DateTimeOffset TimestampUtc,
    TimeSpan Elapsed,
    string? Error = null);

/// <summary>Describes one explicit structured-response validation check.</summary>
public sealed record AiDiagnosticValidation(
    string PropertyName,
    bool Required,
    string ExpectedType,
    string? AllowedValues,
    string ActualType,
    string ActualValue,
    bool Passed,
    string Message);

/// <summary>Immutable view of one live request retained only for this process session.</summary>
public sealed record AiDiagnosticSession(
    string RequestId,
    AiSuggestionKind OperationType,
    string Model,
    string Endpoint,
    DateTimeOffset StartedAtUtc,
    TimeSpan Elapsed,
    AiDiagnosticState Status,
    int? HttpStatusCode,
    bool WasCancelled,
    int RetryAttempt,
    string ContentType,
    IReadOnlyDictionary<string, string> SafeResponseHeaders,
    int ResponseSizeBytes,
    bool ResponseComplete,
    bool WasStreaming,
    IReadOnlyList<AiDiagnosticStage> Stages,
    string SystemPrompt,
    string UserPrompt,
    string RequestJson,
    string RawHttpResponse,
    string ExtractedAssistantResponse,
    string ParsedStructuredResponse,
    IReadOnlyList<AiDiagnosticValidation> Validation,
    IReadOnlyList<string> Errors);

/// <summary>Raised whenever a live diagnostic session is created or updated.</summary>
public sealed class AiDiagnosticSessionChangedEventArgs(AiDiagnosticSession session, bool isNew) : EventArgs
{
    /// <summary>Gets the latest immutable session snapshot.</summary>
    public AiDiagnosticSession Session { get; } = session;
    /// <summary>Gets whether the session was just created.</summary>
    public bool IsNew { get; } = isNew;
}

/// <summary>Collects observable, bounded, process-memory-only Ollama diagnostics.</summary>
public interface IAiDiagnosticsCollector
{
    /// <summary>Gets whether collection is enabled.</summary>
    bool IsEnabled { get; }
    /// <summary>Gets whether retained display content is exact rather than redacted.</summary>
    bool ShowUnredactedContent { get; }
    /// <summary>Occurs after a session is created or updated.</summary>
    event EventHandler<AiDiagnosticSessionChangedEventArgs>? SessionChanged;
    /// <summary>Applies privacy settings and clears history when disabled.</summary>
    void Configure(bool enabled, bool showUnredactedContent);
    /// <summary>Begins one session and returns its identity, or null when disabled.</summary>
    string? Begin(AiSuggestionKind operationType, string model, string endpoint, int retryAttempt = 1);
    /// <summary>Publishes one stage transition.</summary>
    void ReportStage(string? requestId, string name, AiDiagnosticState state, TimeSpan elapsed, string? error = null);
    /// <summary>Captures one diagnostic artifact.</summary>
    void Capture(string? requestId, AiDiagnosticContentKind kind, string? value);
    /// <summary>Captures safe version identities for the exact prompt and schema contract.</summary>
    void SetContract(string? requestId, string taskId, string promptVersion, string schemaSha256);
    /// <summary>Captures safe HTTP transport facts.</summary>
    void SetTransport(string? requestId, int? statusCode, string? contentType, IReadOnlyDictionary<string, string>? safeHeaders, int responseSizeBytes, bool complete, bool streaming);
    /// <summary>Captures parsed content and validation checks.</summary>
    void SetValidation(string? requestId, string? parsedJson, IReadOnlyList<AiDiagnosticValidation> validation, IReadOnlyList<string> errors);
    /// <summary>Completes a session.</summary>
    void Complete(string? requestId, AiDiagnosticState state, bool cancelled, TimeSpan elapsed, string? error = null);
    /// <summary>Adds a related OCR, scan, or downstream diagnostic session.</summary>
    void Relate(string? requestId, string? relatedRequestId)
    {
    }
    /// <summary>Gets newest-first session snapshots.</summary>
    IReadOnlyList<AiDiagnosticSession> GetRecent();
    /// <summary>Clears one retained request.</summary>
    void Clear(string requestId);
    /// <summary>Clears all retained requests.</summary>
    void Clear();
}

/// <summary>Creates a failure-isolating publisher facade for optional diagnostics.</summary>
public static class AiDiagnosticsIsolation
{
    /// <summary>Returns a collector facade whose failures cannot escape into an AI operation.</summary>
    public static IAiDiagnosticsCollector? Protect(IAiDiagnosticsCollector? collector) =>
        collector is null ? null : new ProtectedCollector(collector);

    private sealed class ProtectedCollector(IAiDiagnosticsCollector inner) : IAiDiagnosticsCollector
    {
        public bool IsEnabled { get { try { return inner.IsEnabled; } catch { return false; } } }
        public bool ShowUnredactedContent { get { try { return inner.ShowUnredactedContent; } catch { return false; } } }
        public event EventHandler<AiDiagnosticSessionChangedEventArgs>? SessionChanged { add { } remove { } }
        public void Configure(bool enabled, bool showUnredactedContent) => Try(() => inner.Configure(enabled, showUnredactedContent));
        public string? Begin(AiSuggestionKind operationType, string model, string endpoint, int retryAttempt = 1)
        {
            try { return inner.Begin(operationType, model, endpoint, retryAttempt); } catch { return null; }
        }
        public void ReportStage(string? requestId, string name, AiDiagnosticState state, TimeSpan elapsed, string? error = null) =>
            Try(() => inner.ReportStage(requestId, name, state, elapsed, error));
        public void Capture(string? requestId, AiDiagnosticContentKind kind, string? value) => Try(() => inner.Capture(requestId, kind, value));
        public void SetContract(string? requestId, string taskId, string promptVersion, string schemaSha256) =>
            Try(() => inner.SetContract(requestId, taskId, promptVersion, schemaSha256));
        public void SetTransport(string? requestId, int? statusCode, string? contentType, IReadOnlyDictionary<string, string>? safeHeaders, int responseSizeBytes, bool complete, bool streaming) =>
            Try(() => inner.SetTransport(requestId, statusCode, contentType, safeHeaders, responseSizeBytes, complete, streaming));
        public void SetValidation(string? requestId, string? parsedJson, IReadOnlyList<AiDiagnosticValidation> validation, IReadOnlyList<string> errors) =>
            Try(() => inner.SetValidation(requestId, parsedJson, validation, errors));
        public void Complete(string? requestId, AiDiagnosticState state, bool cancelled, TimeSpan elapsed, string? error = null) =>
            Try(() => inner.Complete(requestId, state, cancelled, elapsed, error));
        public void Relate(string? requestId, string? relatedRequestId) =>
            Try(() => inner.Relate(requestId, relatedRequestId));
        public IReadOnlyList<AiDiagnosticSession> GetRecent() { try { return inner.GetRecent(); } catch { return []; } }
        public void Clear(string requestId) => Try(() => inner.Clear(requestId));
        public void Clear() => Try(inner.Clear);
        private static void Try(Action action) { try { action(); } catch { } }
    }
}

/// <summary>
/// AI compatibility facade over the common diagnostics store; it retains no independent history.
/// </summary>
public sealed class AiDiagnosticsCollector : IAiDiagnosticsCollector
{
    private static readonly string[] ExpectedStages =
    [
        "Preparing file context",
        "Building system prompt",
        "Building user prompt",
        "Serializing Ollama request",
        "Connecting to Ollama",
        "Request sent",
        "Waiting for model",
        "Response headers received",
        "Response body received",
        "Extracting assistant content",
        "Preserving exact assistant content",
        "Parsing structured JSON",
        "Validating response",
        "Completed",
    ];
    private readonly IDiagnosticsCollector _collector;

    /// <summary>Initializes a standalone compatibility collector for tests and non-DI callers.</summary>
    public AiDiagnosticsCollector()
        : this(new InMemoryDiagnosticsCollector())
    {
    }

    /// <summary>Initializes the AI facade over the application-wide diagnostics collector.</summary>
    /// <param name="collector">The shared process-session collector.</param>
    public AiDiagnosticsCollector(IDiagnosticsCollector collector)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _collector.SessionChanged += OnSessionChanged;
    }

    /// <inheritdoc />
    public bool IsEnabled => _collector.IsCategoryEnabled(DiagnosticCategory.Ai);

    /// <inheritdoc />
    public bool ShowUnredactedContent => _collector.ShowUnredactedContent;

    /// <inheritdoc />
    public event EventHandler<AiDiagnosticSessionChangedEventArgs>? SessionChanged;

    /// <inheritdoc />
    public void Configure(bool enabled, bool showUnredactedContent) =>
        _collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = enabled,
            AiDiagnostics = enabled,
            ShowUnredactedDiagnosticContent = showUnredactedContent,
        });

    /// <inheritdoc />
    public string? Begin(
        AiSuggestionKind operationType,
        string model,
        string endpoint,
        int retryAttempt = 1) =>
        _collector.BeginSession(
            DiagnosticCategory.Ai,
            operationType.ToString(),
            [
                new DiagnosticField("Model", model),
                new DiagnosticField("Endpoint", endpoint),
                new DiagnosticField("Retry attempt", Math.Max(1, retryAttempt).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ]);

    /// <inheritdoc />
    public void ReportStage(
        string? requestId,
        string name,
        AiDiagnosticState state,
        TimeSpan elapsed,
        string? error = null) =>
        _collector.Publish(
            requestId,
            name,
            ToCommon(state),
            string.IsNullOrWhiteSpace(error) ? DiagnosticSeverity.Information : DiagnosticSeverity.Error,
            string.IsNullOrWhiteSpace(error) ? DiagnosticSection.Overview : DiagnosticSection.WarningsAndErrors,
            string.IsNullOrWhiteSpace(error) ? name : "The AI request stage reported an error.",
            [
                new DiagnosticField("Elapsed milliseconds", elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticField("Error", error ?? string.Empty, DiagnosticDataClassification.Metadata),
            ]);

    /// <inheritdoc />
    public void Capture(string? requestId, AiDiagnosticContentKind kind, string? value)
    {
        var (section, name) = kind switch
        {
            AiDiagnosticContentKind.SystemPrompt => (DiagnosticSection.Inputs, "Exact system prompt"),
            AiDiagnosticContentKind.UserPrompt => (DiagnosticSection.Inputs, "Exact user prompt"),
            AiDiagnosticContentKind.RequestJson => (DiagnosticSection.Inputs, "Serialized Ollama request"),
            AiDiagnosticContentKind.RawHttpResponse => (DiagnosticSection.IntermediateResults, "Raw HTTP response"),
            AiDiagnosticContentKind.ExtractedAssistantResponse => (DiagnosticSection.IntermediateResults, "Extracted assistant content"),
            AiDiagnosticContentKind.ParsedStructuredResponse => (DiagnosticSection.Outputs, "Parsed structured response"),
            _ => (DiagnosticSection.Overview, kind.ToString()),
        };
        _collector.Publish(
            requestId,
            name,
            DiagnosticStatus.Active,
            DiagnosticSeverity.Information,
            section,
            $"{name} captured.",
            [new DiagnosticField(name, value ?? string.Empty, DiagnosticDataClassification.Content)]);
    }

    /// <inheritdoc />
    public void SetContract(
        string? requestId,
        string taskId,
        string promptVersion,
        string schemaSha256) =>
        _collector.Publish(
            requestId,
            "Prompt contract",
            DiagnosticStatus.Active,
            DiagnosticSeverity.Information,
            DiagnosticSection.Overview,
            "The versioned prompt and exact structured-output schema were selected.",
            [
                new DiagnosticField("Task ID", taskId),
                new DiagnosticField("Prompt version", promptVersion),
                new DiagnosticField("Schema SHA-256", schemaSha256),
                new DiagnosticField("Intended model size", AiPromptTemplates.IntendedModelSizeRange),
            ]);

    /// <inheritdoc />
    public void SetTransport(
        string? requestId,
        int? statusCode,
        string? contentType,
        IReadOnlyDictionary<string, string>? safeHeaders,
        int responseSizeBytes,
        bool complete,
        bool streaming) =>
        _collector.Publish(
            requestId,
            "HTTP transport",
            DiagnosticStatus.Active,
            DiagnosticSeverity.Information,
            DiagnosticSection.Performance,
            "HTTP transport facts updated.",
            [
                new DiagnosticField("HTTP status", statusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty),
                new DiagnosticField("Content type", contentType ?? string.Empty),
                new DiagnosticField("Safe response headers", JsonSerializer.Serialize(safeHeaders ?? new Dictionary<string, string>())),
                new DiagnosticField("Response size bytes", Math.Max(0, responseSizeBytes).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticField("Response complete", complete.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticField("Streaming", streaming.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ]);

    /// <inheritdoc />
    public void SetValidation(
        string? requestId,
        string? parsedJson,
        IReadOnlyList<AiDiagnosticValidation> validation,
        IReadOnlyList<string> errors)
    {
        Capture(requestId, AiDiagnosticContentKind.ParsedStructuredResponse, parsedJson);
        foreach (var item in validation.Take(100))
        {
            _collector.Publish(
                requestId,
                "Validating response",
                item.Passed ? DiagnosticStatus.Succeeded : DiagnosticStatus.Rejected,
                item.Passed ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                item.Passed ? DiagnosticSection.Outputs : DiagnosticSection.WarningsAndErrors,
                "A structured-response validation check completed.",
                [
                    new DiagnosticField("Validation property", item.PropertyName),
                    new DiagnosticField("Required", item.Required.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Expected type", item.ExpectedType),
                    new DiagnosticField("Allowed values", item.AllowedValues ?? string.Empty),
                    new DiagnosticField("Actual type", item.ActualType),
                    new DiagnosticField("Actual value", item.ActualValue, DiagnosticDataClassification.Content),
                    new DiagnosticField("Passed", item.Passed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Validation message", item.Message, DiagnosticDataClassification.Safe),
                ]);
        }

        foreach (var error in errors.Take(100))
        {
            _collector.Publish(
                requestId,
                "Validation error",
                DiagnosticStatus.Rejected,
                DiagnosticSeverity.Error,
                DiagnosticSection.WarningsAndErrors,
                "The structured response failed validation.",
                [new DiagnosticField("Error", error, DiagnosticDataClassification.Safe)]);
        }
    }

    /// <inheritdoc />
    public void Complete(
        string? requestId,
        AiDiagnosticState state,
        bool cancelled,
        TimeSpan elapsed,
        string? error = null) =>
        _collector.Complete(
            requestId,
            cancelled ? DiagnosticStatus.Cancelled : ToCommon(state),
            elapsed,
            $"AI request {state}.",
            state is AiDiagnosticState.Failed ? DiagnosticSeverity.Error :
                state is AiDiagnosticState.Rejected or AiDiagnosticState.Cancelled ? DiagnosticSeverity.Warning :
                DiagnosticSeverity.Information,
            string.IsNullOrWhiteSpace(error)
                ? null
                : [new DiagnosticField("Error", error, DiagnosticDataClassification.Safe)]);

    /// <inheritdoc />
    public void Relate(string? requestId, string? relatedRequestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(relatedRequestId))
        {
            return;
        }

        _collector.Relate(requestId, relatedRequestId);
        _collector.Relate(relatedRequestId, requestId);
    }

    /// <inheritdoc />
    public IReadOnlyList<AiDiagnosticSession> GetRecent() =>
        Array.AsReadOnly(_collector.GetRecent()
            .Where(session => session.Category == DiagnosticCategory.Ai)
            .Take(AiRequestDiagnosticLimits.MaximumRetainedRequests)
            .Select(Project)
            .ToArray());

    /// <inheritdoc />
    public void Clear(string requestId) => _collector.Clear(requestId);

    /// <inheritdoc />
    public void Clear()
    {
        foreach (var session in _collector.GetRecent()
                     .Where(item => item.Category == DiagnosticCategory.Ai))
        {
            _collector.Clear(session.SessionId);
        }
    }

    private void OnSessionChanged(object? sender, DiagnosticSessionChangedEventArgs eventArgs)
    {
        if (eventArgs.Session.Category != DiagnosticCategory.Ai)
        {
            return;
        }

        var projected = Project(eventArgs.Session);
        foreach (EventHandler<AiDiagnosticSessionChangedEventArgs> handler in
                 SessionChanged?.GetInvocationList() ?? [])
        {
            try
            {
                handler(this, new AiDiagnosticSessionChangedEventArgs(projected, eventArgs.IsNew));
            }
            catch
            {
                // Compatibility observers remain isolated from the shared event stream.
            }
        }
    }

    private static AiDiagnosticSession Project(DiagnosticSession session)
    {
        var model = Context(session, "Model");
        var endpoint = Context(session, "Endpoint");
        var retryAttempt = int.TryParse(
            Context(session, "Retry attempt"),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var retry)
            ? retry
            : 1;
        var stages = ExpectedStages
            .Select(name => new AiDiagnosticStage(
                name,
                AiDiagnosticState.Pending,
                session.StartedAtUtc,
                TimeSpan.Zero))
            .ToList();
        foreach (var item in session.Events)
        {
            if (string.Equals(item.Stage, "Completed", StringComparison.Ordinal) &&
                item.Message.StartsWith("AI request ", StringComparison.Ordinal))
            {
                continue;
            }

            var index = stages.FindIndex(stage => string.Equals(stage.Name, item.Stage, StringComparison.Ordinal));
            var elapsed = item.TimestampUtc - session.StartedAtUtc;
            var stage = new AiDiagnosticStage(
                item.Stage,
                ToAi(item.Status),
                item.TimestampUtc,
                elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed,
                item.Severity == DiagnosticSeverity.Error
                    ? EmptyToNull(Field(item, "Error")) ?? item.Message
                    : null);
            if (index >= 0)
            {
                stages[index] = stage;
            }
            else if (!string.Equals(item.Stage, "HTTP transport", StringComparison.Ordinal) &&
                     !string.Equals(item.Stage, "Validation error", StringComparison.Ordinal))
            {
                stages.Add(stage);
            }
        }

        var transport = session.Events.LastOrDefault(item =>
            string.Equals(item.Stage, "HTTP transport", StringComparison.Ordinal));
        var validations = session.Events
            .Where(item =>
                string.Equals(item.Stage, "Validating response", StringComparison.Ordinal) &&
                item.Fields.Any(field =>
                    string.Equals(field.Name, "Validation property", StringComparison.Ordinal)))
            .Select(item => new AiDiagnosticValidation(
                Field(item, "Validation property"),
                bool.TryParse(Field(item, "Required"), out var required) && required,
                Field(item, "Expected type"),
                EmptyToNull(Field(item, "Allowed values")),
                Field(item, "Actual type"),
                Field(item, "Actual value"),
                bool.TryParse(Field(item, "Passed"), out var passed) && passed,
                Field(item, "Validation message")))
            .ToArray();
        var errors = session.Events
            .Where(item => item.Severity == DiagnosticSeverity.Error)
            .Select(item => Field(item, "Error") is { Length: > 0 } value ? value : item.Message)
            .Distinct(StringComparer.Ordinal)
            .Take(100)
            .ToArray();
        return new AiDiagnosticSession(
            session.SessionId,
            Enum.TryParse<AiSuggestionKind>(session.Operation, out var operation) ? operation : AiSuggestionKind.FileRename,
            model,
            endpoint,
            session.StartedAtUtc,
            session.Elapsed,
            ToAi(session.Status),
            int.TryParse(Field(transport, "HTTP status"), out var statusCode) ? statusCode : null,
            session.Status == DiagnosticStatus.Cancelled,
            retryAttempt,
            Field(transport, "Content type"),
            ParseHeaders(Field(transport, "Safe response headers")),
            int.TryParse(Field(transport, "Response size bytes"), out var size) ? size : 0,
            bool.TryParse(Field(transport, "Response complete"), out var complete) && complete,
            bool.TryParse(Field(transport, "Streaming"), out var streaming) && streaming,
            Array.AsReadOnly(stages.Take(100).ToArray()),
            Content(session, "Exact system prompt"),
            Content(session, "Exact user prompt"),
            Content(session, "Serialized Ollama request"),
            Content(session, "Raw HTTP response"),
            Content(session, "Extracted assistant content"),
            Content(session, "Parsed structured response"),
            Array.AsReadOnly(validations),
            Array.AsReadOnly(errors));
    }

    private static DiagnosticStatus ToCommon(AiDiagnosticState state) => state switch
    {
        AiDiagnosticState.Pending or AiDiagnosticState.Active => DiagnosticStatus.Active,
        AiDiagnosticState.Succeeded => DiagnosticStatus.Succeeded,
        AiDiagnosticState.Rejected => DiagnosticStatus.Rejected,
        AiDiagnosticState.Cancelled => DiagnosticStatus.Cancelled,
        _ => DiagnosticStatus.Failed,
    };

    private static AiDiagnosticState ToAi(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Active => AiDiagnosticState.Active,
        DiagnosticStatus.Succeeded or DiagnosticStatus.PartiallySucceeded or DiagnosticStatus.Skipped => AiDiagnosticState.Succeeded,
        DiagnosticStatus.Rejected => AiDiagnosticState.Rejected,
        DiagnosticStatus.Cancelled => AiDiagnosticState.Cancelled,
        _ => AiDiagnosticState.Failed,
    };

    private static string Context(DiagnosticSession session, string name) =>
        session.Context.LastOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal))?.Value ?? string.Empty;

    private static string Content(DiagnosticSession session, string name) =>
        session.Events.SelectMany(item => item.Fields)
            .LastOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal))?.Value ?? string.Empty;

    private static string Field(DiagnosticEvent? item, string name) =>
        item?.Fields.LastOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal))?.Value ?? string.Empty;

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static IReadOnlyDictionary<string, string> ParseHeaders(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(value) ??
                   new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}

/// <summary>Creates validation diagnostics without changing the authoritative parser result.</summary>
public static class AiDiagnosticValidationInspector
{
    /// <summary>Inspects common contract fields and preserves the parsed JSON for diagnostics.</summary>
    public static (string ParsedJson, IReadOnlyList<AiDiagnosticValidation> Checks) Inspect(string? json, string taskId)
    {
        if (string.IsNullOrWhiteSpace(json)) return ("", [Check("$", true, "object", null, "empty", "", false, "Expected a JSON object, but received an empty response.")]);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var pretty = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
            if (root.ValueKind != JsonValueKind.Object)
                return (pretty, [Check("$", true, "object", null, Type(root), root.ToString(), false, $"Expected the root to be an object, but received {Type(root)}.")]);

            var checks = new List<AiDiagnosticValidation>
            {
                StringCheck(root, "taskId", true, taskId),
                StringCheck(root, "status", true, "suggestion, no_suggestion"),
                StringCheck(root, "reason", true, null),
            };
            if (root.TryGetProperty("confidence", out var confidence))
            {
                var valid = confidence.ValueKind is JsonValueKind.Null ||
                    confidence.ValueKind == JsonValueKind.Number && confidence.TryGetDouble(out var number) && number is >= 0 and <= 1;
                checks.Add(Check("confidence", false, "number or null", "0..1", Type(confidence), confidence.ToString(), valid,
                    valid ? "Confidence is valid." : $"Expected `confidence` to be a number from 0 through 1, but received {Type(confidence)} `{confidence}`."));
            }
            return (pretty, checks);
        }
        catch (JsonException exception)
        {
            return ("Parsing failed; original response remains available.", [Check("$", true, "valid JSON object", null, "malformed JSON", json, false, $"JSON parsing failed: {exception.Message}")]);
        }
    }

    private static AiDiagnosticValidation StringCheck(JsonElement root, string name, bool required, string? allowed)
    {
        if (!root.TryGetProperty(name, out var value))
            return Check(name, required, "non-empty string", allowed, "missing", "", false, $"Expected `{name}` to be a non-empty string, but the property was missing.");
        var valid = value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString());
        if (valid && allowed is not null)
            valid = allowed.Split(',', StringSplitOptions.TrimEntries).Contains(value.GetString(), StringComparer.Ordinal);
        var message = valid ? $"{name} is valid." :
            $"Expected `{name}` to be {(allowed is null ? "a non-empty string" : $"one of [{allowed}]")}, but received {Type(value)} `{Bound(value.ToString(), 200)}`.";
        return Check(name, required, "non-empty string", allowed, Type(value), value.ToString(), valid, message);
    }

    private static AiDiagnosticValidation Check(string name, bool required, string expected, string? allowed, string actualType, string actualValue, bool passed, string message) =>
        new(name, required, expected, allowed, actualType, actualValue, passed, message);

    private static string Type(JsonElement element) => element.ValueKind.ToString().ToLowerInvariant();
    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
