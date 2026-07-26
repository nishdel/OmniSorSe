using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Scanner;

/// <summary>
/// Reads operating-system filesystem metadata without opening or modifying file contents.
/// </summary>
public sealed class FileMetadataReader : IFileMetadataReader
{
    private const string LoggerCategory = "Scanner";
    private readonly IErrorHandler _errorHandler;
    private readonly ILogger _logger;
    private readonly IDiagnosticsEventSink? _diagnostics;

    /// <summary>
    /// Initializes a metadata reader that records diagnostics through Core infrastructure.
    /// </summary>
    /// <param name="loggingService">The centralized logging service.</param>
    /// <param name="errorHandler">The handler used for unexpected operation-level failures.</param>
    /// <param name="diagnostics">The optional failure-isolated detailed diagnostics sink.</param>
    public FileMetadataReader(
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
    public Task<FileMetadataResult> ReadAsync(
        IReadOnlyCollection<FileEntry> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Read(files, cancellationToken), CancellationToken.None);
    }

    private FileMetadataResult Read(IReadOnlyCollection<FileEntry> files, CancellationToken cancellationToken)
    {
        var enrichedFiles = new List<FileEntry>(files.Count);
        var issues = new List<FileMetadataIssue>();
        long enrichedCount = 0;
        var started = Stopwatch.StartNew();
        var related = files
            .Select(file => file?.ScanDiagnosticSessionId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var sessionId = _diagnostics?.BeginSession(
            DiagnosticCategory.Scanning,
            "Read filesystem metadata",
            [new DiagnosticField("Discovered file count", files.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            related);
        foreach (var relatedSessionId in related)
        {
            _diagnostics?.Relate(relatedSessionId, sessionId);
        }
        var diagnostic = new MetadataDiagnosticPublisher(_diagnostics, sessionId);

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var enrichedFile = ReadFile(file, issues, diagnostic);
                if (enrichedFile.Metadata is not null)
                {
                    enrichedCount++;
                }

                enrichedFiles.Add(enrichedFile);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var result = new FileMetadataResult(
                enrichedFiles.ToArray(),
                new FileMetadataStatistics(files.Count, enrichedCount, issues.Count),
                issues.ToArray());
            diagnostic.Complete(result, started.Elapsed);
            return result;
        }
        catch (OperationCanceledException)
        {
            diagnostic.Cancel(started.Elapsed);
            throw;
        }
        catch (Exception exception)
        {
            diagnostic.Fail(exception, started.Elapsed);
            _errorHandler.Report(new ApplicationError(
                LoggerCategory,
                "File metadata processing could not be completed due to an unexpected error.",
                ApplicationErrorSeverity.Error,
                exception));
            throw;
        }
    }

    private FileEntry ReadFile(
        FileEntry? file,
        List<FileMetadataIssue> issues,
        MetadataDiagnosticPublisher diagnostic)
    {
        if (file is null || string.IsNullOrWhiteSpace(file.FullPath))
        {
            RecordIssue(file?.FullPath ?? string.Empty, FileMetadataIssueKind.FileUnavailable, "The file path is invalid.", issues, diagnostic: diagnostic);
            return file ?? new FileEntry(string.Empty);
        }

        string fileName;
        string extension;
        try
        {
            fileName = Path.GetFileName(file.FullPath);
            extension = Path.GetExtension(file.FullPath).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            RecordIssue(file.FullPath, FileMetadataIssueKind.FileUnavailable, "The file path is invalid.", issues, exception, diagnostic);
            return file;
        }
        var metadataUnavailable = false;
        FileAttributes? attributes = null;
        long? sizeInBytes = null;
        DateTimeOffset? creationTimeUtc = null;
        DateTimeOffset? lastWriteTimeUtc = null;
        DateTimeOffset? lastAccessTimeUtc = null;
        var fileInfo = new FileInfo(file.FullPath);

        try
        {
            attributes = File.GetAttributes(file.FullPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            RecordIssue(file.FullPath, FileMetadataIssueKind.AccessDenied, "Access to the file metadata was denied.", issues, exception, diagnostic);
            return file;
        }
        catch (Exception exception) when (IsRecoverableFilesystemException(exception))
        {
            RecordIssue(file.FullPath, FileMetadataIssueKind.FileUnavailable, "The file is unavailable or changed after discovery.", issues, exception, diagnostic);
            return file;
        }

        if (attributes.Value.HasFlag(FileAttributes.ReparsePoint))
        {
            RecordIssue(file.FullPath, FileMetadataIssueKind.ReparsePointSkipped, "The reparse point was skipped.", issues, diagnostic: diagnostic);
            return file with { Metadata = null };
        }

        TryRead(() => fileInfo.Length, value => sizeInBytes = value, ref metadataUnavailable);
        TryRead(() => ToUtcOffset(fileInfo.CreationTimeUtc), value => creationTimeUtc = value, ref metadataUnavailable);
        TryRead(() => ToUtcOffset(fileInfo.LastWriteTimeUtc), value => lastWriteTimeUtc = value, ref metadataUnavailable);
        TryRead(() => ToUtcOffset(fileInfo.LastAccessTimeUtc), value => lastAccessTimeUtc = value, ref metadataUnavailable);

        if (metadataUnavailable)
        {
            RecordIssue(file.FullPath, FileMetadataIssueKind.MetadataUnavailable, "Some filesystem metadata could not be retrieved.", issues, diagnostic: diagnostic);
        }

        var result = file with
        {
            Metadata = new FileMetadata(
                fileName,
                extension,
                sizeInBytes,
                creationTimeUtc,
                lastWriteTimeUtc,
                lastAccessTimeUtc,
                attributes.Value),
        };
        diagnostic.RecordAccepted(result, metadataUnavailable);
        return result;
    }

    private static void TryRead<T>(Func<T> read, Action<T> assign, ref bool metadataUnavailable)
    {
        try
        {
            assign(read());
        }
        catch (Exception exception) when (IsRecoverableFilesystemException(exception))
        {
            metadataUnavailable = true;
        }
    }

    private void RecordIssue(
        string filePath,
        FileMetadataIssueKind kind,
        string message,
        List<FileMetadataIssue> issues,
        Exception? exception = null,
        MetadataDiagnosticPublisher? diagnostic = null)
    {
        issues.Add(new FileMetadataIssue(filePath, kind, message));
        _logger.LogWarning(
            "Metadata issue {IssueKind}. Error category: {ErrorCategory}.",
            kind,
            exception?.GetType().Name ?? "None");
        diagnostic?.RecordIssue(filePath, kind, message);
    }

    private static bool IsRecoverableFilesystemException(Exception exception) => exception is
        IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static DateTimeOffset ToUtcOffset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private sealed class MetadataDiagnosticPublisher
    {
        private readonly IDiagnosticsEventSink? _sink;
        private readonly string? _sessionId;
        private int _detailedCount;
        private int _omittedCount;

        public MetadataDiagnosticPublisher(IDiagnosticsEventSink? sink, string? sessionId)
        {
            _sink = sink;
            _sessionId = sessionId;
        }

        public void RecordAccepted(FileEntry file, bool isPartial) =>
            PublishBounded(
                isPartial ? "Metadata read partial" : "Metadata read",
                isPartial ? DiagnosticStatus.PartiallySucceeded : DiagnosticStatus.Succeeded,
                isPartial ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information,
                isPartial
                    ? "The available filesystem metadata was retained, but one or more properties could not be read."
                    : "Filesystem metadata was read.",
                [
                    new DiagnosticField("File", file.FullPath, DiagnosticDataClassification.Path),
                    new DiagnosticField("File size bytes", file.Metadata?.SizeInBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty),
                    new DiagnosticField("Extension", file.Metadata?.Extension ?? string.Empty),
                    new DiagnosticField("Metadata decision", isPartial ? "Accepted partial metadata" : "Accepted"),
                ]);

        public void RecordIssue(string path, FileMetadataIssueKind kind, string message) =>
            PublishBounded(
                "Metadata read failure",
                DiagnosticStatus.Skipped,
                DiagnosticSeverity.Warning,
                message,
                [
                    new DiagnosticField("File", path, DiagnosticDataClassification.Path),
                    new DiagnosticField("Failure kind", kind.ToString()),
                    new DiagnosticField("Metadata decision", "Skipped or partial"),
                ]);

        public void Complete(FileMetadataResult result, TimeSpan elapsed)
        {
            if (_omittedCount > 0)
            {
                _sink?.Publish(
                    _sessionId,
                    "Metadata detail sampling",
                    DiagnosticStatus.PartiallySucceeded,
                    DiagnosticSeverity.Warning,
                    DiagnosticSection.WarningsAndErrors,
                    "Additional per-file metadata records were represented only by aggregate counts.",
                    [new DiagnosticField("Omitted records", _omittedCount.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
            }

            _sink?.Complete(
                _sessionId,
                result.Issues.Count == 0 ? DiagnosticStatus.Succeeded : DiagnosticStatus.PartiallySucceeded,
                elapsed,
                "Filesystem metadata processing completed.",
                result.Issues.Count == 0 ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                [
                    new DiagnosticField("Examined files", result.Statistics.FilesProcessed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Enriched files", result.Statistics.FilesEnriched.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Metadata failures", result.Statistics.IssuesEncountered.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ]);
        }

        public void Cancel(TimeSpan elapsed) =>
            _sink?.Complete(
                _sessionId,
                DiagnosticStatus.Cancelled,
                elapsed,
                "Filesystem metadata processing was cancelled.",
                DiagnosticSeverity.Warning);

        public void Fail(Exception exception, TimeSpan elapsed) =>
            _sink?.Complete(
                _sessionId,
                DiagnosticStatus.Failed,
                elapsed,
                "Filesystem metadata processing failed.",
                DiagnosticSeverity.Error,
                [new DiagnosticField("Error category", exception.GetType().Name)]);

        private void PublishBounded(
            string stage,
            DiagnosticStatus status,
            DiagnosticSeverity severity,
            string message,
            IReadOnlyList<DiagnosticField> fields)
        {
            if (_detailedCount >= DiagnosticLimits.MaximumScanEntryRecords)
            {
                _omittedCount++;
                return;
            }

            _detailedCount++;
            _sink?.Publish(
                _sessionId,
                stage,
                status,
                severity,
                status == DiagnosticStatus.Succeeded
                    ? DiagnosticSection.IntermediateResults
                    : DiagnosticSection.WarningsAndErrors,
                message,
                fields);
        }
    }
}
