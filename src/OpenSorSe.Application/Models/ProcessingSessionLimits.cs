namespace OpenSorSe.Application.Models;

/// <summary>Defines process-memory bounds for non-persistent processing history.</summary>
public static class ProcessingSessionLimits
{
    /// <summary>
    /// Gets the maximum number of recent non-running session snapshots retained
    /// for the process lifetime.
    /// </summary>
    public const int MaximumRetainedSessions = 256;
}
