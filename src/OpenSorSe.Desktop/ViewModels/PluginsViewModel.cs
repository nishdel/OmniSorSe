#pragma warning disable CS1591

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Plugins;
using OpenSorSe.Application.Watching;
using OpenSorSe.Desktop.Services;

namespace OpenSorSe.Desktop.ViewModels;

public sealed record PluginRow(
    string PluginId,
    string Version,
    string Name,
    string Publisher,
    string Description,
    string License,
    string Source,
    string Origin,
    string State,
    string Compatibility,
    string RequestedCapabilities,
    string GrantedCapabilities,
    string Integrity,
    string Dependencies,
    string Contributions,
    string LastError,
    bool IsBuiltIn,
    bool IsEnabled,
    bool IsQuarantined,
    bool RestartRequired);

public sealed class PluginsViewModel : ViewModelBase
{
    private readonly IPluginManager? _manager;
    private readonly IExternalFileLauncher? _launcher;
    private readonly IWatchedFolderCoordinator? _watchedFolders;
    private readonly ObservableCollection<PluginRow> _plugins = [];
    private PluginRow? _selectedPlugin;
    private string _packagePath = string.Empty;
    private string _capabilityGrantText = string.Empty;
    private string _diagnosticExportPath = string.Empty;
    private string _statusText = "Plugin support is unavailable in this preview.";
    private string _inspectionText = "Select a plugin to inspect its validated manifest and runtime state.";
    private bool _isBusy;
    private bool _removalConfirmationPending;

