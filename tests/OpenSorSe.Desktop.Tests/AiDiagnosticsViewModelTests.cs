using OpenSorSe.Application.AI;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies diagnostic export failures remain contained at the UI boundary.</summary>
public sealed class AiDiagnosticsViewModelTests
{
    /// <summary>Verifies a failed clipboard operation is surfaced without escaping.</summary>
    [Fact]
    public async Task CopyAsync_ClipboardFailure_IsReportedSafely()
    {
        var viewModel = new AiDiagnosticsViewModel(
            new AiDiagnosticsCollector(),
            new ThrowingClipboard());

        await viewModel.CopyAsync("private diagnostic text");

        Assert.Equal(
            "The diagnostic text could not be copied. No retained diagnostic data was changed.",
            viewModel.StatusText);
    }

    /// <summary>Verifies successful explicit exports remind the user to review sensitive data.</summary>
    [Fact]
    public void ReportExportResult_Success_RemindsUserToReview()
    {
        var viewModel = new AiDiagnosticsViewModel(
            new AiDiagnosticsCollector(),
            new RecordingClipboard());

        viewModel.ReportExportResult(true);

        Assert.Equal("Diagnostic report saved. Review it before sharing.", viewModel.StatusText);
    }

    private sealed class ThrowingClipboard : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("Synthetic clipboard failure."));
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
