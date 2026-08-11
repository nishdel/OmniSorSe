using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Presents passive scanner progress and emits a cancellation request without controlling a scan.
/// </summary>
public sealed class ScanProgressViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan ElapsedRefreshInterval = TimeSpan.FromMilliseconds(100);
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly TimeProvider _timeProvider;
    private string? _currentFolder;
    private TimeSpan _elapsed;
    private long _filesFound;
    private long _foldersScanned;
    private string? _stageText;
    private ScanProgressStage _stage = ScanProgressStage.Idle;
    private ITimer? _elapsedTimer;
    private long? _startedTimestamp;
    private bool _isDisposed;

    /// <summary>Initializes scan progress with the system monotonic clock.</summary>
    public ScanProgressViewModel()
        : this(TimeProvider.System)
    {
    }

    internal ScanProgressViewModel(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _synchronizationContext = SynchronizationContext.Current;
    }

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
        private set
        {
            if (SetProperty(ref _elapsed, value))
            {
                OnPropertyChanged(nameof(ElapsedText));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>Gets human-readable elapsed time without implying unsupported precision.</summary>
    public string ElapsedText => FormatDuration(Elapsed);

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
        ScanProgressStage.Scanning => $"{_stageText ?? "Scanning"} · {ElapsedText} elapsed",
        ScanProgressStage.Completed => _startedTimestamp is null ? "Scan completed." : $"Completed in {ElapsedText}",
        ScanProgressStage.Cancelled => _startedTimestamp is null ? "Scan cancelled." : $"Cancelled after {ElapsedText}",
        ScanProgressStage.Failed => _startedTimestamp is null
            ? _stageText ?? "Scan failed."
            : $"{_stageText ?? "Scan failed."} · failed after {ElapsedText}",
        _ => throw new InvalidOperationException("The scan progress stage is unsupported."),
    };

    /// <summary>
    /// Resets the presentation for a newly started scan.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        StopTimer();
        CurrentFolder = null;
        Elapsed = TimeSpan.Zero;
        FilesFound = 0;
        FoldersScanned = 0;
        _stageText = null;
        _startedTimestamp = _timeProvider.GetTimestamp();
        Stage = ScanProgressStage.Scanning;
        _elapsedTimer = _timeProvider.CreateTimer(
            static state => ((ScanProgressViewModel)state!).DispatchElapsedRefresh(),
            this,
            ElapsedRefreshInterval,
            ElapsedRefreshInterval);
    }

    /// <summary>
    /// Applies a scanner progress snapshot while a scan is active.
    /// </summary>
    /// <param name="progress">The scanner snapshot to present.</param>
    public void ApplyProgress(ScanProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        CurrentFolder = progress.CurrentPath;
        if (progress.Elapsed > Elapsed)
        {
            Elapsed = progress.Elapsed;
        }

        RefreshElapsed();
        FilesFound = progress.Statistics.FilesDiscovered;
        FoldersScanned = progress.Statistics.DirectoriesDiscovered;
    }

    /// <summary>
    /// Marks the presentation as complete for a terminal scanner status.
    /// </summary>
    /// <param name="status">The scanner's terminal status.</param>
    public void Complete(ScanStatus status)
    {
        if (status is not (ScanStatus.Completed or ScanStatus.Cancelled))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "The scan status is unsupported.");
        }

        if (Stage == ScanProgressStage.Scanning)
        {
            FreezeElapsed();
        }
        else
        {
            StopTimer();
            _startedTimestamp = null;
            Elapsed = TimeSpan.Zero;
        }
        _stageText = null;
        Stage = status switch
        {
            ScanStatus.Completed => ScanProgressStage.Completed,
            ScanStatus.Cancelled => ScanProgressStage.Cancelled,
            _ => throw new InvalidOperationException("The validated scan status was not terminal."),
        };
    }

    /// <summary>
    /// Marks the presentation as failed without manufacturing a scanner terminal status.
    /// </summary>
    /// <param name="message">A concise, user-safe failure description.</param>
    public void Fail(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (Stage == ScanProgressStage.Scanning)
        {
            FreezeElapsed();
        }
        else
        {
            StopTimer();
            _startedTimestamp = null;
            Elapsed = TimeSpan.Zero;
        }
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

    /// <summary>Stops the live timer without changing the frozen terminal duration.</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        StopTimer();
        _isDisposed = true;
    }

    internal void RefreshElapsed()
    {
        if (Stage != ScanProgressStage.Scanning || _startedTimestamp is not { } started)
        {
            return;
        }

        var elapsed = _timeProvider.GetElapsedTime(started, _timeProvider.GetTimestamp());
        var safeElapsed = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        if (safeElapsed > Elapsed)
        {
            Elapsed = safeElapsed;
        }
    }

    private void FreezeElapsed()
    {
        RefreshElapsed();
        StopTimer();
    }

    private void StopTimer()
    {
        Interlocked.Exchange(ref _elapsedTimer, null)?.Dispose();
    }

    private void DispatchElapsedRefresh()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_synchronizationContext is null)
        {
            RefreshElapsed();
            return;
        }

        _synchronizationContext.Post(
            static state => ((ScanProgressViewModel)state!).RefreshElapsed(),
            this);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var safe = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return safe.TotalMinutes < 1
            ? $"{safe.TotalSeconds:0.0} s"
            : $"{(int)safe.TotalMinutes}:{safe.Seconds:00}";
    }
}
