using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Scanner;

/// <summary>
/// Performs a read-only recursive traversal of selected filesystem directories.
/// </summary>
public sealed class FileScanner : IFileScanner
{
    private const string LoggerCategory = "Scanner";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IErrorHandler _errorHandler;
    private readonly ILogger _logger;
    private readonly IDiagnosticsEventSink? _diagnostics;

    /// <summary>
    /// Initializes a file scanner that records diagnostics through shared Core infrastructure.
    /// </summary>
    /// <param name="loggingService">The centralized logging service.</param>
    /// <param name="errorHandler">The handler used for unexpected operation-level failures.</param>
    /// <param name="diagnostics">The optional failure-isolated detailed diagnostics sink.</param>
    public FileScanner(
        ILoggingService loggingService,
        IErrorHandler errorHandler,
        IDiagnosticsEventSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(loggingService);
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _logger = loggingService.CreateLogger(LoggerCategory);
        _diagnostics = DiagnosticsIsolation.Protect(diagnostics);
    }

    /// <inheritdoc />
    public Task<ScanResult> ScanAsync(
        ScanRequest request,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedRequest = NormalizeRequest(request);
        return Task.Run(
            () => Scan(normalizedRequest, progress, cancellationToken),
            CancellationToken.None);
    }

    private ScanResult Scan(
        NormalizedScanRequest request,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = new List<FileEntry>();
        var directories = new List<DirectoryEntry>();
        var issues = new List<ScanIssue>();
        var discoveredPaths = new HashSet<string>(PathComparer);
        var stopwatch = Stopwatch.StartNew();
        var lastProgressReport = TimeSpan.Zero;
        var diagnosticSessionId = _diagnostics?.BeginSession(
            DiagnosticCategory.Scanning,
            "Scan selected folders",
            request.RootDirectories
                .Select((root, index) => new DiagnosticField(
                    $"Selected root {index + 1}",
                    root,
                    DiagnosticDataClassification.Path))
                .Concat(
                [
                    new DiagnosticField("Progress interval milliseconds", request.Options.ProgressReportInterval.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Symbolic-link policy", "Skip symbolic links, junctions, and other reparse points"),
                    new DiagnosticField("Extension policy", "Accept every ordinary file; downstream feature support is reported separately"),
                ])
                .ToArray());
        var diagnostic = new ScanDiagnosticPublisher(_diagnostics, diagnosticSessionId);

        try
        {
            _logger.LogInformation("Started scanning {RootCount} root directories.", request.RootDirectories.Count);
            diagnostic.Publish(
                "Scan started",
                DiagnosticStatus.Active,
                DiagnosticSeverity.Information,
                DiagnosticSection.Overview,
                "Filesystem discovery started.");
            ReportProgress(null, progress, files.Count, directories.Count, issues.Count, stopwatch.Elapsed, ref lastProgressReport, request.Options, true);

            foreach (var rootDirectory in request.RootDirectories)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                ScanRoot(
                    rootDirectory,
                    discoveredPaths,
                    files,
                    directories,
                    issues,
                    stopwatch,
                    progress,
                    ref lastProgressReport,
                    request.Options,
                    cancellationToken,
                    diagnostic);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }

            var status = cancellationToken.IsCancellationRequested ? ScanStatus.Cancelled : ScanStatus.Completed;
            var result = CreateResult(status, files, directories, issues, stopwatch.Elapsed) with
            {
                DiagnosticSessionId = diagnosticSessionId,
            };
            ReportProgress(null, progress, result.Statistics, stopwatch.Elapsed, ref lastProgressReport, request.Options, true);
            _logger.LogInformation(
                "Finished scanning with status {Status}. Discovered {FileCount} files and {DirectoryCount} directories.",
                result.Status,
                result.Statistics.FilesDiscovered,
                result.Statistics.DirectoriesDiscovered);
            diagnostic.Complete(result, stopwatch.Elapsed);
            return result;
        }
        catch (Exception exception)
        {
            diagnostic.Fail(exception, stopwatch.Elapsed);
            _errorHandler.Report(new ApplicationError(
                LoggerCategory,
                "The scan could not be completed due to an unexpected error.",
                ApplicationErrorSeverity.Error,
                exception));
            throw;
        }
    }

