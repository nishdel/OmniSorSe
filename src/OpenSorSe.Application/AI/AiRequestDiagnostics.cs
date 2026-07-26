using System.Text.RegularExpressions;

namespace OpenSorSe.Application.AI;

/// <summary>Defines session-only raw AI diagnostic retention bounds.</summary>
public static class AiRequestDiagnosticLimits
{
    /// <summary>Maximum number of completed request records retained in memory.</summary>
    public const int MaximumRetainedRequests = 20;
}

/// <summary>Records one stage transition for an explicit AI request.</summary>
public sealed record AiRequestStageEntry(AiRequestStage Stage, DateTimeOffset TimestampUtc, string Message);

/// <summary>
/// Contains one bounded, session-only diagnostic record for an explicit AI request.
/// </summary>
public sealed record AiRequestDiagnostic(
    string RequestId,
    DateTimeOffset RequestedAtUtc,
    AiSuggestionKind Capability,
    string NormalizedEndpoint,
    string Model,
    int EffectiveTimeoutSeconds,
    IReadOnlyList<AiRequestStageEntry> Stages,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    TimeSpan Elapsed,
    string Outcome,
    int? HttpStatusCode,
    AiProviderFailureKind FailureKind,
    int PromptCharacterCount,
    int PromptByteCount,
    int ResponseCharacterCount,
    int ResponseByteCount,
    string ValidationOutcome,
    IReadOnlyList<string> ValidationIssues,
    int TotalInputCount,
    int IncludedInputCount,
    int OmittedInputCount,
    string Prompt,
    string Response);

/// <summary>Provides bounded, opt-in process-session AI request inspection.</summary>
public interface IAiRequestDiagnosticsStore
{
    /// <summary>Gets whether raw capture is currently enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>Applies the active setting and clears raw history when disabled.</summary>
    void SetEnabled(bool enabled, bool showUnredactedContent = false);

    /// <summary>Records one already bounded request when capture is enabled.</summary>
    void Record(AiRequestDiagnostic diagnostic);

    /// <summary>Gets newest-first immutable request records.</summary>
    IReadOnlyList<AiRequestDiagnostic> GetRecent();

    /// <summary>Clears all process-session raw records.</summary>
    void Clear();
}

/// <summary>
/// Retains redacted AI request data only in a bounded process-session ring buffer.
/// </summary>
public sealed partial class AiRequestDiagnosticsStore : IAiRequestDiagnosticsStore
{
    private readonly Queue<AiRequestDiagnostic> _records = new();
    private readonly object _syncRoot = new();
    private bool _isEnabled;
    private bool _showUnredactedContent;

    /// <inheritdoc />
    public bool IsEnabled
    {
        get
        {
            lock (_syncRoot)
            {
                return _isEnabled;
            }
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled, bool showUnredactedContent = false)
    {
        lock (_syncRoot)
        {
            _isEnabled = enabled;
            _showUnredactedContent = enabled && showUnredactedContent;
            if (!enabled)
            {
                _records.Clear();
            }
        }
    }

    /// <inheritdoc />
    public void Record(AiRequestDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        lock (_syncRoot)
        {
            if (!_isEnabled)
            {
                return;
            }

            _records.Enqueue(Redact(diagnostic, _showUnredactedContent));
            while (_records.Count > AiRequestDiagnosticLimits.MaximumRetainedRequests)
            {
                _records.Dequeue();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<AiRequestDiagnostic> GetRecent()
    {
        lock (_syncRoot)
        {
            return Array.AsReadOnly(_records.Reverse().ToArray());
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_syncRoot)
        {
            _records.Clear();
        }
    }

    /// <summary>Redacts common credential-like values before any raw diagnostic is retained.</summary>
    public static string RedactSensitiveText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = AuthorizationRegex().Replace(value, "$1[REDACTED]");
        return SecretPropertyRegex().Replace(redacted, "$1[REDACTED]$3");
    }

    private static AiRequestDiagnostic Redact(AiRequestDiagnostic diagnostic, bool showUnredactedContent) => diagnostic with
    {
        NormalizedEndpoint = RedactSensitiveText(diagnostic.NormalizedEndpoint),
        Prompt = RedactDiagnosticContent(diagnostic.Prompt, showUnredactedContent),
        Response = RedactDiagnosticContent(diagnostic.Response, showUnredactedContent),
        ValidationIssues = Array.AsReadOnly(diagnostic.ValidationIssues
            .Select(RedactSensitiveText)
            .Take(50)
            .ToArray()),
    };

    private static string RedactDiagnosticContent(string? value, bool showUnredactedContent)
    {
        var credentialSafe = RedactSensitiveText(value);
        return showUnredactedContent
            ? credentialSafe
            : DiagnosticContentRegex().Replace(credentialSafe, "$1[REDACTED]$3");
    }

    [GeneratedRegex("(?i)(\\\"?authorization\\\"?\\s*[:=]\\s*\\\"?)(?:bearer\\s+)?[^\\s,;\\\"]+")]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex("(?i)(\\\"?(?:api[_-]?key|access[_-]?token|password|secret)\\\"?\\s*[:=]\\s*\\\"?)([^\\\",}\\s]+)(\\\"?)")]
    private static partial Regex SecretPropertyRegex();

    [GeneratedRegex("(\\\"(?:fileName|currentFileName|suggestedFileName|currentStem|suggestedStem|displayFileName|relativePath|path|text|metadata|documentType|title|issuer|reason)\\\"\\s*:\\s*\\\")([^\\\"]*)(\\\")", RegexOptions.IgnoreCase)]
    private static partial Regex DiagnosticContentRegex();
}
