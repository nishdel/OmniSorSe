using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Core.Diagnostics;

/// <summary>Retains immutable advanced-diagnostic sessions only for the current process.</summary>
public interface IDiagnosticsStore
{
    /// <summary>Occurs after one retained session is created or updated.</summary>
    event EventHandler<DiagnosticSessionChangedEventArgs>? SessionChanged;

    /// <summary>Occurs after retained sessions are explicitly cleared.</summary>
    event EventHandler? SessionsCleared;

    /// <summary>Gets retained sessions in reverse chronological order.</summary>
    IReadOnlyList<DiagnosticSession> GetRecent();

    /// <summary>Gets one retained session by identity.</summary>
    DiagnosticSession? Get(string sessionId);

    /// <summary>Clears one retained session and releases its content.</summary>
    void Clear(string sessionId);

    /// <summary>Clears every retained session and releases detailed content.</summary>
    void ClearAll();
}

/// <summary>Publishes typed, UI-independent events into advanced-diagnostic sessions.</summary>
public interface IDiagnosticsEventSink
{
    /// <summary>Gets whether the category is currently authorized to collect detailed data.</summary>
    bool IsCategoryEnabled(DiagnosticCategory category);

    /// <summary>Begins one detailed session, or returns null when collection is disabled.</summary>
    string? BeginSession(
        DiagnosticCategory category,
        string operation,
        IReadOnlyList<DiagnosticField>? context = null,
        IReadOnlyCollection<string>? relatedSessionIds = null);

    /// <summary>Publishes one typed event into an existing session.</summary>
    void Publish(
        string? sessionId,
        string stage,
        DiagnosticStatus status,
        DiagnosticSeverity severity,
        DiagnosticSection section,
        string message,
        IReadOnlyList<DiagnosticField>? fields = null);

    /// <summary>Adds bounded parent or downstream correlation identities.</summary>
    void Relate(string? sessionId, params string?[] relatedSessionIds);

    /// <summary>Marks a session terminal and publishes its final summary.</summary>
    void Complete(
        string? sessionId,
        DiagnosticStatus status,
        TimeSpan elapsed,
        string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Information,
        IReadOnlyList<DiagnosticField>? fields = null);
}

/// <summary>Owns active diagnostics settings, collection, retention, and observation.</summary>
public interface IDiagnosticsCollector : IDiagnosticsStore, IDiagnosticsEventSink
{
    /// <summary>Gets whether the master detailed-diagnostics switch is enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>Gets whether explicitly sensitive content may be retained unredacted.</summary>
    bool ShowUnredactedContent { get; }

    /// <summary>Applies current settings and clears history when the master switch is disabled.</summary>
    void Configure(DiagnosticsSettings settings);
}

/// <summary>Redacts classified values before any detailed content is retained.</summary>
public interface IDiagnosticsRedactor
{
    /// <summary>Returns a bounded value safe for process-memory retention.</summary>
    DiagnosticField Redact(DiagnosticCategory category, DiagnosticField field, bool showUnredactedContent);
}

/// <summary>Builds explicit manual exports without persisting automatically.</summary>
public interface IDiagnosticsExportService
{
    /// <summary>Serializes one selected session as JSON.</summary>
    string ExportJson(DiagnosticSession session);

    /// <summary>Formats one selected session as plain text.</summary>
    string ExportText(DiagnosticSession session);

    /// <summary>Serializes all supplied retained sessions as JSON.</summary>
    string ExportAllJson(IReadOnlyList<DiagnosticSession> sessions);

    /// <summary>Formats all supplied retained sessions as plain text.</summary>
    string ExportAllText(IReadOnlyList<DiagnosticSession> sessions);
}

/// <summary>Creates a failure-isolating event-sink facade for feature services.</summary>
public static class DiagnosticsIsolation
{
    /// <summary>Returns a facade whose failures never escape into a feature operation.</summary>
    /// <param name="sink">The optional process-local diagnostics sink.</param>
    /// <returns>A protected sink, or null when no sink was supplied.</returns>
    public static IDiagnosticsEventSink? Protect(IDiagnosticsEventSink? sink) =>
        sink is null ? null : new ProtectedSink(sink);

    private sealed class ProtectedSink : IDiagnosticsEventSink
    {
        private readonly IDiagnosticsEventSink _inner;

        public ProtectedSink(IDiagnosticsEventSink inner) => _inner = inner;

        public bool IsCategoryEnabled(DiagnosticCategory category)
        {
            try
            {
                return _inner.IsCategoryEnabled(category);
            }
            catch
            {
                return false;
            }
        }

        public string? BeginSession(
            DiagnosticCategory category,
            string operation,
            IReadOnlyList<DiagnosticField>? context = null,
            IReadOnlyCollection<string>? relatedSessionIds = null)
        {
            try
            {
                return _inner.BeginSession(category, operation, context, relatedSessionIds);
            }
            catch
            {
                return null;
            }
        }

        public void Publish(
            string? sessionId,
            string stage,
            DiagnosticStatus status,
            DiagnosticSeverity severity,
            DiagnosticSection section,
            string message,
            IReadOnlyList<DiagnosticField>? fields = null) =>
            Try(() => _inner.Publish(sessionId, stage, status, severity, section, message, fields));

        public void Relate(string? sessionId, params string?[] relatedSessionIds) =>
            Try(() => _inner.Relate(sessionId, relatedSessionIds));

        public void Complete(
            string? sessionId,
            DiagnosticStatus status,
            TimeSpan elapsed,
            string message,
            DiagnosticSeverity severity = DiagnosticSeverity.Information,
            IReadOnlyList<DiagnosticField>? fields = null) =>
            Try(() => _inner.Complete(sessionId, status, elapsed, message, severity, fields));

        private static void Try(Action action)
        {
            try
            {
                action();
            }
            catch
            {
                // Detailed diagnostics are best effort and never alter feature behavior.
            }
        }
    }
}
