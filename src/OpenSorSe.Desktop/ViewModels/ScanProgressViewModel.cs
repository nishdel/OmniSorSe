using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Presents passive scanner progress and emits a cancellation request without controlling a scan.
/// </summary>
public sealed class ScanProgressViewModel : ViewModelBase
{
    private string? _currentFolder;
    private TimeSpan _elapsed;
    private long _filesFound;
    private long _foldersScanned;
    private string? _stageText;
    private ScanProgressStage _stage = ScanProgressStage.Idle;

    /// <summary>
    /// Occurs when the user requests that an active scan be cancelled.
    /// </summary>
    public event EventHandler? CancelRequested;

    /// <summary>
    /// Gets the folder or entry most recently reported by the scanner.
    /// </summary>
    public string? CurrentFolder
    {
        get => _currentFolder;
        private set => SetProperty(ref _currentFolder, value);
    }

    /// <summary>
    /// Gets the elapsed scan duration last reported by the scanner.
    /// </summary>
    public TimeSpan Elapsed
    {
        get => _elapsed;
        private set => SetProperty(ref _elapsed, value);
    }

    /// <summary>
    /// Gets the number of discovered files last reported by the scanner.
    /// </summary>
    public long FilesFound
    {
        get => _filesFound;
        private set => SetProperty(ref _filesFound, value);
    }

    /// <summary>
    /// Gets the number of scanned directories last reported by the scanner.
    /// </summary>
    public long FoldersScanned
    {
        get => _foldersScanned;
        private set => SetProperty(ref _foldersScanned, value);
    }

    /// <summary>
    /// Gets the current scan presentation stage.
    /// </summary>
    public ScanProgressStage Stage
    {
        get => _stage;
        private set
        {
            if (SetProperty(ref _stage, value))
            {
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>
    /// Gets whether the view should present an active indeterminate operation.
    /// </summary>
    public bool IsActive => Stage == ScanProgressStage.Scanning;

    /// <summary>
    /// Gets user-safe stage text.
    /// </summary>
    public string StatusText => Stage switch
    {
        ScanProgressStage.Idle => _stageText ?? "Ready",
        ScanProgressStage.Scanning => _stageText ?? "Scanning...",
        ScanProgressStage.Completed => _stageText ?? "Scan completed.",
        ScanProgressStage.Cancelled => _stageText ?? "Scan cancelled.",
        ScanProgressStage.Failed => _stageText ?? "Scan failed.",
        _ => throw new InvalidOperationException("The scan progress stage is unsupported."),
    };

    /// <summary>
    /// Resets the presentation for a newly started scan.
    /// </summary>
    public void Start()
    {
        CurrentFolder = null;
        Elapsed = TimeSpan.Zero;
        FilesFound = 0;
        FoldersScanned = 0;
        _stageText = null;
        Stage = ScanProgressStage.Scanning;
    }

    /// <summary>
    /// Applies a scanner progress snapshot while a scan is active.
    /// </summary>
    /// <param name="progress">The scanner snapshot to present.</param>
    public void ApplyProgress(ScanProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        CurrentFolder = progress.CurrentPath;
        Elapsed = progress.Elapsed;
        FilesFound = progress.Statistics.FilesDiscovered;
        FoldersScanned = progress.Statistics.DirectoriesDiscovered;
    }

    /// <summary>
    /// Marks the presentation as complete for a terminal scanner status.
    /// </summary>
    /// <param name="status">The scanner's terminal status.</param>
    public void Complete(ScanStatus status)
    {
        _stageText = null;
        Stage = status switch
        {
            ScanStatus.Completed => ScanProgressStage.Completed,
            ScanStatus.Cancelled => ScanProgressStage.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), "The scan status is unsupported."),
        };
    }

    /// <summary>
    /// Marks the presentation as failed without manufacturing a scanner terminal status.
    /// </summary>
    /// <param name="message">A concise, user-safe failure description.</param>
    public void Fail(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _stageText = message;
        Stage = ScanProgressStage.Failed;
    }

    /// <summary>
    /// Emits a cancellation request only while a scan is active.
    /// </summary>
    public void RequestCancellation()
    {
        if (IsActive)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Updates the user-facing stage text while a processing operation is active.
    /// </summary>
    /// <param name="stageText">The user-safe description of the current processing stage.</param>
    public void SetStageText(string stageText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageText);
        _stageText = stageText;
        OnPropertyChanged(nameof(StatusText));
    }
}
