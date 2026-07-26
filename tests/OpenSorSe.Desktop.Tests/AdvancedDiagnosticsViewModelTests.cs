using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies unified filtering, live projection, actions, and truthful placeholders.</summary>
public sealed class AdvancedDiagnosticsViewModelTests
{
    /// <summary>Verifies live upserts are reverse chronological and category/status filters are applied.</summary>
    [Fact]
    public void Upsert_FiltersUnifiedSessionsAndUpdatesSelectedTabs()
    {
        var collector = EnabledCollector();
        var clipboard = new RecordingClipboard();
        var viewModel = new AdvancedDiagnosticsViewModel(
            collector,
            new DiagnosticsExportService(),
            clipboard);
        var aiId = collector.BeginSession(DiagnosticCategory.Ai, "Rename")!;
        var scanId = collector.BeginSession(DiagnosticCategory.Scanning, "Scan")!;
        collector.Complete(scanId, DiagnosticStatus.Cancelled, TimeSpan.FromSeconds(1), "Cancelled");
        viewModel.Upsert(collector.Get(aiId)!, true);
        viewModel.Upsert(collector.Get(scanId)!, true);

        Assert.Equal(scanId, viewModel.VisibleSessions[0].SessionId);
        viewModel.SelectedCategoryFilter = viewModel.CategoryFilters.Single(item =>
            item.Category == DiagnosticCategory.Ai);
        Assert.Equal(aiId, Assert.Single(viewModel.VisibleSessions).SessionId);
        viewModel.SelectedStatusFilter = AdvancedDiagnosticStatusFilter.Cancelled;
        Assert.True(viewModel.HasNoVisibleSessions);
        viewModel.SelectedCategoryFilter = viewModel.CategoryFilters[0];
        Assert.Equal(scanId, Assert.Single(viewModel.VisibleSessions).SessionId);
        Assert.Contains("Status: Cancelled", viewModel.OverviewText, StringComparison.Ordinal);
        Assert.Contains("Cancelled", viewModel.TimelineText, StringComparison.Ordinal);
    }

    /// <summary>Verifies unsupported categories are explicitly labelled and no fake sessions exist.</summary>
    [Fact]
    public void CategoryFilters_MarkPlannedCategoriesNotInstrumented()
    {
        var viewModel = new AdvancedDiagnosticsViewModel(
            EnabledCollector(),
            new DiagnosticsExportService(),
            new RecordingClipboard());

        var planned = viewModel.CategoryFilters.Where(item => !item.IsInstrumented).ToArray();

        Assert.Equal(5, planned.Length);
        Assert.All(planned, item =>
            Assert.Contains("not yet instrumented", item.DisplayName, StringComparison.Ordinal));
        Assert.DoesNotContain(
            viewModel.Sessions,
            item => !DiagnosticCategoryRegistry.Get(item.Session.Category).IsInstrumented);
    }

    /// <summary>Verifies copy/export/clear actions use the selected common session only.</summary>
    [Fact]
    public async Task Actions_CopyExportAndClearCommonSessions()
    {
        var collector = EnabledCollector();
        var clipboard = new RecordingClipboard();
        var id = collector.BeginSession(
            DiagnosticCategory.OcrAndTextExtraction,
            "Extract",
            [new DiagnosticField("File", @"C:\known.pdf", DiagnosticDataClassification.Path)])!;
        collector.Publish(
            id,
            "Native text",
            DiagnosticStatus.Succeeded,
            DiagnosticSeverity.Information,
            DiagnosticSection.Outputs,
            "Extracted",
            [new DiagnosticField("Text", "bounded text", DiagnosticDataClassification.Content)]);
        var viewModel = new AdvancedDiagnosticsViewModel(
            collector,
            new DiagnosticsExportService(),
            clipboard);
        viewModel.SelectedTabIndex = 4;

        await viewModel.CopyCurrentSectionCommand.ExecuteAsync(null);

        Assert.Contains("Native text", clipboard.Text, StringComparison.Ordinal);
        Assert.Contains(id, viewModel.BuildSelectedJson(), StringComparison.Ordinal);
        Assert.Contains(id, viewModel.BuildSelectedText(), StringComparison.Ordinal);
        Assert.Contains(id, viewModel.BuildAllJson(), StringComparison.Ordinal);
        viewModel.ClearSelectedCommand.Execute(null);
        Assert.Empty(collector.GetRecent());
        Assert.True(viewModel.HasNoVisibleSessions);
    }

