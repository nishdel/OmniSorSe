#pragma warning disable CS1591

using OpenSorSe.Application.Plugins;
using OpenSorSe.Extensions.Abstractions;

namespace OpenSorSe.Application.Tests;

public sealed class PluginRuntimeTests
{
    [Fact]
    public async Task Activate_BuiltInPlugin_RegistersContributionsAndStopsCleanly()
    {
        var diagnostics = new PluginDiagnostics();
        var registry = new PluginContributionRegistry(diagnostics);
        var definition = BuiltInPluginCatalog.Definitions[0];
        await using var runtime = new PluginRuntime(registry, diagnostics, [definition]);

        var activated = await runtime.ActivateAsync(
            Descriptor(definition.Manifest),
            CancellationToken.None);
        Assert.True(activated.Succeeded);
        Assert.Equal(PluginLifecycleState.Active, activated.Plugin!.LifecycleState);
        Assert.Single(registry.List());

        var stopped = await runtime.DeactivateAsync(
            definition.Manifest.PluginId,
            definition.Manifest.PluginVersion,
            CancellationToken.None);

        Assert.True(stopped.Succeeded);
        Assert.Empty(registry.List());
    }

    [Fact]
    public async Task Activate_InitializationException_IsContained()
    {
        var diagnostics = new PluginDiagnostics();
        var registry = new PluginContributionRegistry(diagnostics);
        var manifest = ManifestFor(
            "throwing.plugin",
            "throwing.metadata",
            ExtensionPointKind.MetadataProvider);
        var definition = new BuiltInPluginDefinition(manifest, static () => new ThrowingPlugin());
        await using var runtime = new PluginRuntime(registry, diagnostics, [definition]);

        var result = await runtime.ActivateAsync(Descriptor(manifest), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PluginLifecycleState.Failed, result.Plugin!.LifecycleState);
        Assert.Empty(registry.List());
        Assert.Contains(diagnostics.List(), value =>
            value.Kind == PluginDiagnosticKind.Initialization);
    }

