#pragma warning disable CS1591

using System.Runtime.InteropServices;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

public sealed class PlatformDiagnosticsViewModelTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.PlatformDiagnostics.Tests",
        Guid.NewGuid().ToString("N"));

    public PlatformDiagnosticsViewModelTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Presentation_ExposesCapabilitiesLocationsAndCopyableReport()
    {
        var pathProvider = new ApplicationPathProvider(
            PlatformServices.CurrentPlatform,
            _ => null,
            _workspace,
            Path.Combine(_workspace, "local"));
        pathProvider.EnsureOwnedDirectories();
        var provider = new FakeCapabilityProvider();
        var clipboard = new RecordingClipboard();
        var viewModel = new PlatformDiagnosticsViewModel(
            provider,
            pathProvider,
            clipboard);

        await viewModel.CopyReportCommand.ExecuteAsync(null);

        Assert.Contains("Test OS", viewModel.OperatingSystem, StringComparison.Ordinal);
        Assert.Contains(pathProvider.Paths.PluginDirectory, viewModel.ApplicationLocations, StringComparison.Ordinal);
        Assert.Equal(2, viewModel.Capabilities.Count);
        Assert.Contains("OpenSorSe platform diagnostics", clipboard.Text, StringComparison.Ordinal);
        Assert.Equal("Platform report copied.", viewModel.StatusText);
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

    private sealed class FakeCapabilityProvider : IPlatformCapabilityProvider
    {
        public HostPlatformKind Platform => HostPlatformKind.Linux;
        public string OperatingSystemDescription => "Test OS";
        public Architecture ProcessArchitecture => Architecture.X64;
        public string RuntimeDescription => ".NET Test";
        public IReadOnlyList<PlatformCapability> Capabilities { get; } =
        [
            new(
                PlatformCapabilityKind.Scanning,
                PlatformSupportState.Supported,
                "Root-confined."),
            new(
                PlatformCapabilityKind.PackagingAndUpdates,
                PlatformSupportState.Unavailable,
                "No package."),
        ];

        public PlatformCapability Get(PlatformCapabilityKind kind) =>
            Capabilities.Single(value => value.Kind == kind);

        public string ExportHumanReadable() =>
            "OpenSorSe platform diagnostics\nPlatform: Linux\nNo secrets.";
    }
}
