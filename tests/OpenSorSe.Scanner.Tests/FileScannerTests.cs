using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Scanner;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Scanner.Tests;

/// <summary>
/// Verifies read-only filesystem traversal behavior.
/// </summary>
public sealed class FileScannerTests
{
    /// <summary>
    /// Verifies that nested files and directories are discovered recursively.
    /// </summary>
    [Fact]
    public async Task ScanAsync_RecursivelyDiscoversFilesAndDirectories()
    {
        using var directory = new TemporaryDirectory();
        var nestedDirectory = directory.CreateDirectory("first\\second");
        var rootFile = directory.CreateFile("root.txt");
        var nestedFile = Path.Combine(nestedDirectory, "nested.txt");
        File.WriteAllText(nestedFile, "content is not read by the scanner");

        var result = await CreateScanner().ScanAsync(CreateRequest(directory.Path));

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(2L, result.Statistics.FilesDiscovered);
        Assert.Equal(3L, result.Statistics.DirectoriesDiscovered);
        Assert.Empty(result.Issues);
        Assert.True(
            new HashSet<string>(PathComparer) { rootFile, nestedFile }.SetEquals(
                result.Files.Select(file => file.FullPath)));
        Assert.True(
            new HashSet<string>(PathComparer)
            {
                directory.Path,
                Path.Combine(directory.Path, "first"),
                nestedDirectory,
            }.SetEquals(result.Directories.Select(entry => entry.FullPath)));
    }

    /// <summary>
    /// Verifies that an empty root is returned as a discovered directory.
    /// </summary>
    [Fact]
    public async Task ScanAsync_IncludesAnEmptyRootDirectory()
    {
        using var directory = new TemporaryDirectory();

        var result = await CreateScanner().ScanAsync(CreateRequest(directory.Path));

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Empty(result.Files);
        Assert.Equal(new[] { directory.Path }, result.Directories.Select(entry => entry.FullPath));
        Assert.Equal(1L, result.Statistics.DirectoriesDiscovered);
    }

    /// <summary>
    /// Verifies that equivalent and overlapping roots produce unique entries.
    /// </summary>
    [Fact]
    public async Task ScanAsync_NormalizesAndDeduplicatesIdenticalAndOverlappingRoots()
    {
        using var directory = new TemporaryDirectory();
        var childDirectory = directory.CreateDirectory("child");
        var childFile = directory.CreateFile("child\\document.txt");
        var duplicateRoot = directory.Path + Path.DirectorySeparatorChar;

        var result = await CreateScanner().ScanAsync(CreateRequest(directory.Path, duplicateRoot, childDirectory));

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(1L, result.Statistics.FilesDiscovered);
        Assert.Equal(2L, result.Statistics.DirectoriesDiscovered);
        Assert.Equal(new[] { childFile }, result.Files.Select(entry => entry.FullPath));
        Assert.True(
            new HashSet<string>(PathComparer) { directory.Path, childDirectory }.SetEquals(
                result.Directories.Select(entry => entry.FullPath)));
    }

    /// <summary>
    /// Verifies that an unavailable root does not prevent other roots from scanning.
    /// </summary>
    [Fact]
    public async Task ScanAsync_RecordsUnavailableRootAndContinuesWithOtherRoots()
    {
        using var directory = new TemporaryDirectory();
        var availableFile = directory.CreateFile("available.txt");
        var missingRoot = Path.Combine(directory.Path, "missing");

        var result = await CreateScanner().ScanAsync(CreateRequest(missingRoot, directory.Path));

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(new[] { availableFile }, result.Files.Select(entry => entry.FullPath));
        Assert.Contains(result.Issues, issue =>
            PathComparer.Equals(issue.Path, missingRoot) &&
            issue.Kind == ScanIssueKind.RootDirectoryUnavailable);
    }

    /// <summary>
    /// Verifies that scanning reports structured progress information.
    /// </summary>
    [Fact]
    public async Task ScanAsync_ReportsStructuredProgress()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("one.txt");
        var reports = new List<ScanProgress>();
        var progress = new CallbackProgress(scanProgress => reports.Add(scanProgress));

        var result = await CreateScanner().ScanAsync(
            CreateRequest(directory.Path, TimeSpan.Zero),
            progress);

