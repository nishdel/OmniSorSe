using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop.Services;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Projects platform capability and application-location data for Settings.</summary>
public sealed class PlatformDiagnosticsViewModel : ViewModelBase
{
    private readonly IPlatformCapabilityProvider _capabilityProvider;
    private readonly IApplicationPathProvider _pathProvider;
    private readonly IClipboardService? _clipboard;
    private readonly IExternalFileLauncher? _launcher;
    private string _statusText = "Platform capabilities were detected at startup.";

    /// <summary>Creates the platform diagnostics presentation.</summary>
    public PlatformDiagnosticsViewModel(
        IPlatformCapabilityProvider capabilityProvider,
        IApplicationPathProvider pathProvider,
        IClipboardService? clipboard = null,
        IExternalFileLauncher? launcher = null)
    {
        _capabilityProvider = capabilityProvider ??
                              throw new ArgumentNullException(nameof(capabilityProvider));
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _clipboard = clipboard;
        _launcher = launcher;
        Capabilities = Array.AsReadOnly(_capabilityProvider.Capabilities
            .Select(value => new PlatformCapabilityPresentation(
                value.Kind.ToString(),
                value.State.ToString(),
                value.Explanation))
            .ToArray());
        CopyReportCommand = new AsyncRelayCommand(
            CopyReportAsync,
            () => _clipboard is not null);
        OpenDiagnosticsFolderCommand = new AsyncRelayCommand(
            OpenDiagnosticsFolderAsync,
            () => _launcher is not null);
    }

    /// <summary>Gets the detected operating-system family and description.</summary>
    public string OperatingSystem =>
        $"{_capabilityProvider.Platform} — {_capabilityProvider.OperatingSystemDescription}";

    /// <summary>Gets the process architecture.</summary>
    public string Architecture => _capabilityProvider.ProcessArchitecture.ToString();

    /// <summary>Gets the active .NET runtime.</summary>
    public string Runtime => _capabilityProvider.RuntimeDescription;

    /// <summary>Gets the application-owned location summary.</summary>
    public string ApplicationLocations
    {
        get
        {
            var paths = _pathProvider.Paths;
            return string.Join(
                Environment.NewLine,
                $"Configuration: {paths.ConfigurationDirectory}",
                $"Data: {paths.DataDirectory}",
                $"State: {paths.StateDirectory}",
                $"Cache: {paths.CacheDirectory}",
                $"Diagnostics: {paths.DiagnosticsDirectory}",
                $"Plugins: {paths.PluginDirectory}");
        }
    }

    /// <summary>Gets the explicit symbolic-link policy.</summary>
    public string LinkPolicy =>
        "Links and reparse points are inspected and are not followed outside approved roots.";

    /// <summary>Gets current capability rows.</summary>
    public IReadOnlyList<PlatformCapabilityPresentation> Capabilities { get; }

    /// <summary>Gets status for the last explicit diagnostics action.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets the command that copies a bounded human-readable bug-report summary.</summary>
    public IAsyncRelayCommand CopyReportCommand { get; }

    /// <summary>Gets the command that opens the application-owned diagnostics directory.</summary>
    public IAsyncRelayCommand OpenDiagnosticsFolderCommand { get; }

    private async Task CopyReportAsync()
    {
        if (_clipboard is null)
        {
            return;
        }

        try
        {
            await _clipboard
                .SetTextAsync(_capabilityProvider.ExportHumanReadable(), CancellationToken.None)
                .ConfigureAwait(false);
            StatusText = "Platform report copied.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            StatusText = "The platform report could not be copied.";
        }
    }

    private async Task OpenDiagnosticsFolderAsync()
    {
        if (_launcher is null)
        {
            return;
        }

        var result = await _launcher
            .OpenFolderAsync(_pathProvider.Paths.DiagnosticsDirectory, CancellationToken.None)
            .ConfigureAwait(false);
        StatusText = result.Message;
    }
}

/// <summary>Contains one user-facing capability row.</summary>
public sealed record PlatformCapabilityPresentation(
    string Capability,
    string State,
    string Explanation);
