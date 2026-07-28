#pragma warning disable CS1591

using System.Reflection;
using System.Runtime.Loader;
using OpenSorSe.Core;
using OpenSorSe.Extensions.Abstractions;

namespace OpenSorSe.Application.Plugins;

/// <summary>
/// Activates and deactivates exact plugin versions behind bounded lifecycle and registry boundaries.
/// </summary>
/// <remarks>
/// Built-ins are instantiated by the host. Each external plugin receives one
/// collectible <see cref="AssemblyLoadContext"/> whose managed/native resolver
/// is confined to its installed root; the SDK assembly is shared with the
/// default context. Initialization and stop use linked timeouts/cancellation,
/// exceptions are contained, and partial registration is removed on failure.
/// Load-context isolation is not an operating-system sandbox.
/// </remarks>
public sealed class PluginRuntime : IPluginRuntime
{
    private readonly object _sync = new();
    private readonly IPluginContributionRegistry _registry;
    private readonly IPluginDiagnostics _diagnostics;
    private readonly IReadOnlyDictionary<string, BuiltInPluginDefinition> _builtIns;
    private readonly TimeSpan _initializationTimeout;
    private readonly Dictionary<(string PluginId, string Version), LoadedPlugin> _loaded = [];
    private bool _disposed;

    public PluginRuntime(
        IPluginContributionRegistry registry,
        IPluginDiagnostics diagnostics,
        IEnumerable<BuiltInPluginDefinition>? builtIns = null,
        TimeSpan? initializationTimeout = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _builtIns = (builtIns ?? []).ToDictionary(
            definition => Key(definition.Manifest.PluginId, definition.Manifest.PluginVersion),
            StringComparer.Ordinal);
        _initializationTimeout = initializationTimeout ?? PluginLimits.InitializationTimeout;
        if (_initializationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initializationTimeout));
        }
    }

    public async Task<PluginOperationResult> ActivateAsync(
        PluginDescriptor plugin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (plugin.Manifest is null ||
            !plugin.IsEnabled ||
            plugin.LifecycleState != PluginLifecycleState.Ready)
        {
            return new PluginOperationResult(false, "Only a validated, compatible, enabled, ready plugin can be activated.", plugin);
        }

        var key = (plugin.PluginId, plugin.PluginVersion);
        lock (_sync)
        {
            if (_loaded.ContainsKey(key))
            {
                return new PluginOperationResult(
                    true,
                    "The plugin is already active.",
                    plugin with { LifecycleState = PluginLifecycleState.Active });
            }
        }

        _diagnostics.Record(
            PluginDiagnosticKind.Loading,
            plugin.PluginId,
            "Loading the validated plugin entry point.");
        PluginLoadContext? loadContext = null;
        IOpenSorSePlugin instance;
        try
        {
            if (plugin.IsBuiltIn)
            {
                if (!_builtIns.TryGetValue(Key(plugin.PluginId, plugin.PluginVersion), out var definition))
                {
                    return new PluginOperationResult(false, "The built-in plugin factory is unavailable.", plugin);
                }

                instance = definition.Factory();
            }
            else
            {
                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plugin.InstallationPath));
                var entryPath = Path.GetFullPath(Path.Combine(root, plugin.Manifest.EntryAssembly));
                if (!IsWithin(root, entryPath) || !File.Exists(entryPath))
                {
                    return new PluginOperationResult(false, "The validated plugin entry assembly is unavailable.", plugin);
                }

                loadContext = new PluginLoadContext(entryPath);
                var assembly = loadContext.LoadFromAssemblyPath(entryPath);
                var entryType = assembly.GetType(
                    plugin.Manifest.EntryType,
                    throwOnError: false,
                    ignoreCase: false);
                if (entryType is null ||
                    entryType.IsAbstract ||
                    !typeof(IOpenSorSePlugin).IsAssignableFrom(entryType) ||
                    entryType.GetConstructor(Type.EmptyTypes) is null)
                {
                    loadContext.Unload();
                    return new PluginOperationResult(
                        false,
                        "The plugin entry type is missing, incompatible, abstract, or lacks a public parameterless constructor.",
                        plugin);
                }

                instance = (IOpenSorSePlugin)Activator.CreateInstance(entryType)!;
            }
        }
        catch (Exception exception)
        {
            loadContext?.Unload();
            _diagnostics.Record(
                PluginDiagnosticKind.Loading,
                plugin.PluginId,
                "The plugin assembly or entry point could not be loaded safely.",
                exception.GetType().Name);
            return new PluginOperationResult(
                false,
                "The plugin assembly or entry point could not be loaded safely.",
                plugin with
                {
                    LifecycleState = PluginLifecycleState.Failed,
                    LastError = "Assembly or entry-point loading failed.",
                });
        }

        ExtensionResult<IReadOnlyList<IExtensionContribution>> initialized;
        using var initializationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        initializationCancellation.CancelAfter(_initializationTimeout);
        try
        {
            var context = new PluginInitializationContext(
                new PluginIdentity(
                    plugin.PluginId,
                    plugin.PluginVersion,
                    plugin.DisplayName,
                    plugin.IsBuiltIn),
                new HashSet<PluginCapability>(plugin.GrantedCapabilities),
                ApplicationVersionInfo.Current);
            initialized = await instance.InitializeAsync(context, initializationCancellation.Token)
                .WaitAsync(_initializationTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            initializationCancellation.Cancel();
            loadContext?.Unload();
            _diagnostics.Record(
                PluginDiagnosticKind.Timeout,
                plugin.PluginId,
                "Plugin initialization exceeded the bounded timeout.",
                "plugin.initialization-timeout");
            return new PluginOperationResult(
                false,
                "Plugin initialization timed out.",
                plugin with
                {
                    LifecycleState = PluginLifecycleState.Failed,
                    LastError = "Initialization timed out.",
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            loadContext?.Unload();
            _diagnostics.Record(
                PluginDiagnosticKind.Cancellation,
                plugin.PluginId,
                "Plugin initialization was cancelled.");
            throw;
        }
        catch (OperationCanceledException)
        {
            loadContext?.Unload();
            _diagnostics.Record(
                PluginDiagnosticKind.Timeout,
                plugin.PluginId,
                "Plugin initialization exceeded the bounded timeout.",
                "plugin.initialization-timeout");
            return new PluginOperationResult(
                false,
                "Plugin initialization timed out.",
                plugin with
                {
                    LifecycleState = PluginLifecycleState.Failed,
                    LastError = "Initialization timed out.",
                });
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or BadImageFormatException)
        {
            loadContext?.Unload();
            _diagnostics.Record(
                PluginDiagnosticKind.Initialization,
                plugin.PluginId,
                "Plugin initialization failed because a native dependency was unavailable or incompatible.",
                exception.GetType().Name);
            return new PluginOperationResult(
                false,
                "Plugin initialization failed because a native dependency was unavailable or incompatible.",
                plugin with
                {
                    LifecycleState = PluginLifecycleState.Failed,
                    LastError = "A native dependency was unavailable or incompatible.",
                });
        }
        catch (Exception exception)
        {
            loadContext?.Unload();
            _diagnostics.Record(
                PluginDiagnosticKind.Initialization,
                plugin.PluginId,
                "Plugin initialization failed without preventing host startup.",
                exception.GetType().Name);
            return new PluginOperationResult(
                false,
                "Plugin initialization failed safely.",
                plugin with
                {
                    LifecycleState = PluginLifecycleState.Failed,
                    LastError = "Initialization failed.",
                });
        }

        if (!initialized.Succeeded || initialized.Value is null)
        {
            loadContext?.Unload();
            const string message = "Plugin initialization returned a controlled failure.";
            _diagnostics.Record(
                PluginDiagnosticKind.Initialization,
                plugin.PluginId,
                message,
                initialized.ErrorCode);
            return new PluginOperationResult(
                false,
                message,
                plugin with
                {
                    LifecycleState = PluginLifecycleState.Failed,
                    LastError = message,
                });
        }

        PluginOperationResult registration;
        try
        {
            registration = _registry.Register(plugin, initialized.Value);
        }
        catch (Exception exception)
        {
            registration = new PluginOperationResult(
                false,
                "Contribution registration failed safely.",
                plugin);
            _diagnostics.Record(
                PluginDiagnosticKind.ContributionRegistration,
                plugin.PluginId,
                registration.Message,
                exception.GetType().Name);
        }

        if (!registration.Succeeded)
        {
            try
            {
                await instance.StopAsync(CancellationToken.None)
                    .WaitAsync(_initializationTimeout)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Registration remains rejected; cleanup failure is separately non-authoritative.
            }

            loadContext?.Unload();
            return registration with
            {
                Plugin = plugin with
                {
                    LifecycleState = PluginLifecycleState.Failed,
                    LastError = registration.Message,
                },
            };
        }

        lock (_sync)
        {
            _loaded.Add(key, new LoadedPlugin(instance, loadContext));
        }

        var active = plugin with
        {
            LifecycleState = PluginLifecycleState.Active,
            LastError = null,
        };
        _diagnostics.Record(
            PluginDiagnosticKind.Activation,
            plugin.PluginId,
            "Plugin activated with validated contribution registrations.");
        return new PluginOperationResult(true, "Plugin activated.", active);
    }

    public async Task<PluginOperationResult> DeactivateAsync(
        string pluginId,
        string pluginVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginVersion);
        LoadedPlugin? loaded;
        lock (_sync)
        {
            _loaded.Remove((pluginId, pluginVersion), out loaded);
        }

        if (loaded is null)
        {
            _registry.RemovePlugin(pluginId, pluginVersion);
            return new PluginOperationResult(true, "The plugin was not active.");
        }

        try
        {
            await loaded.Instance.StopAsync(cancellationToken)
                .WaitAsync(_initializationTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                _loaded[(pluginId, pluginVersion)] = loaded;
            }

            throw;
        }
        catch (Exception exception)
        {
            _diagnostics.Record(
                PluginDiagnosticKind.Deactivation,
                pluginId,
                "The plugin stop callback failed; host registrations were still removed.",
                exception.GetType().Name);
        }

        _registry.RemovePlugin(pluginId, pluginVersion);
        loaded.LoadContext?.Unload();
        var requiresRestart = loaded.LoadContext is not null;
        _diagnostics.Record(
            PluginDiagnosticKind.Deactivation,
            pluginId,
            requiresRestart
                ? "External plugin stopped. Restart may be required to release all in-process assembly references."
                : "Built-in plugin stopped.");
        return new PluginOperationResult(
            true,
            requiresRestart
                ? "Plugin stopped. Restart may be required to complete unload."
                : "Plugin stopped.",
            null,
            requiresRestart ? ["In-process .NET assembly unloading is cooperative, not a security boundary."] : []);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        (string PluginId, string Version)[] keys;
        lock (_sync)
        {
            keys = _loaded.Keys.ToArray();
        }

        foreach (var key in keys)
        {
            try
            {
                await DeactivateAsync(
                    key.PluginId,
                    key.Version,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Application shutdown continues even when one plugin cannot stop cleanly.
            }
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static bool IsWithin(string root, string candidate)
    {
        var comparison = OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparison;
        return string.Equals(root, candidate, comparison) ||
               candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static string Key(string pluginId, string version) =>
        $"{pluginId}\0{version}";

    private sealed record LoadedPlugin(
        IOpenSorSePlugin Instance,
        PluginLoadContext? LoadContext);

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _pluginRoot;

        public PluginLoadContext(string entryAssemblyPath)
            : base($"OpenSorSe.Plugin.{Path.GetFileNameWithoutExtension(entryAssemblyPath)}.{Guid.NewGuid():N}", isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
            _pluginRoot = Path.TrimEndingDirectorySeparator(
                Path.GetDirectoryName(Path.GetFullPath(entryAssemblyPath))
                ?? throw new InvalidOperationException("The plugin entry assembly must have a parent directory."));
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var contracts = typeof(IOpenSorSePlugin).Assembly.GetName();
            if (AssemblyName.ReferenceMatchesDefinition(assemblyName, contracts))
            {
                return typeof(IOpenSorSePlugin).Assembly;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null || !IsWithin(_pluginRoot, Path.GetFullPath(path))
                ? null
                : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null || !IsWithin(_pluginRoot, Path.GetFullPath(path))
                ? IntPtr.Zero
                : LoadUnmanagedDllFromPath(path);
        }
    }
}

/// <summary>
/// Serializes discovery, state transitions, activation, package operations, rollback, and shutdown.
/// </summary>
/// <remarks>
/// External plugins require explicit enable and selective grants. Refresh
/// deactivates contributions that became disabled, changed, missing, blocked,
/// or quarantined. Upgrade validates and installs first, retains the old
/// version, and restores it if activation of the new version fails. Removal is
/// preflighted before deactivation so a blocked request leaves the plugin
/// active. State failure is contained from unrelated application startup.
/// </remarks>
public sealed class PluginManager : IPluginManager
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IPluginDiscoveryService _discovery;
    private readonly IPluginStateStore _stateStore;
    private readonly IPluginIntegrityService _integrity;
    private readonly IPluginRuntime _runtime;
    private readonly IPluginPackageService _packages;
    private readonly IPluginDiagnostics _diagnostics;
    private readonly IPluginUsageInspector _usage;
    private IReadOnlyList<PluginDescriptor> _plugins = [];
    private bool _initialized;
    private bool _disposed;

    public PluginManager(
        string pluginRoot,
        IPluginDiscoveryService discovery,
        IPluginStateStore stateStore,
        IPluginIntegrityService integrity,
        IPluginRuntime runtime,
        IPluginPackageService packages,
        IPluginDiagnostics diagnostics,
        IPluginUsageInspector? usage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRoot);
        PluginRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pluginRoot));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _integrity = integrity ?? throw new ArgumentNullException(nameof(integrity));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _packages = packages ?? throw new ArgumentNullException(nameof(packages));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _usage = usage ?? new NullPluginUsageInspector();
    }

    public string PluginRoot { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            _plugins = (await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false)).Plugins;
            await ActivateReadyAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PluginDescriptor>> RefreshAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var discovered = (await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false)).Plugins;
            foreach (var active in _plugins.Where(plugin =>
                         plugin.LifecycleState == PluginLifecycleState.Active))
            {
                var current = discovered.SingleOrDefault(plugin =>
                    string.Equals(plugin.PluginId, active.PluginId, StringComparison.Ordinal) &&
                    string.Equals(plugin.PluginVersion, active.PluginVersion, StringComparison.Ordinal));
                if (current is not null &&
                    current.IsSelectedVersion &&
                    current.IsEnabled &&
                    current.LifecycleState == PluginLifecycleState.Ready)
                {
                    continue;
                }

                await _runtime.DeactivateAsync(
                    active.PluginId,
                    active.PluginVersion,
                    cancellationToken).ConfigureAwait(false);
            }

            _plugins = discovered;
            if (_initialized)
            {
                await ActivateReadyAsync(cancellationToken).ConfigureAwait(false);
            }

            return Clone(_plugins);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PluginDescriptor>> ListAsync(
        CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Clone(_plugins);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginOperationResult> EnableAsync(
        string pluginId,
        string pluginVersion,
        IReadOnlySet<PluginCapability> grantedCapabilities,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginVersion);
        ArgumentNullException.ThrowIfNull(grantedCapabilities);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plugin = Find(pluginId, pluginVersion);
            if (plugin?.Manifest is null)
            {
                return new PluginOperationResult(false, "The plugin is not installed or has an invalid manifest.");
            }

            if (plugin.IsBuiltIn)
            {
                return new PluginOperationResult(true, "Built-in reference plugins are active by default.", plugin);
            }

            if (plugin.Compatibility != PluginCompatibilityState.Compatible ||
                plugin.LifecycleState == PluginLifecycleState.Invalid)
            {
                return new PluginOperationResult(false, "The plugin is invalid or incompatible.", plugin);
            }

            if (!grantedCapabilities.All(plugin.Manifest.Capabilities.Contains))
            {
                return new PluginOperationResult(
                    false,
                    "Granted capabilities must be a subset of the validated manifest request.",
                    plugin);
            }

            string hash;
            try
            {
                hash = await _integrity.CalculateAsync(
                    plugin.InstallationPath,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return new PluginOperationResult(
                    false,
                    "Plugin integrity could not be calculated; the plugin remains disabled.",
                    plugin with
                    {
                        IntegrityStatus = PluginIntegrityStatus.Failed,
                        LastError = "Integrity calculation failed.",
                    });
            }

            var states = (await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
            UpsertState(
                states,
                new PluginStateEntry(
                    pluginId,
                    pluginVersion,
                    true,
                    new HashSet<PluginCapability>(grantedCapabilities),
                    hash,
                    0,
                    false,
                    DateTimeOffset.UtcNow,
                    null));
            await _stateStore.SaveAsync(
                Array.AsReadOnly(states.ToArray()),
                cancellationToken).ConfigureAwait(false);
            _plugins = (await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false)).Plugins;
            plugin = Find(pluginId, pluginVersion);
            if (plugin is null || plugin.LifecycleState != PluginLifecycleState.Ready)
            {
                return new PluginOperationResult(
                    false,
                    plugin?.DependencyErrors.FirstOrDefault() ??
                    "The plugin could not become ready after review.",
                    plugin);
            }

            var activation = await _runtime.ActivateAsync(plugin, cancellationToken).ConfigureAwait(false);
            ApplyRuntimeResult(pluginId, pluginVersion, activation);
            if (!activation.Succeeded)
            {
                await RecordFailureAsync(plugin, activation.Message, cancellationToken).ConfigureAwait(false);
            }

            return activation;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginOperationResult> DisableAsync(
        string pluginId,
        string pluginVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginVersion);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plugin = Find(pluginId, pluginVersion);
            if (plugin is null)
            {
                return new PluginOperationResult(false, "The plugin is not installed.");
            }

            if (plugin.IsBuiltIn)
            {
                return new PluginOperationResult(
                    false,
                    "Built-in reference plugins cannot be disabled in v1.4.",
                    plugin);
            }

            var stopped = await _runtime.DeactivateAsync(
                pluginId,
                pluginVersion,
                cancellationToken).ConfigureAwait(false);
            var states = (await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var previous = states.SingleOrDefault(entry =>
                string.Equals(entry.PluginId, pluginId, StringComparison.Ordinal) &&
                string.Equals(entry.PluginVersion, pluginVersion, StringComparison.Ordinal));
            UpsertState(
                states,
                previous is null
                    ? new PluginStateEntry(
                        pluginId,
                        pluginVersion,
                        false,
                        new HashSet<PluginCapability>(),
                        plugin.CalculatedIntegrityHash,
                        0,
                        false,
                        null,
                        null)
                    : previous with { Enabled = false });
            await _stateStore.SaveAsync(
                Array.AsReadOnly(states.ToArray()),
                cancellationToken).ConfigureAwait(false);
            _plugins = (await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false)).Plugins;
            var disabled = Find(pluginId, pluginVersion);
            _diagnostics.Record(
                PluginDiagnosticKind.Deactivation,
                pluginId,
                "External plugin disabled by explicit user action.");
            return stopped with
            {
                Message = "Plugin disabled. " + stopped.Message,
                Plugin = disabled is null
                    ? null
                    : disabled with
                    {
                        RestartRequired = stopped.SafeWarnings.Count > 0,
                        LifecycleState = stopped.SafeWarnings.Count > 0
                            ? PluginLifecycleState.RestartRequired
                            : PluginLifecycleState.Disabled,
                    },
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PluginOperationResult> InstallAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var result = await _packages.InstallAsync(packagePath, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<PluginOperationResult> UpgradeAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        var inspection = await _packages.InspectAsync(packagePath, cancellationToken).ConfigureAwait(false);
        if (!inspection.IsValid || inspection.Manifest is null)
        {
            return new PluginOperationResult(false, string.Join(" ", inspection.Issues));
        }

        var previous = (await ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(plugin =>
                string.Equals(
                    plugin.PluginId,
                    inspection.Manifest.PluginId,
                    StringComparison.Ordinal) &&
                plugin.IsEnabled)
            .OrderByDescending(plugin => ParseVersion(plugin.PluginVersion))
            .FirstOrDefault();
        var result = await _packages.UpgradeAsync(packagePath, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return result;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (previous is not null)
        {
            var previousCapabilities =
                new HashSet<PluginCapability>(previous.GrantedCapabilities);
            var newPlugin = (await ListAsync(cancellationToken).ConfigureAwait(false))
                .Single(plugin =>
                    string.Equals(plugin.PluginId, inspection.Manifest.PluginId, StringComparison.Ordinal) &&
                    string.Equals(plugin.PluginVersion, inspection.Manifest.PluginVersion, StringComparison.Ordinal));
            var retainedCapabilities = previousCapabilities
                .Where(newPlugin.Manifest!.Capabilities.Contains)
                .ToHashSet();
            var disabled = await DisableAsync(
                previous.PluginId,
                previous.PluginVersion,
                cancellationToken).ConfigureAwait(false);
            if (!disabled.Succeeded)
            {
                return disabled with
                {
                    Message = "The upgrade was installed but the active version could not be stopped; restart and review the plugin.",
                };
            }

            var enabled = await EnableAsync(
                inspection.Manifest.PluginId,
                inspection.Manifest.PluginVersion,
                retainedCapabilities,
                cancellationToken).ConfigureAwait(false);
            if (enabled.Succeeded)
            {
                return enabled with { Message = "Plugin upgrade installed, compatible reviewed capabilities retained, and new version activated." };
            }

            if ((await ListAsync(cancellationToken).ConfigureAwait(false))
                .Any(plugin =>
                    string.Equals(plugin.PluginId, inspection.Manifest.PluginId, StringComparison.Ordinal) &&
                    string.Equals(plugin.PluginVersion, inspection.Manifest.PluginVersion, StringComparison.Ordinal) &&
                    plugin.IsEnabled))
            {
                await DisableAsync(
                    inspection.Manifest.PluginId,
                    inspection.Manifest.PluginVersion,
                    cancellationToken).ConfigureAwait(false);
            }

            var rollback = await EnableAsync(
                previous.PluginId,
                previous.PluginVersion,
                previousCapabilities,
                cancellationToken).ConfigureAwait(false);
            _diagnostics.Record(
                PluginDiagnosticKind.Rollback,
                previous.PluginId,
                rollback.Succeeded
                    ? $"Upgrade activation failed; restored active version {previous.PluginVersion}."
                    : $"Upgrade activation failed and version {previous.PluginVersion} requires manual re-enable.");
            return enabled with
            {
                Message = rollback.Succeeded
                    ? "The upgrade was installed but activation failed; the previous version was restored."
                    : "The upgrade was installed but activation and automatic rollback both failed; review plugin diagnostics.",
            };
        }

        return result with { Message = "Plugin upgrade installed and awaits explicit enablement." };
    }

    public async Task<PluginOperationResult> RemoveAsync(
        string pluginId,
        string pluginVersion,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        if (!confirmed)
        {
            return new PluginOperationResult(
                false,
                "Removal requires explicit confirmation. User documents and historical records are never removed.");
        }

        var usage = await _usage.InspectAsync(pluginId, cancellationToken).ConfigureAwait(false);
        if (usage.HasActiveDependencies)
        {
            return new PluginOperationResult(
                false,
                $"Removal is blocked because active dependencies remain: {usage.WorkflowProfileIds.Count} profile(s), {usage.SortingRecipeIds.Count} recipe(s), and {usage.WatchedFolderIds.Count} watched folder(s).");
        }

        var plugin = (await ListAsync(cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(value =>
                string.Equals(value.PluginId, pluginId, StringComparison.Ordinal) &&
                string.Equals(value.PluginVersion, pluginVersion, StringComparison.Ordinal));
        var wasEnabled = plugin?.IsEnabled == true && !plugin.IsBuiltIn;
        var previousCapabilities = plugin is null
            ? new HashSet<PluginCapability>()
            : new HashSet<PluginCapability>(plugin.GrantedCapabilities);
        if (wasEnabled)
        {
            await DisableAsync(pluginId, pluginVersion, cancellationToken).ConfigureAwait(false);
        }

        var result = await _packages.RemoveAsync(
            pluginId,
            pluginVersion,
            confirmed,
            cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            var states = (await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false))
                .Where(entry =>
                    !string.Equals(entry.PluginId, pluginId, StringComparison.Ordinal) ||
                    !string.Equals(entry.PluginVersion, pluginVersion, StringComparison.Ordinal))
                .ToArray();
            await _stateStore.SaveAsync(Array.AsReadOnly(states), cancellationToken).ConfigureAwait(false);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (wasEnabled)
        {
            var rollback = await EnableAsync(
                pluginId,
                pluginVersion,
                previousCapabilities,
                cancellationToken).ConfigureAwait(false);
            if (!rollback.Succeeded)
            {
                return result with
                {
                    Message = result.Message + " The previous enabled state could not be restored automatically.",
                };
            }
        }

        return result;
    }

    public string ExportDiagnostics() => _diagnostics.Export();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _runtime.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task ActivateReadyAsync(CancellationToken cancellationToken)
    {
        foreach (var plugin in PluginDependencyResolver.ActivationOrder(_plugins))
        {
            var result = await _runtime.ActivateAsync(plugin, cancellationToken).ConfigureAwait(false);
            ApplyRuntimeResult(plugin.PluginId, plugin.PluginVersion, result);
            if (!result.Succeeded && !plugin.IsBuiltIn)
            {
                await RecordFailureAsync(plugin, result.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RecordFailureAsync(
        PluginDescriptor plugin,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var states = (await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var current = states.SingleOrDefault(entry =>
                string.Equals(entry.PluginId, plugin.PluginId, StringComparison.Ordinal) &&
                string.Equals(entry.PluginVersion, plugin.PluginVersion, StringComparison.Ordinal));
            if (current is null)
            {
                return;
            }

            var count = Math.Min(1_000, current.ConsecutiveFailureCount + 1);
            var quarantine = count >= PluginLimits.MaximumFailuresBeforeQuarantine;
            UpsertState(
                states,
                current with
                {
                    Enabled = !quarantine,
                    ConsecutiveFailureCount = count,
                    Quarantined = quarantine,
                    LastError = SafeError(message),
                });
            await _stateStore.SaveAsync(
                Array.AsReadOnly(states.ToArray()),
                cancellationToken).ConfigureAwait(false);
            if (quarantine)
            {
                _diagnostics.Record(
                    PluginDiagnosticKind.Quarantine,
                    plugin.PluginId,
                    "The plugin was quarantined after repeated failures.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics.Record(
                PluginDiagnosticKind.Quarantine,
                plugin.PluginId,
                "Plugin failure state could not be persisted; unrelated plugin startup continued.",
                exception.GetType().Name);
        }
    }

    private void ApplyRuntimeResult(
        string pluginId,
        string pluginVersion,
        PluginOperationResult result)
    {
        var updated = _plugins.ToArray();
        for (var index = 0; index < updated.Length; index++)
        {
            if (string.Equals(updated[index].PluginId, pluginId, StringComparison.Ordinal) &&
                string.Equals(updated[index].PluginVersion, pluginVersion, StringComparison.Ordinal))
            {
                updated[index] = result.Plugin ?? updated[index] with
                {
                    LifecycleState = result.Succeeded
                        ? PluginLifecycleState.Active
                        : PluginLifecycleState.Failed,
                    LastError = result.Succeeded ? null : result.Message,
                };
            }
        }

        _plugins = Array.AsReadOnly(updated);
    }

    private PluginDescriptor? Find(string pluginId, string pluginVersion) =>
        _plugins.SingleOrDefault(plugin =>
            string.Equals(plugin.PluginId, pluginId, StringComparison.Ordinal) &&
            string.Equals(plugin.PluginVersion, pluginVersion, StringComparison.Ordinal));

    private static void UpsertState(
        List<PluginStateEntry> states,
        PluginStateEntry entry)
    {
        states.RemoveAll(value =>
            string.Equals(value.PluginId, entry.PluginId, StringComparison.Ordinal) &&
            string.Equals(value.PluginVersion, entry.PluginVersion, StringComparison.Ordinal));
        states.Add(entry);
        states.Sort((first, second) =>
        {
            var id = string.Compare(first.PluginId, second.PluginId, StringComparison.Ordinal);
            return id != 0
                ? id
                : string.Compare(first.PluginVersion, second.PluginVersion, StringComparison.Ordinal);
        });
    }

    private static IReadOnlyList<PluginDescriptor> Clone(
        IReadOnlyList<PluginDescriptor> plugins) =>
        Array.AsReadOnly(plugins.Select(plugin => plugin with
        {
            GrantedCapabilities = new HashSet<PluginCapability>(plugin.GrantedCapabilities),
            DependencyErrors = Array.AsReadOnly(plugin.DependencyErrors.ToArray()),
        }).ToArray());

    private static string SafeError(string message) =>
        new(message.Where(character => !char.IsControl(character)).Take(2_048).ToArray());

    private static Version ParseVersion(string version)
    {
        _ = PluginManifestParser.TryVersion(version, out var parsed);
        return parsed;
    }
}
