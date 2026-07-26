using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Core.Logging;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies privacy-safe aggregate logging presentation.
/// </summary>
public sealed class LogViewerViewModelTests
{
    /// <summary>
    /// Verifies refresh projects user-facing process-lifetime diagnostics without querying log-entry payloads.
    /// </summary>
    [Fact]
    public void Refresh_ProjectsAggregateCountersAndLoggingHealth()
    {
        var logging = new TestLoggingService(new LoggingStatistics(1, 2, 3, 4, 5, 6, 7));
        var viewModel = new LogViewerViewModel(logging);

        Assert.Equal(21L, viewModel.RecordedEventCount);
        Assert.Equal(7L, viewModel.Statistics.FileWriteFailures);
        Assert.Equal("Attention needed: OpenSorSe could not write one or more diagnostic log entries.", viewModel.LoggingStatus);
        Assert.Equal("No diagnostic events have been recorded in this application session.", viewModel.StatusText);
        Assert.Equal(1, logging.StatisticsRequestCount);
    }

    /// <summary>
    /// Verifies the empty diagnostics state explains that no events exist in the current application session.
    /// </summary>
    [Fact]
    public void Constructor_NoRecordedEvents_UsesPlainLanguageEmptyState()
    {
        var logging = new TestLoggingService(LoggingStatistics.Empty);
        var viewModel = new LogViewerViewModel(logging);

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasRecordedEvents);
        Assert.Equal("No diagnostic events have been recorded in this application session.", viewModel.EmptyStateMessage);
        Assert.Equal("Healthy: no diagnostic log write failures have been recorded.", viewModel.LoggingStatus);
        Assert.Equal(viewModel.EmptyStateMessage, viewModel.StatusText);
        Assert.Equal(1, logging.StatisticsRequestCount);
    }

    /// <summary>Verifies category/severity projection and clipboard output use only bounded safe event details.</summary>
    [Fact]
    public async Task FiltersAndCopy_ProjectSelectedSafeDiagnosticEvent()
    {
        var events = new[]
        {
            new DiagnosticEvent(2, DateTimeOffset.UnixEpoch.AddSeconds(2), LogLevel.Error, "Scanner", "Could not read metadata.", 7, "ReadFailed", "IOException", "File disappeared."),
            new DiagnosticEvent(1, DateTimeOffset.UnixEpoch, LogLevel.Information, "Catalog", "Catalog refreshed.", 0, null, null, null),
        };
        var logging = new TestLoggingService(new LoggingStatistics(0, 0, 1, 0, 1, 0, 0), events);
        var clipboard = new RecordingClipboard();
        var viewModel = new LogViewerViewModel(logging, clipboard);

        Assert.Equal(["All categories", "Catalog", "Scanner"], viewModel.Categories);
        viewModel.SeverityFilter = DiagnosticSeverityFilter.Error;
        var selected = Assert.Single(viewModel.VisibleEvents);
        viewModel.SelectedEvent = selected;
        await viewModel.CopyDiagnosticDetailsCommand.ExecuteAsync(null);

        Assert.Contains("Severity: Error", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("Category: Scanner", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains("Safe exception summary: File disappeared.", clipboard.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", clipboard.Text, StringComparison.Ordinal);
    }

    private sealed class TestLoggingService(
        LoggingStatistics statistics,
        IReadOnlyList<DiagnosticEvent>? events = null) : ILoggingService
    {
        public int StatisticsRequestCount { get; private set; }

        public void Initialize(LogLevel minimumLevel)
        {
        }

        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public LoggingStatistics GetStatistics()
        {
            StatisticsRequestCount++;
            return statistics;
        }

        public IReadOnlyList<DiagnosticEvent> GetRecentEvents() => events ?? [];

        public void Dispose()
        {
        }
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string Text { get; private set; } = string.Empty;

        public Task SetTextAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Text = text;
            return Task.CompletedTask;
        }
    }
}
