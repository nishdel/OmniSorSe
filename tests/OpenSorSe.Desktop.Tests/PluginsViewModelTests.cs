#pragma warning disable CS1591

using OpenSorSe.Application.Plugins;
using OpenSorSe.Desktop.Services;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Extensions.Abstractions;

namespace OpenSorSe.Desktop.Tests;

public sealed class PluginsViewModelTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.PluginsViewModel.Tests",
        Guid.NewGuid().ToString("N"));

    public PluginsViewModelTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }

        DeleteEmptyParent(_workspace);
    }

    [Fact]
    public async Task Refresh_DisplaysTrustCapabilityIntegrityDependencyAndContributionState()
    {
        using var manager = new RecordingManager(_workspace, [External()]);
        var viewModel = new PluginsViewModel(manager);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.Plugins);
        Assert.Equal("External", row.Origin);
        Assert.Contains("ReadFileMetadata", row.RequestedCapabilities);
        Assert.Contains("NotCalculated", row.Integrity);
        Assert.Contains("example.metadata", row.Contributions);
        Assert.Equal("None", row.Dependencies);
        Assert.False(row.IsEnabled);
    }

    [Fact]
    public async Task EnableAndDisable_AreExplicitAndGrantOnlyDisplayedRequests()
    {
        using var manager = new RecordingManager(_workspace, [External()]);
        var viewModel = new PluginsViewModel(manager);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedPlugin = Assert.Single(viewModel.Plugins);

        await viewModel.EnableCommand.ExecuteAsync(null);

        Assert.Equal(1, manager.EnableCount);
        Assert.Contains(PluginCapability.ReadFileMetadata, manager.LastGrants!);
        viewModel.SelectedPlugin = Assert.Single(viewModel.Plugins);
        await viewModel.DisableCommand.ExecuteAsync(null);
        Assert.Equal(1, manager.DisableCount);
    }

    [Fact]
    public async Task Enable_AllowsUserToGrantSubsetOfRequestedCapabilities()
    {
        using var manager = new RecordingManager(_workspace, [External()]);
        var viewModel = new PluginsViewModel(manager);
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedPlugin = Assert.Single(viewModel.Plugins);
        viewModel.CapabilityGrantText = string.Empty;

        await viewModel.EnableCommand.ExecuteAsync(null);

        Assert.Empty(manager.LastGrants!);
    }

    [Fact]
    public async Task InstallUpgradeAndRemoval_UseLocalPathsAndSeparateConfirmation()
    {
        var package = Path.Combine(_workspace, "plugin.zip");
        await File.WriteAllTextAsync(package, "test");
        using var manager = new RecordingManager(_workspace, [External()]);
        var viewModel = new PluginsViewModel(manager) { PackagePath = package };
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedPlugin = Assert.Single(viewModel.Plugins);

        await viewModel.InstallCommand.ExecuteAsync(null);
        await viewModel.UpgradeCommand.ExecuteAsync(null);
        viewModel.RequestRemoveCommand.Execute(null);
        Assert.True(viewModel.RemovalConfirmationPending);
        await viewModel.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Equal(1, manager.InstallCount);
        Assert.Equal(1, manager.UpgradeCount);
        Assert.Equal(1, manager.RemoveCount);
        Assert.True(manager.LastRemovalConfirmed);
    }

    [Fact]
    public async Task DiagnosticsExport_ExcludesSensitiveContentByManagerContract()
    {
        var export = Path.Combine(_workspace, "plugin-diagnostics.txt");
        using var manager = new RecordingManager(_workspace, [External()]);
        var viewModel = new PluginsViewModel(manager)
        {
            DiagnosticExportPath = export,
        };

        await viewModel.ExportDiagnosticsCommand.ExecuteAsync(null);

        var text = await File.ReadAllTextAsync(export);
        Assert.Contains("credentials", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-value", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiagnosticsExport_DoesNotOverwriteExistingFile()
    {
        var export = Path.Combine(_workspace, "existing-diagnostics.txt");
        await File.WriteAllTextAsync(export, "preserve");
        using var manager = new RecordingManager(_workspace, [External()]);
        var viewModel = new PluginsViewModel(manager)
        {
            DiagnosticExportPath = export,
        };

        await viewModel.ExportDiagnosticsCommand.ExecuteAsync(null);

        Assert.Equal("preserve", await File.ReadAllTextAsync(export));
        Assert.Contains("failed safely", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenFolder_UsesOnlyControlledPluginRoot()
    {
        using var manager = new RecordingManager(_workspace, [External()]);
        var launcher = new RecordingLauncher();
        var viewModel = new PluginsViewModel(manager, launcher);

        await viewModel.OpenPluginFolderCommand.ExecuteAsync(null);

        Assert.Equal(Path.GetFullPath(_workspace), launcher.OpenedFolder);
    }

    private static PluginDescriptor External() =>
        new(
            PluginManifestTestsManifest(),
            Path.GetTempPath(),
            new PluginProvenance(
                PluginOriginKind.ControlledFolder,
                "local package",
                DateTimeOffset.UtcNow),
            PluginLifecycleState.Disabled,
            PluginCompatibilityState.Compatible,
            PluginIntegrityStatus.NotCalculated,
            null,
            true,
            false,
            new HashSet<PluginCapability>(),
            [],
            null,
            false);

    private static PluginManifest PluginManifestTestsManifest() =>
        new(
            PluginLimits.CurrentManifestSchemaVersion,
            "example.plugin",
            "Example Plugin",
            "Example external plugin.",
            "1.0.0",
            "Publisher",
            "MIT",
            "1.4.0",
            null,
            "net8.0",
            "plugin.dll",
            "Example.Plugin",
            [
                new PluginManifestContribution(
                    "example.metadata",
                    ExtensionPointKind.MetadataProvider,
                    "Metadata"),
            ],
            [PluginCapability.ReadFileMetadata],
            [],
            null,
            null,
            false,
            null);

    private sealed class RecordingManager(
        string root,
        IReadOnlyList<PluginDescriptor> initial) : IPluginManager
    {
        private List<PluginDescriptor> _plugins = initial.ToList();
        public string PluginRoot { get; } = Path.GetFullPath(root);
        public int EnableCount { get; private set; }
        public int DisableCount { get; private set; }
        public int InstallCount { get; private set; }
        public int UpgradeCount { get; private set; }
        public int RemoveCount { get; private set; }
        public bool LastRemovalConfirmed { get; private set; }
        public IReadOnlySet<PluginCapability>? LastGrants { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<PluginDescriptor>> RefreshAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PluginDescriptor>>(_plugins.ToArray());

        public Task<IReadOnlyList<PluginDescriptor>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PluginDescriptor>>(_plugins.ToArray());

        public Task<PluginOperationResult> EnableAsync(
            string pluginId,
            string pluginVersion,
            IReadOnlySet<PluginCapability> grantedCapabilities,
            CancellationToken cancellationToken)
        {
            EnableCount++;
            LastGrants = grantedCapabilities;
            Update(pluginId, pluginVersion, true);
            return Task.FromResult(new PluginOperationResult(true, "Enabled."));
        }

        public Task<PluginOperationResult> DisableAsync(
            string pluginId,
            string pluginVersion,
            CancellationToken cancellationToken)
        {
            DisableCount++;
            Update(pluginId, pluginVersion, false);
            return Task.FromResult(new PluginOperationResult(true, "Disabled."));
        }

        public Task<PluginOperationResult> InstallAsync(
            string packagePath,
            CancellationToken cancellationToken)
        {
            InstallCount++;
            return Task.FromResult(new PluginOperationResult(true, "Installed."));
        }

        public Task<PluginOperationResult> UpgradeAsync(
            string packagePath,
            CancellationToken cancellationToken)
        {
            UpgradeCount++;
            return Task.FromResult(new PluginOperationResult(true, "Upgraded."));
        }

        public Task<PluginOperationResult> RemoveAsync(
            string pluginId,
            string pluginVersion,
            bool confirmed,
            CancellationToken cancellationToken)
        {
            RemoveCount++;
            LastRemovalConfirmed = confirmed;
            _plugins.RemoveAll(value => value.PluginId == pluginId && value.PluginVersion == pluginVersion);
            return Task.FromResult(new PluginOperationResult(true, "Removed."));
        }

        public string ExportDiagnostics() =>
            "Plugin diagnostics exclude file contents, extracted text, credentials, tokens, and secrets.";

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose()
        {
        }

        private void Update(string id, string version, bool enabled)
        {
            for (var index = 0; index < _plugins.Count; index++)
            {
                if (_plugins[index].PluginId == id && _plugins[index].PluginVersion == version)
                {
                    _plugins[index] = _plugins[index] with
                    {
                        IsEnabled = enabled,
                        LifecycleState = enabled
                            ? PluginLifecycleState.Active
                            : PluginLifecycleState.Disabled,
                        GrantedCapabilities = enabled
                            ? new HashSet<PluginCapability> { PluginCapability.ReadFileMetadata }
                            : new HashSet<PluginCapability>(),
                    };
                }
            }
        }
    }

    private static void DeleteEmptyParent(string path)
    {
        try
        {
            var parent = Directory.GetParent(path);
            if (parent?.Exists == true &&
                !parent.EnumerateFileSystemInfos().Any())
            {
                parent.Delete();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Parallel test cleanup may create another child after the empty check.
        }
    }

    private sealed class RecordingLauncher : IExternalFileLauncher
    {
        public string? OpenedFolder { get; private set; }

        public Task<ExternalLaunchResult> OpenFileAsync(
            string fullPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExternalLaunchResult.Success("Opened."));

        public Task<ExternalLaunchResult> OpenContainingFolderAsync(
            string fullPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExternalLaunchResult.Success("Opened."));

        public Task<ExternalLaunchResult> OpenFolderAsync(
            string fullPath,
            CancellationToken cancellationToken)
        {
            OpenedFolder = fullPath;
            return Task.FromResult(ExternalLaunchResult.Success("Opened."));
        }
    }
}
