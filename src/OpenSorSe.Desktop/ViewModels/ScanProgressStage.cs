namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Describes the presentation lifecycle of a scan-progress view.
/// </summary>
public enum ScanProgressStage
{
    /// <summary>
    /// No scan progress is currently presented.
    /// </summary>
    Idle,

    /// <summary>
    /// A scan is running and may publish progress.
    /// </summary>
    Scanning,

    /// <summary>
    /// A scan completed normally.
    /// </summary>
    Completed,

    /// <summary>
    /// A scan stopped after cancellation.
    /// </summary>
    Cancelled,

    /// <summary>
    /// A scan could not start or complete safely.
    /// </summary>
    Failed,
}
