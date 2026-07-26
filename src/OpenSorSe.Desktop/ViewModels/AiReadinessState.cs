namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Describes the concise, user-facing readiness of the optional local AI service.
/// </summary>
public enum AiReadinessState
{
    /// <summary>AI is disabled or required configuration is missing.</summary>
    NotConfigured,

    /// <summary>Configuration is present but has not been checked in this session.</summary>
    NotChecked,

    /// <summary>The configured local server could not be reached.</summary>
    ServerUnavailable,

    /// <summary>The local server responded but the configured model has not been validated.</summary>
    ServerAvailable,

    /// <summary>The exact configured model was not found.</summary>
    ModelMissing,

    /// <summary>The local server and exact configured model are ready.</summary>
    Ready,

    /// <summary>An explicit local-AI operation is running.</summary>
    Running,

    /// <summary>The latest operation failed safely and may be retried.</summary>
    Failed,

    /// <summary>The latest operation was cancelled and may be retried.</summary>
    Cancelled,
}