    private void ScanRoot(
        string rootDirectory,
        HashSet<string> discoveredPaths,
        List<FileEntry> files,
        List<DirectoryEntry> directories,
        List<ScanIssue> issues,
        Stopwatch stopwatch,
        IProgress<ScanProgress>? progress,
        ref TimeSpan lastProgressReport,
        ScanOptions options,
        CancellationToken cancellationToken,
        ScanDiagnosticPublisher diagnostic)
    {
        if (!TryGetAttributes(rootDirectory, true, issues, diagnostic, out var attributes))
        {
            return;
        }

        if (!attributes.HasFlag(FileAttributes.Directory))
        {
            RecordIssue(
                rootDirectory,
                ScanIssueKind.RootDirectoryUnavailable,
                "The requested root is not a directory.",
                issues,
                diagnostic: diagnostic);
            return;
        }

        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            RecordIssue(rootDirectory, ScanIssueKind.SymbolicLinkSkipped, "The symbolic link was skipped.", issues, diagnostic: diagnostic);
            return;
        }

        if (!discoveredPaths.Add(rootDirectory))
        {
            return;
        }

        var pendingDirectories = new Stack<string>();
        directories.Add(new DirectoryEntry(rootDirectory));
        diagnostic.RecordDirectory(rootDirectory);
        ReportProgress(rootDirectory, progress, files.Count, directories.Count, issues.Count, stopwatch.Elapsed, ref lastProgressReport, options, false);
        pendingDirectories.Push(rootDirectory);
        while (pendingDirectories.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var currentDirectory = pendingDirectories.Pop();
            try
            {
                foreach (var entryPath in Directory.EnumerateFileSystemEntries(currentDirectory))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    ProcessEntry(
                        entryPath,
                        discoveredPaths,
                        pendingDirectories,
                        files,
                        directories,
                        issues,
                        stopwatch,
                        progress,
                        ref lastProgressReport,
                        options,
                        diagnostic);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                RecordIssue(currentDirectory, ScanIssueKind.AccessDenied, "Access to the directory was denied.", issues, exception, diagnostic);
            }
            catch (IOException exception)
            {
                RecordIssue(currentDirectory, ScanIssueKind.DirectoryUnavailable, "The directory could not be enumerated.", issues, exception, diagnostic);
            }
        }
    }

    private void ProcessEntry(
        string entryPath,
        HashSet<string> discoveredPaths,
        Stack<string> pendingDirectories,
        List<FileEntry> files,
        List<DirectoryEntry> directories,
        List<ScanIssue> issues,
        Stopwatch stopwatch,
        IProgress<ScanProgress>? progress,
        ref TimeSpan lastProgressReport,
        ScanOptions options,
        ScanDiagnosticPublisher diagnostic)
    {
        if (!TryGetAttributes(entryPath, false, issues, diagnostic, out var attributes))
        {
            return;
        }

        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            RecordIssue(entryPath, ScanIssueKind.SymbolicLinkSkipped, "The symbolic link was skipped.", issues, diagnostic: diagnostic);
            return;
        }

        var normalizedEntryPath = NormalizePath(entryPath);
        if (!discoveredPaths.Add(normalizedEntryPath))
        {
            return;
        }

        if (attributes.HasFlag(FileAttributes.Directory))
        {
            directories.Add(new DirectoryEntry(normalizedEntryPath));
            pendingDirectories.Push(normalizedEntryPath);
            diagnostic.RecordDirectory(normalizedEntryPath);
        }
        else
        {
            files.Add(new FileEntry(normalizedEntryPath)
            {
                ScanDiagnosticSessionId = diagnostic.SessionId,
            });
            diagnostic.RecordFile(normalizedEntryPath);
        }

        ReportProgress(
            normalizedEntryPath,
            progress,
            files.Count,
            directories.Count,
            issues.Count,
            stopwatch.Elapsed,
            ref lastProgressReport,
            options,
            false);
    }

    private bool TryGetAttributes(
        string path,
        bool isRoot,
        List<ScanIssue> issues,
        ScanDiagnosticPublisher diagnostic,
        out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            attributes = default;
            RecordIssue(
                path,
                ScanIssueKind.AccessDenied,
                "Access to the filesystem location was denied.",
                issues,
                exception,
                diagnostic);
            return false;
        }
        catch (IOException exception)
        {
            attributes = default;
            RecordIssue(
                path,
                isRoot ? ScanIssueKind.RootDirectoryUnavailable : ScanIssueKind.EntryUnavailable,
                "The filesystem location could not be inspected.",
                issues,
                exception,
                diagnostic);
            return false;
        }
    }

    private void RecordIssue(
        string path,
        ScanIssueKind kind,
        string message,
        List<ScanIssue> issues,
        Exception? exception = null,
        ScanDiagnosticPublisher? diagnostic = null)
    {
        issues.Add(new ScanIssue(path, kind, message));
        _logger.LogWarning(
            "Scanner issue {IssueKind}. Error category: {ErrorCategory}.",
            kind,
            exception?.GetType().Name ?? "None");
        diagnostic?.RecordIssue(path, kind, message);
    }

    private static NormalizedScanRequest NormalizeRequest(ScanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RootDirectories);
        ArgumentNullException.ThrowIfNull(request.Options);
        if (request.RootDirectories.Count == 0)
        {
            throw new ArgumentException("At least one root directory is required.", nameof(request));
        }

        if (request.Options.ProgressReportInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The progress report interval cannot be negative.");
        }

        var rootDirectories = new HashSet<string>(PathComparer);
        foreach (var rootDirectory in request.RootDirectories)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
            rootDirectories.Add(NormalizePath(rootDirectory));
        }

        return new NormalizedScanRequest(rootDirectories.ToArray(), request.Options);
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static ScanResult CreateResult(
        ScanStatus status,
        List<FileEntry> files,
        List<DirectoryEntry> directories,
        List<ScanIssue> issues,
        TimeSpan elapsed) => new(
        files.ToArray(),
        directories.ToArray(),
        new ScanStatistics(files.Count, directories.Count, issues.Count),
        issues.ToArray(),
        status,
        elapsed);

    private static void ReportProgress(
        string? currentPath,
        IProgress<ScanProgress>? progress,
        int fileCount,
        int directoryCount,
        int issueCount,
        TimeSpan elapsed,
        ref TimeSpan lastProgressReport,
        ScanOptions options,
        bool force)
    {
        ReportProgress(
            currentPath,
            progress,
            new ScanStatistics(fileCount, directoryCount, issueCount),
            elapsed,
            ref lastProgressReport,
            options,
            force);
    }

    private static void ReportProgress(
        string? currentPath,
        IProgress<ScanProgress>? progress,
        ScanStatistics statistics,
        TimeSpan elapsed,
        ref TimeSpan lastProgressReport,
        ScanOptions options,
        bool force)
    {
        if (progress is null || (!force && elapsed - lastProgressReport < options.ProgressReportInterval))
        {
            return;
        }

        progress.Report(new ScanProgress(currentPath, statistics, elapsed));
        lastProgressReport = elapsed;
    }

    private sealed record NormalizedScanRequest(
        IReadOnlyList<string> RootDirectories,
        ScanOptions Options);

    private sealed class ScanDiagnosticPublisher
    {
        private static readonly HashSet<string> ExtractionExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".xlsx", ".png", ".jpg", ".jpeg", ".tif", ".tiff",
        };
        private readonly IDiagnosticsEventSink? _sink;
        private int _detailedEntryCount;
        private int _omittedEntryCount;
        private int _fileCount;
        private int _directoryCount;
        private int _skippedCount;
        private int _unsupportedExtractionCount;

        public ScanDiagnosticPublisher(IDiagnosticsEventSink? sink, string? sessionId)
        {
            _sink = sink;
            SessionId = sessionId;
        }

        public string? SessionId { get; }

        public void RecordFile(string path)
        {
            _fileCount++;
            var extension = Path.GetExtension(path);
            var supported = ExtractionExtensions.Contains(extension);
            if (!supported)
            {
                _unsupportedExtractionCount++;
            }

            RecordEntry(
                "File accepted",
                DiagnosticStatus.Succeeded,
                supported ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                supported
                    ? "The ordinary file was accepted for downstream processing."
                    : "The ordinary file was accepted by the scanner; its extension has no format-specific text-extraction strategy.",
                [
                    new DiagnosticField("File", path, DiagnosticDataClassification.Path),
                    new DiagnosticField("Extension", extension),
                    new DiagnosticField("Scanner decision", "Accepted"),
                    new DiagnosticField("Downstream extraction support", supported ? "Supported" : "Unsupported extension"),
                ]);
            PublishProgress();
        }

        public void RecordDirectory(string path)
        {
            _directoryCount++;
            RecordEntry(
                "Directory discovered",
                DiagnosticStatus.Succeeded,
                DiagnosticSeverity.Information,
                "The directory was accepted for traversal.",
                [
                    new DiagnosticField("Directory", path, DiagnosticDataClassification.Path),
                    new DiagnosticField("Scanner decision", "Accepted for traversal"),
                ]);
            PublishProgress();
        }

        public void RecordIssue(string path, ScanIssueKind kind, string message)
        {
            _skippedCount++;
            RecordEntry(
                "Entry skipped",
                DiagnosticStatus.Skipped,
                DiagnosticSeverity.Warning,
                message,
                [
                    new DiagnosticField("Filesystem entry", path, DiagnosticDataClassification.Path),
                    new DiagnosticField("Skip reason", kind.ToString()),
                    new DiagnosticField("Scanner decision", "Skipped"),
                ]);
        }

        public void Publish(
            string stage,
            DiagnosticStatus status,
            DiagnosticSeverity severity,
            DiagnosticSection section,
            string message,
            IReadOnlyList<DiagnosticField>? fields = null) =>
            _sink?.Publish(SessionId, stage, status, severity, section, message, fields);

        public void Complete(ScanResult result, TimeSpan elapsed)
        {
            if (_omittedEntryCount > 0)
            {
                Publish(
                    "Detailed entry sampling",
                    DiagnosticStatus.PartiallySucceeded,
                    DiagnosticSeverity.Warning,
                    DiagnosticSection.WarningsAndErrors,
                    "Detailed scan-entry retention reached its bound; remaining entries are represented by aggregate counts.",
                    [
                        new DiagnosticField("Detailed entry limit", DiagnosticLimits.MaximumScanEntryRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new DiagnosticField("Omitted detailed entries", _omittedEntryCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ]);
            }

            var completedStatus = result.Status == ScanStatus.Cancelled
                ? DiagnosticStatus.Cancelled
                : result.Issues.Count > 0
                    ? DiagnosticStatus.PartiallySucceeded
                    : DiagnosticStatus.Succeeded;
            _sink?.Complete(
                SessionId,
                completedStatus,
                elapsed,
                result.Status == ScanStatus.Cancelled
                    ? "Scan cancelled with bounded partial discovery."
                    : result.Issues.Count > 0
                        ? "Scan completed with skipped or inaccessible entries."
                        : "Scan completed.",
                completedStatus == DiagnosticStatus.Succeeded
                    ? DiagnosticSeverity.Information
                    : DiagnosticSeverity.Warning,
                [
                    new DiagnosticField("Discovered files", result.Statistics.FilesDiscovered.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Discovered directories", result.Statistics.DirectoriesDiscovered.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Accepted files", _fileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Accepted directories", _directoryCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Skipped entries", _skippedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Accepted files with unsupported extraction extensions", _unsupportedExtractionCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Issues", result.Statistics.IssuesEncountered.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Elapsed milliseconds", elapsed.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
                ]);
        }

        public void Fail(Exception exception, TimeSpan elapsed) =>
            _sink?.Complete(
                SessionId,
                DiagnosticStatus.Failed,
                elapsed,
                "The scan stopped because of an unexpected operation-level failure.",
                DiagnosticSeverity.Error,
                [new DiagnosticField("Error category", exception.GetType().Name)]);

        private void RecordEntry(
            string stage,
            DiagnosticStatus status,
            DiagnosticSeverity severity,
            string message,
            IReadOnlyList<DiagnosticField> fields)
        {
            if (_detailedEntryCount >= DiagnosticLimits.MaximumScanEntryRecords)
            {
                _omittedEntryCount++;
                return;
            }

            _detailedEntryCount++;
            Publish(stage, status, severity, DiagnosticSection.IntermediateResults, message, fields);
        }

        private void PublishProgress()
        {
            var total = _fileCount + _directoryCount;
            if (total != 1 && total % 50 != 0)
            {
                return;
            }

            Publish(
                "Scan progress",
                DiagnosticStatus.Active,
                DiagnosticSeverity.Information,
                DiagnosticSection.Performance,
                "Filesystem discovery is continuing.",
                [
                    new DiagnosticField("Files discovered so far", _fileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Directories discovered so far", _directoryCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Issues so far", _skippedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ]);
        }
    }
}