        Assert.NotEmpty(reports);
        Assert.Contains(reports, report => report.CurrentPath is not null);
        var finalReport = reports[^1];
        Assert.Equal(result.Statistics, finalReport.Statistics);
        Assert.Null(finalReport.CurrentPath);
    }

    /// <summary>
    /// Verifies that cancellation returns a partial scan result deterministically.
    /// </summary>
    [Fact]
    public async Task ScanAsync_ReturnsPartialResultWhenCancellationIsRequestedDuringProgressReporting()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("one.txt");
        directory.CreateFile("two.txt");
        using var cancellationSource = new CancellationTokenSource();
        var progress = new CallbackProgress(scanProgress =>
        {
            if (scanProgress.CurrentPath is not null)
            {
                cancellationSource.Cancel();
            }
        });

        var result = await CreateScanner().ScanAsync(
            CreateRequest(directory.Path, TimeSpan.Zero),
            progress,
            cancellationSource.Token);

        Assert.Equal(ScanStatus.Cancelled, result.Status);
        Assert.InRange(result.Statistics.FilesDiscovered, 0L, 2L);
        Assert.Equal(result.Statistics.FilesDiscovered, (long)result.Files.Count);
    }

    /// <summary>
    /// Verifies that a scan request requires at least one root directory.
    /// </summary>
    [Fact]
    public async Task ScanAsync_RejectsAnEmptyRootCollection()
    {
        var request = new ScanRequest(Array.Empty<string>(), ScanOptions.Default);

        await Assert.ThrowsAsync<ArgumentException>(() => CreateScanner().ScanAsync(request));
    }

    /// <summary>
    /// Verifies that a negative progress-report interval is rejected.
    /// </summary>
    [Fact]
    public async Task ScanAsync_RejectsANegativeProgressInterval()
    {
        using var directory = new TemporaryDirectory();
        var request = new ScanRequest(
            new[] { directory.Path },
            new ScanOptions(TimeSpan.FromMilliseconds(-1)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CreateScanner().ScanAsync(request));
    }

    /// <summary>Verifies accepted, unsupported-for-extraction, missing, and aggregate scan decisions are retained.</summary>
    [Fact]
    public async Task ScanAsync_AdvancedDiagnostics_ExplainsAcceptedAndSkippedEntries()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("supported.pdf");
        directory.CreateFile("unsupported.bin");
        var missing = Path.Combine(directory.Path, "missing");
        var collector = EnabledCollector(showUnredacted: true);
        var scanner = new FileScanner(new TestLoggingService(), new TestErrorHandler(), collector);

        var result = await scanner.ScanAsync(CreateRequest(missing, directory.Path));

        var session = collector.Get(result.DiagnosticSessionId!)!;
        Assert.Equal(DiagnosticStatus.PartiallySucceeded, session.Status);
        Assert.Contains(session.Events, item =>
            item.Stage == "File accepted" &&
            Field(item, "Extension") == ".pdf" &&
            Field(item, "Downstream extraction support") == "Supported");
        Assert.Contains(session.Events, item =>
            item.Stage == "File accepted" &&
            Field(item, "Extension") == ".bin" &&
            Field(item, "Downstream extraction support") == "Unsupported extension");
        Assert.Contains(session.Events, item =>
            item.Stage == "Entry skipped" &&
            Field(item, "Skip reason") == ScanIssueKind.RootDirectoryUnavailable.ToString());
        var completed = session.Events.Last(item => item.Stage == "Completed");
        Assert.Equal("2", Field(completed, "Accepted files"));
        Assert.Equal("1", Field(completed, "Accepted files with unsupported extraction extensions"));
    }

    /// <summary>Verifies large scans retain bounded detail and report aggregate sampling.</summary>
    [Fact]
    public async Task ScanAsync_LargeFolder_ReportsDetailedEntrySampling()
    {
        using var directory = new TemporaryDirectory();
        for (var index = 0; index < DiagnosticLimits.MaximumScanEntryRecords + 12; index++)
        {
            directory.CreateFile($"item-{index:D4}.txt");
        }
        var collector = EnabledCollector(showUnredacted: false);
        var scanner = new FileScanner(new TestLoggingService(), new TestErrorHandler(), collector);

        var result = await scanner.ScanAsync(CreateRequest(directory.Path));

        var session = collector.Get(result.DiagnosticSessionId!)!;
        var sampling = Assert.Single(session.Events, item => item.Stage == "Detailed entry sampling");
        Assert.Equal("13", Field(sampling, "Omitted detailed entries"));
        var completed = session.Events.Last(item => item.Stage == "Completed");
        Assert.Equal(
            (DiagnosticLimits.MaximumScanEntryRecords + 12).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Field(completed, "Accepted files"));
        Assert.DoesNotContain(
            directory.Path,
            string.Join(Environment.NewLine, session.Events.SelectMany(item => item.Fields).Select(field => field.Value)),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies every diagnostic-sink failure is isolated from read-only scan results.</summary>
    [Fact]
    public async Task ScanAsync_DiagnosticSinkFailure_DoesNotAlterResults()
    {
        using var directory = new TemporaryDirectory();
        var file = directory.CreateFile("known.pdf");
        var scanner = new FileScanner(
            new TestLoggingService(),
            new TestErrorHandler(),
            new ThrowingDiagnosticsSink());

        var result = await scanner.ScanAsync(CreateRequest(directory.Path));

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(file, Assert.Single(result.Files).FullPath);
        Assert.Empty(result.Issues);
        Assert.Null(result.DiagnosticSessionId);
    }

    /// <summary>Verifies cancellation remains a normal partial result with a terminal diagnostic.</summary>
    [Fact]
    public async Task ScanAsync_Cancelled_RecordsTerminalCancellation()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("one.txt");
        using var cancellation = new CancellationTokenSource();
        var collector = EnabledCollector(showUnredacted: true);
        var scanner = new FileScanner(new TestLoggingService(), new TestErrorHandler(), collector);
        var progress = new CallbackProgress(value =>
        {
            if (value.CurrentPath is not null)
            {
                cancellation.Cancel();
            }
        });

        var result = await scanner.ScanAsync(
            CreateRequest(directory.Path, TimeSpan.Zero),
            progress,
            cancellation.Token);

        Assert.Equal(ScanStatus.Cancelled, result.Status);
        Assert.Equal(DiagnosticStatus.Cancelled, collector.Get(result.DiagnosticSessionId!)!.Status);
    }

    /// <summary>Verifies missing or changed files produce correlated metadata-read diagnostics.</summary>
    [Fact]
    public async Task MetadataReader_MissingFile_RecordsFailureRelatedToScan()
    {
        using var directory = new TemporaryDirectory();
        var missing = Path.Combine(directory.Path, "gone.pdf");
        var collector = EnabledCollector(showUnredacted: true);
        var reader = new FileMetadataReader(
            new TestLoggingService(),
            new TestErrorHandler(),
            collector);

        var result = await reader.ReadAsync(
            [new FileEntry(missing) { ScanDiagnosticSessionId = "scan:parent" }]);

        Assert.Single(result.Issues);
        var session = Assert.Single(collector.GetRecent());
        Assert.Contains("scan:parent", session.RelatedSessionIds);
        Assert.Equal(DiagnosticStatus.PartiallySucceeded, session.Status);
        Assert.Contains(session.Events, item =>
            item.Stage == "Metadata read failure" &&
            Field(item, "Failure kind") == FileMetadataIssueKind.FileUnavailable.ToString());
    }

    private static string Field(OpenSorSe.Core.Diagnostics.DiagnosticEvent item, string name) =>
        item.Fields.Single(field => field.Name == name).Value;

    private static InMemoryDiagnosticsCollector EnabledCollector(bool showUnredacted)
    {
        var collector = new InMemoryDiagnosticsCollector();
        collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            ScanningDiagnostics = true,
            ShowUnredactedDiagnosticContent = showUnredacted,
        });
        return collector;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static FileScanner CreateScanner() => new(new TestLoggingService(), new TestErrorHandler());

    private static ScanRequest CreateRequest(string firstRoot, params string[] additionalRoots) =>
        CreateRequest(new[] { firstRoot }.Concat(additionalRoots), ScanOptions.Default.ProgressReportInterval);

    private static ScanRequest CreateRequest(string root, TimeSpan progressReportInterval) =>
        CreateRequest(new[] { root }, progressReportInterval);

    private static ScanRequest CreateRequest(IEnumerable<string> roots, TimeSpan progressReportInterval) =>
        new(roots.ToArray(), new ScanOptions(progressReportInterval));

    private sealed class CallbackProgress : IProgress<ScanProgress>
    {
        private readonly Action<ScanProgress> _callback;

        public CallbackProgress(Action<ScanProgress> callback)
        {
            _callback = callback;
        }

        public void Report(ScanProgress value)
        {
            _callback(value);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"OpenSorSe.Scanner.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string relativePath)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public string CreateFile(string relativePath)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }

    private sealed class TestLoggingService : ILoggingService
    {
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public void Dispose()
        {
        }

        public void Initialize(LogLevel minimumLevel)
        {
        }
    }

    private sealed class TestErrorHandler : IErrorHandler
    {
        public event EventHandler<ApplicationError>? ErrorReported;

        public void Report(ApplicationError applicationError)
        {
            ErrorReported?.Invoke(this, applicationError);
        }
    }

    private sealed class ThrowingDiagnosticsSink : IDiagnosticsEventSink
    {
        public bool IsCategoryEnabled(DiagnosticCategory category) => throw Failure();

        public string? BeginSession(
            DiagnosticCategory category,
            string operation,
            IReadOnlyList<DiagnosticField>? context = null,
            IReadOnlyCollection<string>? relatedSessionIds = null) =>
            throw Failure();

        public void Publish(
            string? sessionId,
            string stage,
            DiagnosticStatus status,
            DiagnosticSeverity severity,
            DiagnosticSection section,
            string message,
            IReadOnlyList<DiagnosticField>? fields = null) =>
            throw Failure();

        public void Relate(string? sessionId, params string?[] relatedSessionIds) =>
            throw Failure();

        public void Complete(
            string? sessionId,
            DiagnosticStatus status,
            TimeSpan elapsed,
            string message,
            DiagnosticSeverity severity = DiagnosticSeverity.Information,
            IReadOnlyList<DiagnosticField>? fields = null) =>
            throw Failure();

        private static InvalidOperationException Failure() =>
            new("Simulated diagnostic failure.");
    }
}