    /// <summary>Verifies clipboard and export-service failures are contained and reported without clearing data.</summary>
    [Fact]
    public async Task Actions_FailingClipboardOrExporter_LeaveSessionAvailable()
    {
        var collector = EnabledCollector();
        var id = collector.BeginSession(DiagnosticCategory.Ai, "Rename")!;
        var viewModel = new AdvancedDiagnosticsViewModel(
            collector,
            new ThrowingExporter(),
            new ThrowingClipboard());

        await viewModel.CopyCompleteDiagnosticCommand.ExecuteAsync(null);
        var json = viewModel.BuildSelectedJson();

        Assert.Equal("{}", json);
        Assert.Contains("could not", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(id, Assert.Single(viewModel.Sessions).SessionId);
        Assert.NotNull(collector.Get(id));
    }

    /// <summary>Verifies the viewer releases immutable snapshots after an external settings/exit clear.</summary>
    [Fact]
    public void Reload_AfterExternalStoreClearReleasesViewerHistory()
    {
        var collector = EnabledCollector();
        collector.BeginSession(DiagnosticCategory.Scanning, "Scan");
        var viewModel = new AdvancedDiagnosticsViewModel(
            collector,
            new DiagnosticsExportService(),
            new RecordingClipboard());
        Assert.Single(viewModel.Sessions);

        collector.ClearAll();
        viewModel.Reload();

        Assert.Empty(viewModel.Sessions);
        Assert.True(viewModel.HasNoVisibleSessions);
    }

    /// <summary>Verifies Settings exposes one master switch and disables subordinate controls in XAML.</summary>
    [Fact]
    public void SettingsView_UsesMasterDiagnosticsSwitchAndSharedPrivacyText()
    {
        var root = FindRepositoryRoot();
        var settingsXaml = File.ReadAllText(
            Path.Combine(root, "src", "OpenSorSe.Desktop", "Views", "SettingsView.axaml"));
        var viewerXaml = File.ReadAllText(
            Path.Combine(root, "src", "OpenSorSe.Desktop", "Views", "AdvancedDiagnosticsWindow.axaml"));

        Assert.Contains("Enable diagnostics", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding Draft.DiagnosticsEnabled}\"", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("not yet instrumented", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("authorization headers are always removed", settingsXaml, StringComparison.Ordinal);
        foreach (var tab in new[]
                 {
                     "Overview", "Timeline", "Inputs", "Intermediate results", "Outputs",
                     "Warnings and errors", "Performance",
                 })
        {
            Assert.Contains($"Header=\"{tab}\"", viewerXaml, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies desktop exit explicitly releases process-session diagnostic content.</summary>
    [Fact]
    public void AppExit_ClearsTheCommonDiagnosticsCollector()
    {
        var root = FindRepositoryRoot();
        var appSource = File.ReadAllText(
            Path.Combine(root, "src", "OpenSorSe.Desktop", "App.axaml.cs"));

        Assert.Contains(
            "GetService<IDiagnosticsCollector>()?.ClearAll()",
            appSource,
            StringComparison.Ordinal);
    }

    /// <summary>Verifies the non-modal viewer coordinator owns no feature cancellation mechanism.</summary>
    [Fact]
    public void ViewerClosure_HasNoOperationCancellationPath()
    {
        var root = FindRepositoryRoot();
        var coordinator = File.ReadAllText(Path.Combine(
            root,
            "src",
            "OpenSorSe.Desktop",
            "Services",
            "AdvancedDiagnosticsWindowCoordinator.cs"));

        Assert.DoesNotContain("CancellationTokenSource", coordinator, StringComparison.Ordinal);
        Assert.DoesNotContain(".Cancel(", coordinator, StringComparison.Ordinal);
        Assert.Contains("_automaticOpeningSuppressed = true", coordinator, StringComparison.Ordinal);
    }

    private static InMemoryDiagnosticsCollector EnabledCollector()
    {
        var collector = new InMemoryDiagnosticsCollector();
        collector.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            AiDiagnostics = true,
            OcrAndTextExtractionDiagnostics = true,
            ScanningDiagnostics = true,
            ShowUnredactedDiagnosticContent = true,
        });
        return collector;
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (current is not null &&
               !File.Exists(Path.Combine(current, "OpenSorSe.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }

        return current ?? throw new DirectoryNotFoundException("The repository root was not found.");
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string Text { get; private set; } = string.Empty;

        public Task SetTextAsync(string text, CancellationToken cancellationToken)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingClipboard : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated clipboard failure.");
    }

    private sealed class ThrowingExporter : IDiagnosticsExportService
    {
        public string ExportJson(DiagnosticSession session) => throw Failure();
        public string ExportText(DiagnosticSession session) => throw Failure();
        public string ExportAllJson(IReadOnlyList<DiagnosticSession> sessions) => throw Failure();
        public string ExportAllText(IReadOnlyList<DiagnosticSession> sessions) => throw Failure();

        private static InvalidOperationException Failure() =>
            new("Simulated export failure.");
    }
}