    [Fact]
    public async Task Activate_InitializationTimeout_IsBounded()
    {
        var diagnostics = new PluginDiagnostics();
        var manifest = ManifestFor(
            "timeout.plugin",
            "timeout.metadata",
            ExtensionPointKind.MetadataProvider);
        var definition = new BuiltInPluginDefinition(manifest, static () => new BlockingPlugin());
        await using var runtime = new PluginRuntime(
            new PluginContributionRegistry(diagnostics),
            diagnostics,
            [definition],
            TimeSpan.FromMilliseconds(25));

        var result = await runtime.ActivateAsync(Descriptor(manifest), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(diagnostics.List(), value => value.Kind == PluginDiagnosticKind.Timeout);
    }

    [Fact]
    public async Task Activate_Cancellation_IsPropagatedAndNoContributionLeaks()
    {
        var diagnostics = new PluginDiagnostics();
        var registry = new PluginContributionRegistry(diagnostics);
        var manifest = ManifestFor(
            "cancel.plugin",
            "cancel.metadata",
            ExtensionPointKind.MetadataProvider);
        var definition = new BuiltInPluginDefinition(manifest, static () => new BlockingPlugin());
        await using var runtime = new PluginRuntime(
            registry,
            diagnostics,
            [definition],
            TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.ActivateAsync(Descriptor(manifest), cancellation.Token));

        Assert.Empty(registry.List());
    }

    [Fact]
    public async Task Activate_ExternalAssembly_UsesCollectibleContextAndReportsRestartLimitation()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "OpenSorSe.ExternalPlugin.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var entryAssembly = "external-test-plugin.dll";
        File.Copy(typeof(ExternalLoadTestPlugin).Assembly.Location, Path.Combine(workspace, entryAssembly));
        var diagnostics = new PluginDiagnostics();
        var registry = new PluginContributionRegistry(diagnostics);
        var manifest = PluginManifestTests.Manifest("external.load-test") with
        {
            EntryAssembly = entryAssembly,
            EntryType = typeof(ExternalLoadTestPlugin).FullName!,
            Contributions =
            [
                new PluginManifestContribution(
                    "external.metadata",
                    ExtensionPointKind.MetadataProvider,
                    "External metadata"),
            ],
        };
        var descriptor = ExternalDescriptor(manifest) with
        {
            InstallationPath = workspace,
            LifecycleState = PluginLifecycleState.Ready,
            IsEnabled = true,
            IsSelectedVersion = true,
            GrantedCapabilities = new HashSet<PluginCapability>(manifest.Capabilities),
        };

        var (activated, registeredCount, stopped) = await ActivateAndStopExternalAsync(
            descriptor,
            registry,
            diagnostics);
        Assert.True(activated.Succeeded);
        Assert.Equal(1, registeredCount);
        Assert.True(stopped.Succeeded);
        Assert.NotEmpty(stopped.SafeWarnings);
        Assert.Contains("restart", stopped.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(registry.List());
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Directory.Delete(workspace, recursive: true);
        var parent = Directory.GetParent(workspace);
        if (parent?.Exists == true &&
            !parent.EnumerateFileSystemInfos().Any())
        {
            parent.Delete();
        }
    }

    [Fact]
    public void Registry_DuplicateContributionId_DoesNotReplaceFirstOwner()
    {
        var diagnostics = new PluginDiagnostics();
        var registry = new PluginContributionRegistry(diagnostics);
        var firstManifest = ManifestFor(
            "first.plugin",
            "shared.metadata",
            ExtensionPointKind.MetadataProvider);
        var secondManifest = ManifestFor(
            "second.plugin",
            "shared.metadata",
            ExtensionPointKind.MetadataProvider);

        var first = registry.Register(
            Descriptor(firstManifest),
            [new MetadataContribution("shared.metadata")]);
        var second = registry.Register(
            Descriptor(secondManifest),
            [new MetadataContribution("shared.metadata")]);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal("first.plugin", Assert.Single(registry.List()).PluginId);
    }

    [Fact]
    public void Registry_UngrantedCapability_CannotRegisterContribution()
    {
        var diagnostics = new PluginDiagnostics();
        var registry = new PluginContributionRegistry(diagnostics);
        var manifest = PluginManifestTests.Manifest();
        var owner = Descriptor(manifest) with
        {
            GrantedCapabilities = new HashSet<PluginCapability>(),
        };

        var result = registry.Register(
            owner,
            [new MetadataContribution(manifest.Contributions[0].ContributionId)]);

        Assert.False(result.Succeeded);
        Assert.Contains("granted capability", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(registry.List());
    }

    [Fact]
    public async Task Manager_RepeatedActivationFailures_QuarantineExternalPlugin()
    {
        var manifest = PluginManifestTests.Manifest();
        var descriptor = ExternalDescriptor(manifest);
        var state = new PluginDiscoveryTests.MemoryStateStore(
        [
            new PluginStateEntry(
                manifest.PluginId,
                manifest.PluginVersion,
                true,
                new HashSet<PluginCapability>(),
                new string('a', 64),
                0,
                false,
                null,
                null),
        ]);

        for (var attempt = 0; attempt < PluginLimits.MaximumFailuresBeforeQuarantine; attempt++)
        {
            using var manager = new PluginManager(
                Path.GetTempPath(),
                new StateAwareDiscovery(descriptor, state),
                state,
                new FixedIntegrity(),
                new FailingRuntime(),
                new NullPackageService(),
                new PluginDiagnostics());
            await manager.InitializeAsync(CancellationToken.None);
        }

        var saved = Assert.Single(await state.LoadAsync(CancellationToken.None));
        Assert.True(saved.Quarantined);
        Assert.False(saved.Enabled);
        Assert.Equal(PluginLimits.MaximumFailuresBeforeQuarantine, saved.ConsecutiveFailureCount);
    }

    [Fact]
    public async Task Manager_Refresh_DeactivatesRegistrationWhenPluginBecomesDisabled()
    {
        var manifest = PluginManifestTests.Manifest();
        var descriptor = ExternalDescriptor(manifest);
        var initial = new PluginStateEntry(
            manifest.PluginId,
            manifest.PluginVersion,
            true,
            new HashSet<PluginCapability>(),
            new string('a', 64),
            0,
            false,
            null,
            null);
        var state = new PluginDiscoveryTests.MemoryStateStore([initial]);
        var runtime = new TrackingRuntime();
        using var manager = new PluginManager(
            Path.GetTempPath(),
            new StateAwareDiscovery(descriptor, state),
            state,
            new FixedIntegrity(),
            runtime,
            new NullPackageService(),
            new PluginDiagnostics());
        await manager.InitializeAsync(CancellationToken.None);
        await state.SaveAsync([initial with { Enabled = false }], CancellationToken.None);

        var refreshed = await manager.RefreshAsync(CancellationToken.None);

        Assert.Equal(1, runtime.DeactivateCount);
        Assert.Equal(PluginLifecycleState.Disabled, Assert.Single(refreshed).LifecycleState);
    }

    [Fact]
    public async Task Manager_EnableAndDisable_RequireExplicitStateTransition()
    {
        var manifest = PluginManifestTests.Manifest();
        var descriptor = ExternalDescriptor(manifest);
        var state = new PluginDiscoveryTests.MemoryStateStore([]);
        var runtime = new TrackingRuntime();
        using var manager = new PluginManager(
            Path.GetTempPath(),
            new StateAwareDiscovery(descriptor, state),
            state,
            new FixedIntegrity(),
            runtime,
            new NullPackageService(),
            new PluginDiagnostics());
        await manager.InitializeAsync(CancellationToken.None);

        var enabled = await manager.EnableAsync(
            manifest.PluginId,
            manifest.PluginVersion,
            new HashSet<PluginCapability>(),
            CancellationToken.None);
        var disabled = await manager.DisableAsync(
            manifest.PluginId,
            manifest.PluginVersion,
            CancellationToken.None);

        Assert.True(enabled.Succeeded);
        Assert.True(disabled.Succeeded);
        Assert.False((await state.LoadAsync(CancellationToken.None)).Single().Enabled);
        Assert.Equal(1, runtime.DeactivateCount);
    }

    [Fact]
    public async Task Manager_BlockedRemoval_DoesNotDisableActivePlugin()
    {
        var manifest = PluginManifestTests.Manifest();
        var descriptor = ExternalDescriptor(manifest);
        var state = new PluginDiscoveryTests.MemoryStateStore(
        [
            new PluginStateEntry(
                manifest.PluginId,
                manifest.PluginVersion,
                true,
                new HashSet<PluginCapability>(),
                new string('a', 64),
                0,
                false,
                null,
                null),
        ]);
        var runtime = new TrackingRuntime();
        using var manager = new PluginManager(
            Path.GetTempPath(),
            new StateAwareDiscovery(descriptor, state),
            state,
            new FixedIntegrity(),
            runtime,
            new NullPackageService(),
            new PluginDiagnostics(),
            new FixedUsage(new PluginUsage(["profile"], [], [], [])));
        await manager.InitializeAsync(CancellationToken.None);

        var result = await manager.RemoveAsync(
            manifest.PluginId,
            manifest.PluginVersion,
            confirmed: true,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("blocked", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runtime.DeactivateCount);
        Assert.True((await state.LoadAsync(CancellationToken.None)).Single().Enabled);
    }

    [Fact]
    public async Task Manager_OneFailedPlugin_DoesNotBlockUnrelatedPluginActivation()
    {
        var first = Descriptor(ManifestFor(
            "first.plugin",
            "first.metadata",
            ExtensionPointKind.MetadataProvider));
        var second = Descriptor(ManifestFor(
            "second.plugin",
            "second.metadata",
            ExtensionPointKind.MetadataProvider));
        using var manager = new PluginManager(
            Path.GetTempPath(),
            new StaticDiscovery([second, first]),
            new PluginDiscoveryTests.MemoryStateStore([]),
            new FixedIntegrity(),
            new SelectiveRuntime("first.plugin"),
            new NullPackageService(),
            new PluginDiagnostics());

        await manager.InitializeAsync(CancellationToken.None);
        var plugins = await manager.ListAsync(CancellationToken.None);

        Assert.Equal(
            PluginLifecycleState.Failed,
            plugins.Single(plugin => plugin.PluginId == "first.plugin").LifecycleState);
        Assert.Equal(
            PluginLifecycleState.Active,
            plugins.Single(plugin => plugin.PluginId == "second.plugin").LifecycleState);
    }

    private static PluginManifest ManifestFor(
        string pluginId,
        string contributionId,
        ExtensionPointKind kind) =>
        PluginManifestTests.Manifest(pluginId) with
        {
            BuiltIn = true,
            EntryType = "Test.Plugin",
            Contributions =
            [
                new PluginManifestContribution(
                    contributionId,
                    kind,
                    contributionId),
            ],
        };

    private static PluginDescriptor Descriptor(PluginManifest manifest) =>
        new(
            manifest,
            "built-in",
            new PluginProvenance(PluginOriginKind.BuiltIn, "tests", DateTimeOffset.UtcNow),
            PluginLifecycleState.Ready,
            PluginCompatibilityState.Compatible,
            PluginIntegrityStatus.NotApplicable,
            null,
            true,
            true,
            new HashSet<PluginCapability>(manifest.Capabilities),
            [],
            null,
            false);

    private static PluginDescriptor ExternalDescriptor(PluginManifest manifest) =>
        Descriptor(manifest) with
        {
            InstallationPath = Path.GetTempPath(),
            Provenance = new PluginProvenance(
                PluginOriginKind.ControlledFolder,
                "tests",
                DateTimeOffset.UtcNow),
            LifecycleState = PluginLifecycleState.Disabled,
            IsEnabled = false,
            GrantedCapabilities = new HashSet<PluginCapability>(),
            IntegrityStatus = PluginIntegrityStatus.NotCalculated,
        };

    private static async Task<(
        PluginOperationResult Activated,
        int RegisteredCount,
        PluginOperationResult Stopped)> ActivateAndStopExternalAsync(
        PluginDescriptor descriptor,
        IPluginContributionRegistry registry,
        IPluginDiagnostics diagnostics)
    {
        await using var runtime = new PluginRuntime(registry, diagnostics);
        var activated = await runtime.ActivateAsync(descriptor, CancellationToken.None);
        var registeredCount = registry.List().Count;
        var stopped = await runtime.DeactivateAsync(
            descriptor.PluginId,
            descriptor.PluginVersion,
            CancellationToken.None);
        return (activated, registeredCount, stopped);
    }

    private sealed class ThrowingPlugin : IOpenSorSePlugin
    {
        public Task<ExtensionResult<IReadOnlyList<IExtensionContribution>>> InitializeAsync(
            PluginInitializationContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("test");

        public Task<ExtensionResult<bool>> StopAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<bool>.Success(true));
    }

    private sealed class BlockingPlugin : IOpenSorSePlugin
    {
        public async Task<ExtensionResult<IReadOnlyList<IExtensionContribution>>> InitializeAsync(
            PluginInitializationContext context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ExtensionResult<IReadOnlyList<IExtensionContribution>>.Success([]);
        }

        public Task<ExtensionResult<bool>> StopAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<bool>.Success(true));
    }

    private sealed class MetadataContribution(string id) : IMetadataProvider
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = id;
        public int Priority => 0;

        public Task<ExtensionResult<MetadataResponse>> GetMetadataAsync(
            MetadataRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<MetadataResponse>.Success(
                new MetadataResponse([])));
    }

    private sealed class StateAwareDiscovery(
        PluginDescriptor descriptor,
        IPluginStateStore state) : IPluginDiscoveryService
    {
        public async Task<PluginDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
        {
            var current = (await state.LoadAsync(cancellationToken))
                .SingleOrDefault(value =>
                    value.PluginId == descriptor.PluginId &&
                    value.PluginVersion == descriptor.PluginVersion);
            var enabled = current?.Enabled == true && current.Quarantined == false;
            var updated = descriptor with
            {
                IsEnabled = enabled,
                LifecycleState = current?.Quarantined == true
                    ? PluginLifecycleState.Quarantined
                    : enabled
                        ? PluginLifecycleState.Ready
                        : PluginLifecycleState.Disabled,
                GrantedCapabilities = current?.GrantedCapabilities ??
                                      new HashSet<PluginCapability>(),
            };
            return new PluginDiscoveryResult([updated], []);
        }
    }

    private sealed class FixedIntegrity : IPluginIntegrityService
    {
        public Task<string> CalculateAsync(
            string pluginDirectory,
            CancellationToken cancellationToken) =>
            Task.FromResult(new string('a', 64));
    }

    private sealed class FailingRuntime : IPluginRuntime
    {
        public Task<PluginOperationResult> ActivateAsync(
            PluginDescriptor plugin,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PluginOperationResult(
                false,
                "Failed.",
                plugin with { LifecycleState = PluginLifecycleState.Failed }));

        public Task<PluginOperationResult> DeactivateAsync(
            string pluginId,
            string pluginVersion,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PluginOperationResult(true, "Stopped."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose()
        {
        }
    }

    private sealed class TrackingRuntime : IPluginRuntime
    {
        public int DeactivateCount { get; private set; }

        public Task<PluginOperationResult> ActivateAsync(
            PluginDescriptor plugin,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PluginOperationResult(
                true,
                "Active.",
                plugin with { LifecycleState = PluginLifecycleState.Active }));

        public Task<PluginOperationResult> DeactivateAsync(
            string pluginId,
            string pluginVersion,
            CancellationToken cancellationToken)
        {
            DeactivateCount++;
            return Task.FromResult(new PluginOperationResult(true, "Stopped."));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose()
        {
        }
    }

    private sealed class FixedUsage(PluginUsage usage) : IPluginUsageInspector
    {
        public Task<PluginUsage> InspectAsync(
            string pluginId,
            CancellationToken cancellationToken) =>
            Task.FromResult(usage);
    }

    private sealed class StaticDiscovery(
        IReadOnlyList<PluginDescriptor> plugins) : IPluginDiscoveryService
    {
        public Task<PluginDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PluginDiscoveryResult(plugins, []));
    }

    private sealed class SelectiveRuntime(string failingPluginId) : IPluginRuntime
    {
        public Task<PluginOperationResult> ActivateAsync(
            PluginDescriptor plugin,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                string.Equals(plugin.PluginId, failingPluginId, StringComparison.Ordinal)
                    ? new PluginOperationResult(
                        false,
                        "Failed.",
                        plugin with { LifecycleState = PluginLifecycleState.Failed })
                    : new PluginOperationResult(
                        true,
                        "Active.",
                        plugin with { LifecycleState = PluginLifecycleState.Active }));

        public Task<PluginOperationResult> DeactivateAsync(
            string pluginId,
            string pluginVersion,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PluginOperationResult(true, "Stopped."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose()
        {
        }
    }

    private sealed class NullPackageService : IPluginPackageService
    {
        public Task<PluginPackageInspection> InspectAsync(
            string packagePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PluginPackageInspection(false, null, 0, 0, []));

        public Task<PluginOperationResult> InstallAsync(
            string packagePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PluginOperationResult(false, "Unavailable."));

        public Task<PluginOperationResult> UpgradeAsync(
            string packagePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PluginOperationResult(false, "Unavailable."));

        public Task<PluginOperationResult> RemoveAsync(
            string pluginId,
            string pluginVersion,
            bool confirmed,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PluginOperationResult(false, "Unavailable."));
    }
}

public sealed class ExternalLoadTestPlugin : IOpenSorSePlugin
{
    public Task<ExtensionResult<IReadOnlyList<IExtensionContribution>>> InitializeAsync(
        PluginInitializationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<IExtensionContribution> contributions =
            Array.AsReadOnly<IExtensionContribution>([new ExternalLoadTestMetadata()]);
        return Task.FromResult(
            ExtensionResult<IReadOnlyList<IExtensionContribution>>.Success(contributions));
    }

    public Task<ExtensionResult<bool>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ExtensionResult<bool>.Success(true));
}

public sealed class ExternalLoadTestMetadata : IMetadataProvider
{
    public string Id => "external.metadata";
    public string DisplayName => "External metadata";
    public int Priority => 0;

    public Task<ExtensionResult<MetadataResponse>> GetMetadataAsync(
        MetadataRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            ExtensionResult<MetadataResponse>.Success(new MetadataResponse([])));
}

public sealed class PluginExtensionHostTests
{
    [Fact]
    public async Task Host_InvokesAndValidatesEverySupportedExtensionPoint()
    {
        var diagnostics = new PluginDiagnostics();
        var registry = new PluginContributionRegistry(diagnostics);
        var contributions = Contributions();
        var manifest = PluginManifestTests.Manifest("all.extensions") with
        {
            Contributions = contributions.Select(value =>
                new PluginManifestContribution(
                    value.Id,
                    Kind(value),
                    value.DisplayName,
                    value.Priority)).ToArray(),
            Capabilities =
            [
                PluginCapability.ReadFileMetadata,
                PluginCapability.ReadFileContents,
                PluginCapability.ContributeRecipeFields,
                PluginCapability.ContributeWorkflowCapabilities,
                PluginCapability.ImportConfiguration,
                PluginCapability.ExportReports,
            ],
        };
        Assert.True(registry.Register(
            ExternalOwner(manifest),
            contributions).Succeeded);
        var host = new PluginExtensionHost(registry, diagnostics);
        var file = new PluginFileReference(
            "file:1",
            Path.Combine(Path.GetTempPath(), "test.txt"),
            10,
            DateTimeOffset.UtcNow,
            ".txt");

        Assert.True((await host.GetMetadataAsync(
            manifest.PluginId,
            "metadata",
            new MetadataRequest(file, 4, 128),
            CancellationToken.None)).Succeeded);
        Assert.True((await host.ExtractContentAsync(
            manifest.PluginId,
            "content",
            new ContentExtractionRequest(file, 1_000, 100, 4),
            CancellationToken.None)).Succeeded);
        Assert.True((await host.ClassifyAsync(
            manifest.PluginId,
            "classifier",
            new ClassificationRequest(file, null, 2),
            CancellationToken.None)).Succeeded);
        Assert.True((await host.ResolveRecipeFieldAsync(
            manifest.PluginId,
            "recipe",
            new RecipeFieldRequest(file, "plugin.all.extensions.recipe", new Dictionary<string, string>(), null),
            CancellationToken.None)).Succeeded);
        Assert.True((await host.AnalyzeDuplicateAsync(
            manifest.PluginId,
            "duplicate",
            new DuplicateSignalRequest(file, file, new Dictionary<string, string>(), new Dictionary<string, string>()),
            CancellationToken.None)).Succeeded);
        Assert.True((await host.ResolveWorkflowCapabilityAsync(
            manifest.PluginId,
            "workflow",
            new WorkflowCapabilityRequest("workflow", new Dictionary<string, string>()),
            CancellationToken.None)).Succeeded);
        Assert.True((await host.ImportAsync(
            manifest.PluginId,
            "import",
            new ImportRequest("test", ReadOnlyMemory<byte>.Empty, 2),
            CancellationToken.None)).Succeeded);
        Assert.True((await host.ExportAsync(
            manifest.PluginId,
            "export",
            new ExportRequest("test", [], 1_024),
            CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task Host_InvalidOutputAndException_AreContained()
    {
        var diagnostics = new PluginDiagnostics();
        var registry = new PluginContributionRegistry(diagnostics);
        var invalid = new InvalidMetadataContribution();
        var manifest = PluginManifestTests.Manifest("invalid.output") with
        {
            Contributions =
            [
                new PluginManifestContribution(
                    invalid.Id,
                    ExtensionPointKind.MetadataProvider,
                    invalid.DisplayName),
            ],
        };
        Assert.True(registry.Register(ExternalOwner(manifest), [invalid]).Succeeded);
        var host = new PluginExtensionHost(registry, diagnostics);
        var file = new PluginFileReference("file", "C:\\test", 1, DateTimeOffset.UtcNow, ".txt");

        var result = await host.GetMetadataAsync(
            manifest.PluginId,
            invalid.Id,
            new MetadataRequest(file, 1, 8),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("plugin.exception", result.ErrorCode);
    }

    [Fact]
    public async Task Host_Cancellation_ReachesProvider()
    {
        var diagnostics = new PluginDiagnostics();
        var registry = new PluginContributionRegistry(diagnostics);
        var contribution = new CancellingMetadataContribution();
        var manifest = PluginManifestTests.Manifest("cancel.output") with
        {
            Contributions =
            [
                new PluginManifestContribution(
                    contribution.Id,
                    ExtensionPointKind.MetadataProvider,
                    contribution.DisplayName),
            ],
        };
        Assert.True(registry.Register(ExternalOwner(manifest), [contribution]).Succeeded);
        var host = new PluginExtensionHost(registry, diagnostics);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.GetMetadataAsync(
                manifest.PluginId,
                contribution.Id,
                new MetadataRequest(
                    new PluginFileReference("file", "C:\\test", 1, DateTimeOffset.UtcNow, ".txt"),
                    1,
                    8),
                cancellation.Token));
    }

    private static IReadOnlyList<IExtensionContribution> Contributions() =>
    [
        new MetadataContribution("metadata"),
        new ContentContribution(),
        new ClassifierContribution(),
        new RecipeContribution(),
        new DuplicateContribution(),
        new WorkflowContribution(),
        new ImportContribution(),
        new ExportContribution(),
    ];

    private static PluginDescriptor ExternalOwner(PluginManifest manifest) =>
        new(
            manifest,
            Path.GetTempPath(),
            new PluginProvenance(PluginOriginKind.ControlledFolder, "test", DateTimeOffset.UtcNow),
            PluginLifecycleState.Active,
            PluginCompatibilityState.Compatible,
            PluginIntegrityStatus.Verified,
            new string('a', 64),
            true,
            true,
            new HashSet<PluginCapability>(manifest.Capabilities),
            [],
            null,
            false);

    private static ExtensionPointKind Kind(IExtensionContribution value) => value switch
    {
        IMetadataProvider => ExtensionPointKind.MetadataProvider,
        IContentExtractor => ExtensionPointKind.ContentExtractor,
        IFileClassifier => ExtensionPointKind.FileClassifier,
        IRecipeFieldProvider => ExtensionPointKind.RecipeFieldProvider,
        IDuplicateSignalProvider => ExtensionPointKind.DuplicateSignalProvider,
        IWorkflowCapabilityProvider => ExtensionPointKind.WorkflowCapabilityProvider,
        IImportFormatProvider => ExtensionPointKind.ImportFormatProvider,
        IExportFormatProvider => ExtensionPointKind.ExportFormatProvider,
        _ => throw new InvalidOperationException(),
    };

    private abstract class Contribution(string id, string displayName) : IExtensionContribution
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public int Priority => 0;
    }

    private sealed class MetadataContribution(string id = "metadata")
        : Contribution(id, id), IMetadataProvider
    {
        public Task<ExtensionResult<MetadataResponse>> GetMetadataAsync(
            MetadataRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<MetadataResponse>.Success(
                new MetadataResponse(
                [
                    new ExtensionValue(
                        "name",
                        ExtensionValueKind.Text,
                        "value",
                        ExtensionDerivationKind.Deterministic,
                        "test"),
                ])));
    }

    private sealed class ContentContribution() : Contribution("content", "content"), IContentExtractor
    {
        public Task<ExtensionResult<ContentExtractionResponse>> ExtractAsync(
            ContentExtractionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<ContentExtractionResponse>.Success(
                new ContentExtractionResponse("text", [], false)));
    }

    private sealed class ClassifierContribution() : Contribution("classifier", "classifier"), IFileClassifier
    {
        public Task<ExtensionResult<ClassificationResponse>> ClassifyAsync(
            ClassificationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<ClassificationResponse>.Success(
                new ClassificationResponse(
                [
                    new ClassificationLabel(
                        "document",
                        1,
                        "test",
                        ExtensionDerivationKind.Deterministic),
                ])));
    }

    private sealed class RecipeContribution() : Contribution("recipe", "recipe"), IRecipeFieldProvider
    {
        public Task<ExtensionResult<RecipeFieldResponse>> ResolveFieldAsync(
            RecipeFieldRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<RecipeFieldResponse>.Success(
                new RecipeFieldResponse(
                    new ExtensionValue(
                        request.FieldName,
                        ExtensionValueKind.Text,
                        "field",
                        ExtensionDerivationKind.Deterministic,
                        "test"))));
    }

    private sealed class DuplicateContribution() : Contribution("duplicate", "duplicate"), IDuplicateSignalProvider
    {
        public Task<ExtensionResult<DuplicateSignalResponse>> AnalyzeAsync(
            DuplicateSignalRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<DuplicateSignalResponse>.Success(
                new DuplicateSignalResponse(
                    0.5,
                    "test",
                    "test",
                    ExtensionDerivationKind.Deterministic)));
    }

    private sealed class WorkflowContribution() : Contribution("workflow", "workflow"), IWorkflowCapabilityProvider
    {
        public Task<ExtensionResult<WorkflowCapabilityResponse>> ResolveAsync(
            WorkflowCapabilityRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<WorkflowCapabilityResponse>.Success(
                new WorkflowCapabilityResponse(
                    true,
                    new Dictionary<string, string> { ["value"] = "test" },
                    ExtensionDerivationKind.Deterministic,
                    "test")));
    }

    private sealed class ImportContribution() : Contribution("import", "import"), IImportFormatProvider
    {
        public Task<ExtensionResult<ImportResponse>> ImportAsync(
            ImportRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<ImportResponse>.Success(
                new ImportResponse(
                [
                    new ImportProposal(
                        "test",
                        new Dictionary<string, string> { ["value"] = "test" },
                        "test"),
                ])));
    }

    private sealed class ExportContribution() : Contribution("export", "export"), IExportFormatProvider
    {
        public Task<ExtensionResult<ExportResponse>> ExportAsync(
            ExportRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(ExtensionResult<ExportResponse>.Success(
                new ExportResponse(
                    "test.json",
                    "application/json",
                    "{}"u8.ToArray())));
    }

    private sealed class InvalidMetadataContribution()
        : Contribution("invalid", "invalid"), IMetadataProvider
    {
        public Task<ExtensionResult<MetadataResponse>> GetMetadataAsync(
            MetadataRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("untrusted failure");
    }

    private sealed class CancellingMetadataContribution()
        : Contribution("cancel", "cancel"), IMetadataProvider
    {
        public async Task<ExtensionResult<MetadataResponse>> GetMetadataAsync(
            MetadataRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ExtensionResult<MetadataResponse>.Success(new MetadataResponse([]));
        }
    }
}