    public PluginsViewModel(
        IPluginManager? manager = null,
        IExternalFileLauncher? launcher = null,
        IWatchedFolderCoordinator? watchedFolders = null)
    {
        _manager = manager;
        _launcher = launcher;
        _watchedFolders = watchedFolders;
        Plugins = new ReadOnlyObservableCollection<PluginRow>(_plugins);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, CanOperate);
        InspectCommand = new RelayCommand(Inspect, () => SelectedPlugin is not null);
        EnableCommand = new AsyncRelayCommand(EnableAsync, CanEnable);
        DisableCommand = new AsyncRelayCommand(DisableAsync, CanDisable);
        InstallCommand = new AsyncRelayCommand(InstallAsync, CanInstall);
        UpgradeCommand = new AsyncRelayCommand(UpgradeAsync, CanInstall);
        RequestRemoveCommand = new RelayCommand(RequestRemove, CanRequestRemove);
        ConfirmRemoveCommand = new AsyncRelayCommand(ConfirmRemoveAsync, CanConfirmRemove);
        CancelRemoveCommand = new RelayCommand(CancelRemove, () => RemovalConfirmationPending && !IsBusy);
        OpenPluginFolderCommand = new AsyncRelayCommand(OpenPluginFolderAsync, () => _manager is not null && _launcher is not null && !IsBusy);
        ExportDiagnosticsCommand = new AsyncRelayCommand(ExportDiagnosticsAsync, CanExportDiagnostics);
        if (_manager is not null)
        {
            _ = RefreshAsync();
        }
    }

    public ReadOnlyObservableCollection<PluginRow> Plugins { get; }

    public PluginRow? SelectedPlugin
    {
        get => _selectedPlugin;
        set
        {
            if (SetProperty(ref _selectedPlugin, value))
            {
                CapabilityGrantText = value?.RequestedCapabilities == "None"
                    ? string.Empty
                    : value?.RequestedCapabilities ?? string.Empty;
                RemovalConfirmationPending = false;
                NotifyCommands();
            }
        }
    }

    public string PackagePath
    {
        get => _packagePath;
        set
        {
            if (SetProperty(ref _packagePath, value))
            {
                InstallCommand.NotifyCanExecuteChanged();
                UpgradeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CapabilityGrantText
    {
        get => _capabilityGrantText;
        set => SetProperty(ref _capabilityGrantText, value);
    }

    public string DiagnosticExportPath
    {
        get => _diagnosticExportPath;
        set
        {
            if (SetProperty(ref _diagnosticExportPath, value))
            {
                ExportDiagnosticsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string InspectionText
    {
        get => _inspectionText;
        private set => SetProperty(ref _inspectionText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool RemovalConfirmationPending
    {
        get => _removalConfirmationPending;
        private set
        {
            if (SetProperty(ref _removalConfirmationPending, value))
            {
                ConfirmRemoveCommand.NotifyCanExecuteChanged();
                CancelRemoveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand InspectCommand { get; }
    public IAsyncRelayCommand EnableCommand { get; }
    public IAsyncRelayCommand DisableCommand { get; }
    public IAsyncRelayCommand InstallCommand { get; }
    public IAsyncRelayCommand UpgradeCommand { get; }
    public IRelayCommand RequestRemoveCommand { get; }
    public IAsyncRelayCommand ConfirmRemoveCommand { get; }
    public IRelayCommand CancelRemoveCommand { get; }
    public IAsyncRelayCommand OpenPluginFolderCommand { get; }
    public IAsyncRelayCommand ExportDiagnosticsCommand { get; }

    private bool CanOperate() => _manager is not null && !IsBusy;
    private bool CanEnable() =>
        CanOperate() &&
        SelectedPlugin is { IsBuiltIn: false, IsEnabled: false, IsQuarantined: false } &&
        string.Equals(SelectedPlugin.Compatibility, "Compatible", StringComparison.Ordinal);
    private bool CanDisable() =>
        CanOperate() && SelectedPlugin is { IsBuiltIn: false, IsEnabled: true };
    private bool CanInstall() =>
        CanOperate() &&
        !string.IsNullOrWhiteSpace(PackagePath) &&
        Path.IsPathFullyQualified(PackagePath);
    private bool CanRequestRemove() =>
        CanOperate() && SelectedPlugin is { IsBuiltIn: false };
    private bool CanConfirmRemove() =>
        CanRequestRemove() && RemovalConfirmationPending;
    private bool CanExportDiagnostics() =>
        CanOperate() &&
        !string.IsNullOrWhiteSpace(DiagnosticExportPath) &&
        Path.IsPathFullyQualified(DiagnosticExportPath);

    private async Task RefreshAsync()
    {
        if (_manager is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var plugins = await _manager.RefreshAsync(CancellationToken.None);
            Replace(plugins);
            if (_watchedFolders is not null)
            {
                await _watchedFolders.RefreshAsync(CancellationToken.None);
            }

            return $"Discovery refreshed. {plugins.Count} installed plugin version(s) found.";
        });
    }

    private void Inspect()
    {
        if (SelectedPlugin is not { } plugin)
        {
            return;
        }

        InspectionText =
            $"{plugin.Name} {plugin.Version}{Environment.NewLine}" +
            $"ID: {plugin.PluginId}{Environment.NewLine}" +
            $"Publisher: {plugin.Publisher}; license: {plugin.License}{Environment.NewLine}" +
            $"Origin: {plugin.Origin}; source: {plugin.Source}{Environment.NewLine}" +
            $"Lifecycle: {plugin.State}; compatibility: {plugin.Compatibility}; integrity: {plugin.Integrity}{Environment.NewLine}" +
            $"Requested: {plugin.RequestedCapabilities}{Environment.NewLine}" +
            $"Granted: {plugin.GrantedCapabilities}{Environment.NewLine}" +
            $"Dependencies: {plugin.Dependencies}{Environment.NewLine}" +
            $"Contributions: {plugin.Contributions}{Environment.NewLine}" +
            $"Last error: {plugin.LastError}{Environment.NewLine}" +
            (plugin.RequestedCapabilities.Contains(
                nameof(OpenSorSe.Extensions.Abstractions.PluginCapability.NetworkAccess),
                StringComparison.Ordinal)
                ? $"Network access requested: this plugin must not be treated as offline or local-only.{Environment.NewLine}"
                : string.Empty) +
            "In-process plugins are not sandboxed. Integrity hashes detect changes but do not authenticate publishers.";
    }

    private async Task EnableAsync()
    {
        if (_manager is null || SelectedPlugin is not { } selected)
        {
            return;
        }

        await RunOperationAsync(() => _manager.EnableAsync(
            selected.PluginId,
            selected.Version,
            ParseCapabilities(CapabilityGrantText),
            CancellationToken.None));
    }

    private async Task DisableAsync()
    {
        if (_manager is null || SelectedPlugin is not { } selected)
        {
            return;
        }

        await RunOperationAsync(() => _manager.DisableAsync(
            selected.PluginId,
            selected.Version,
            CancellationToken.None));
    }

    private async Task InstallAsync()
    {
        if (_manager is null)
        {
            return;
        }

        await RunOperationAsync(() => _manager.InstallAsync(PackagePath, CancellationToken.None));
    }

    private async Task UpgradeAsync()
    {
        if (_manager is null)
        {
            return;
        }

        await RunOperationAsync(() => _manager.UpgradeAsync(PackagePath, CancellationToken.None));
    }

    private void RequestRemove()
    {
        RemovalConfirmationPending = true;
        StatusText = "Confirm removal after reviewing workflow, recipe, watched-folder, and imported-configuration dependencies. User documents are never removed.";
    }

    private async Task ConfirmRemoveAsync()
    {
        if (_manager is null || SelectedPlugin is not { } selected)
        {
            return;
        }

        await RunOperationAsync(() => _manager.RemoveAsync(
            selected.PluginId,
            selected.Version,
            confirmed: true,
            CancellationToken.None));
        RemovalConfirmationPending = false;
    }

    private void CancelRemove()
    {
        RemovalConfirmationPending = false;
        StatusText = "Plugin removal cancelled.";
    }

    private async Task OpenPluginFolderAsync()
    {
        if (_manager is null || _launcher is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_manager.PluginRoot);
            var result = await _launcher.OpenFolderAsync(_manager.PluginRoot, CancellationToken.None);
            StatusText = result.Message;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            StatusText = "The controlled plugin folder could not be opened.";
        }
    }

    private async Task ExportDiagnosticsAsync()
    {
        if (_manager is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var path = Path.GetFullPath(DiagnosticExportPath);
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("The diagnostic export path has no directory.");
            if (!Directory.Exists(directory))
            {
                return "The diagnostic export directory does not exist.";
            }

            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous);
            await using var writer = new StreamWriter(
                stream,
                new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(_manager.ExportDiagnostics());
            return "Plugin diagnostics exported without file contents, extracted text, credentials, tokens, or secrets.";
        });
    }

    private async Task RunOperationAsync(Func<Task<PluginOperationResult>> operation)
    {
        await RunAsync(async () =>
        {
            var result = await operation();
            var plugins = await _manager!.ListAsync(CancellationToken.None);
            Replace(plugins);
            if (_watchedFolders is not null)
            {
                await _watchedFolders.RefreshAsync(CancellationToken.None);
            }

            return result.Message;
        });
    }

    private async Task RunAsync(Func<Task<string>> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusText = await operation();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText = "The plugin operation failed safely.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Replace(IReadOnlyList<PluginDescriptor> plugins)
    {
        var selectedId = SelectedPlugin?.PluginId;
        var selectedVersion = SelectedPlugin?.Version;
        _plugins.Clear();
        foreach (var plugin in plugins)
        {
            _plugins.Add(ToRow(plugin));
        }

        SelectedPlugin = _plugins.SingleOrDefault(row =>
            string.Equals(row.PluginId, selectedId, StringComparison.Ordinal) &&
            string.Equals(row.Version, selectedVersion, StringComparison.Ordinal));
    }

    private static PluginRow ToRow(PluginDescriptor plugin)
    {
        var manifest = plugin.Manifest;
        return new PluginRow(
            plugin.PluginId,
            plugin.PluginVersion,
            plugin.DisplayName,
            manifest?.Publisher ?? "Unavailable",
            manifest?.Description ?? plugin.LastError ?? "Manifest unavailable.",
            manifest?.LicenseIdentifier ?? "Unavailable",
            plugin.Provenance.Source,
            plugin.IsBuiltIn ? "Built-in" : "External",
            plugin.LifecycleState.ToString(),
            plugin.Compatibility.ToString(),
            Join(manifest?.Capabilities.Select(value => value.ToString())),
            Join(plugin.GrantedCapabilities.Select(value => value.ToString())),
            plugin.IntegrityStatus +
            (plugin.CalculatedIntegrityHash is null ? string.Empty : $" ({plugin.CalculatedIntegrityHash[..12]}…)"),
            plugin.DependencyErrors.Count > 0
                ? string.Join("; ", plugin.DependencyErrors)
                : Join(manifest?.Dependencies.Select(value =>
                    $"{value.PluginId} >= {value.MinimumVersion}{(value.Optional ? " (optional)" : string.Empty)}")),
            Join(manifest?.Contributions.Select(value =>
                $"{value.ContributionId} [{value.ExtensionPoint}]")),
            plugin.LastError ?? "None",
            plugin.IsBuiltIn,
            plugin.IsEnabled,
            plugin.LifecycleState == PluginLifecycleState.Quarantined,
            plugin.RestartRequired);
    }

    private static string Join(IEnumerable<string>? values)
    {
        var materialized = (values ?? []).ToArray();
        return materialized.Length == 0 ? "None" : string.Join(", ", materialized);
    }

    private static IReadOnlySet<OpenSorSe.Extensions.Abstractions.PluginCapability> ParseCapabilities(
        string capabilities)
    {
        var result = new HashSet<OpenSorSe.Extensions.Abstractions.PluginCapability>();
        foreach (var value in capabilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<OpenSorSe.Extensions.Abstractions.PluginCapability>(value, out var parsed))
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        InspectCommand.NotifyCanExecuteChanged();
        EnableCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
        UpgradeCommand.NotifyCanExecuteChanged();
        RequestRemoveCommand.NotifyCanExecuteChanged();
        ConfirmRemoveCommand.NotifyCanExecuteChanged();
        CancelRemoveCommand.NotifyCanExecuteChanged();
        OpenPluginFolderCommand.NotifyCanExecuteChanged();
        ExportDiagnosticsCommand.NotifyCanExecuteChanged();
    }
}
