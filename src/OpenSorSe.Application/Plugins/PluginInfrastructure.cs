#pragma warning disable CS1591

using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSorSe.Core.Persistence;
using OpenSorSe.Extensions.Abstractions;

namespace OpenSorSe.Application.Plugins;

/// <summary>
/// Retains a bounded, sanitized process-lifetime record of plugin host decisions.
/// </summary>
/// <remarks>
/// Diagnostics are best effort and must not contain file content, credentials,
/// exception stacks, or unbounded plugin text. Export returns a snapshot; it
/// does not write a file.
/// </remarks>
public sealed class PluginDiagnostics : IPluginDiagnostics
{
    private readonly object _sync = new();
    private readonly List<PluginDiagnostic> _entries = [];
    private readonly TimeProvider _timeProvider;

    public PluginDiagnostics(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Record(
        PluginDiagnosticKind kind,
        string pluginId,
        string summary,
        string? errorCode = null)
    {
        var safeId = Sanitize(pluginId, PluginLimits.MaximumIdentifierCharacters);
        var safeSummary = Sanitize(summary, 2_048);
        var safeCode = errorCode is null ? null : Sanitize(errorCode, 128);
        lock (_sync)
        {
            _entries.Add(new PluginDiagnostic(
                _timeProvider.GetUtcNow().ToUniversalTime(),
                kind,
                safeId,
                safeSummary,
                safeCode));
            if (_entries.Count > PluginLimits.MaximumDiagnostics)
            {
                _entries.RemoveRange(0, _entries.Count - PluginLimits.MaximumDiagnostics);
            }
        }
    }

    public IReadOnlyList<PluginDiagnostic> List()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_entries.ToArray());
        }
    }

    public string Export()
    {
        var builder = new StringBuilder();
        builder.AppendLine("OmniSorSe v1.4 plugin diagnostics");
        builder.AppendLine("Plugin file contents, extracted text, credentials, tokens, and secrets are excluded.");
        foreach (var entry in List())
        {
            builder.Append(entry.TimestampUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" | ")
                .Append(entry.Kind)
                .Append(" | ")
                .Append(entry.PluginId)
                .Append(" | ")
                .Append(entry.Summary);
            if (entry.ErrorCode is not null)
            {
                builder.Append(" | ").Append(entry.ErrorCode);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Sanitize(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var safe = new string(value
            .Where(character => !char.IsControl(character) || character is '\t')
            .Take(maximum)
            .ToArray());
        return safe
            .Replace("authorization", "[redacted-header]", StringComparison.OrdinalIgnoreCase)
            .Replace("bearer ", "[redacted-token] ", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Persists enabled versions, effective grants, integrity baselines, failure counts, and quarantine state.
/// </summary>
/// <remarks>
/// The schema is host-owned and bounded. Writes use a unique sibling followed
/// by replacement of the exact state file. Store failure can disable external
/// plugin activation, but must not prevent unrelated application startup.
/// </remarks>
public sealed class JsonPluginStateStore : IPluginStateStore
{
    private const int CurrentSchemaVersion = 1;
    private const long MaximumBytes = 4L * 1024 * 1024;
    private readonly string _path;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) },
    };

    public JsonPluginStateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _fileAccess = new ApplicationFileAccessCoordinator(_path);
    }

    public async Task<IReadOnlyList<PluginStateEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var info = new FileInfo(_path);
            if (info.Length <= 0 || info.Length > MaximumBytes)
            {
                return [];
            }

            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var envelope = await JsonSerializer.DeserializeAsync<PluginStateEnvelope>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            if (envelope is null ||
                envelope.SchemaVersion != CurrentSchemaVersion ||
                envelope.Entries is null ||
                envelope.Entries.Count > PluginLimits.MaximumPlugins)
            {
                return [];
            }

            return Array.AsReadOnly(envelope.Entries
                .Where(IsValid)
                .GroupBy(entry => (entry.PluginId, entry.PluginVersion))
                .Select(group => Clone(group.Last()))
                .OrderBy(entry => entry.PluginId, StringComparer.Ordinal)
                .ThenBy(entry => entry.PluginVersion, StringComparer.Ordinal)
                .ToArray());
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<PluginStateEntry> entries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count > PluginLimits.MaximumPlugins || entries.Any(entry => !IsValid(entry)))
        {
            throw new ArgumentException("Plugin state is invalid or excessive.", nameof(entries));
        }

        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var envelope = new PluginStateEnvelope(
                CurrentSchemaVersion,
                Array.AsReadOnly(entries
                    .Select(Clone)
                    .OrderBy(entry => entry.PluginId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.PluginVersion, StringComparer.Ordinal)
                    .ToArray()));
            await AtomicJsonFile.WriteAsync(
                _path,
                envelope,
                JsonOptions,
                MaximumBytes,
                cancellationToken,
                static (_, _) => new InvalidDataException(
                    "The plugin state exceeds its supported encoded size.")).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsValid(PluginStateEntry? entry) =>
        entry is not null &&
        !string.IsNullOrWhiteSpace(entry.PluginId) &&
        entry.PluginId.Length <= PluginLimits.MaximumIdentifierCharacters &&
        PluginManifestParser.TryVersion(entry.PluginVersion, out _) &&
        entry.GrantedCapabilities is not null &&
        entry.GrantedCapabilities.All(Enum.IsDefined) &&
        entry.ConsecutiveFailureCount is >= 0 and <= 1_000 &&
        entry.AcceptedIntegrityHash is null or { Length: 64 } &&
        entry.LastError is null or { Length: <= 2_048 };

    private static PluginStateEntry Clone(PluginStateEntry entry) =>
        entry with
        {
            GrantedCapabilities = new HashSet<PluginCapability>(entry.GrantedCapabilities),
        };

    private sealed record PluginStateEnvelope(
        int SchemaVersion,
        IReadOnlyList<PluginStateEntry> Entries);
}

/// <summary>
/// Computes a deterministic SHA-256 identity for one controlled installed plugin tree.
/// </summary>
/// <remarks>
/// Enumeration, depth, count, size, and reparse points are bounded and checked
/// before bytes are accepted. The hash detects change; it is not a signature,
/// publisher identity, review, or sandbox.
/// </remarks>
public sealed class PluginIntegrityService : IPluginIntegrityService
{
    public async Task<string> CalculateAsync(
        string pluginDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pluginDirectory));
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists || rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The plugin directory is unavailable or is a reparse point.");
        }

        var files = new List<FileInfo>();
        var pending = new Queue<DirectoryInfo>();
        pending.Enqueue(rootInfo);
        var directoryCount = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Dequeue();
            directoryCount++;
            if (directoryCount > PluginLimits.MaximumInstalledFiles)
            {
                throw new InvalidOperationException("The installed plugin contains too many directories.");
            }

            var relativeDirectory = Path.GetRelativePath(root, directory.FullName);
            if (relativeDirectory != "." &&
                relativeDirectory.Split(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar).Length > PluginLimits.MaximumManifestDepth)
            {
                throw new InvalidOperationException("The installed plugin directory depth exceeds the limit.");
            }

            foreach (var entry in directory.EnumerateFileSystemInfos()
                         .OrderBy(value => value.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidOperationException("Plugin integrity cannot traverse reparse points.");
                }

                switch (entry)
                {
                    case DirectoryInfo child:
                        pending.Enqueue(child);
                        break;
                    case FileInfo file:
                        files.Add(file);
                        if (files.Count > PluginLimits.MaximumInstalledFiles)
                        {
                            throw new InvalidOperationException("The installed plugin contains too many files.");
                        }

                        break;
                    default:
                        throw new InvalidOperationException("The installed plugin contains an unsupported filesystem entry.");
                }
            }
        }

        files.Sort((first, second) => string.Compare(
            Path.GetRelativePath(root, first.FullName).Replace('\\', '/'),
            Path.GetRelativePath(root, second.FullName).Replace('\\', '/'),
            StringComparison.Ordinal));

        long totalBytes = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException("Plugin integrity cannot include reparse-point files.");
            }

            totalBytes = checked(totalBytes + file.Length);
            if (totalBytes > PluginLimits.MaximumInstalledPluginBytes)
            {
                throw new InvalidOperationException("The installed plugin exceeds the integrity size limit.");
            }

            var relative = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
            var pathBytes = Encoding.UTF8.GetBytes(relative);
            hash.AppendData(BitConverter.GetBytes(pathBytes.Length));
            hash.AppendData(pathBytes);
            hash.AppendData(BitConverter.GetBytes(file.Length));
            await using var stream = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

/// <summary>
/// Performs bounded manifest-first discovery without executing external code.
/// </summary>
/// <remarks>
/// Discovery examines only the controlled root and exact supported directory
/// depth. It parses manifests, confirms managed entry assemblies, compares
/// state/integrity, protects built-in identities, selects versions
/// deterministically, and applies dependency results. Disabled or invalid
/// plugins remain inspectable but not loadable.
/// </remarks>
public sealed class PluginDiscoveryService : IPluginDiscoveryService
{
    private readonly string _pluginRoot;
    private readonly IPluginManifestParser _parser;
    private readonly IPluginStateStore _stateStore;
    private readonly IPluginIntegrityService _integrity;
    private readonly IPluginDependencyResolver _dependencies;
    private readonly IPluginDiagnostics _diagnostics;
    private readonly IReadOnlyList<BuiltInPluginDefinition> _builtIns;
    private readonly TimeProvider _timeProvider;

    public PluginDiscoveryService(
        string pluginRoot,
        IPluginManifestParser parser,
        IPluginStateStore stateStore,
        IPluginIntegrityService integrity,
        IPluginDependencyResolver dependencies,
        IPluginDiagnostics diagnostics,
        IEnumerable<BuiltInPluginDefinition>? builtIns = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRoot);
        _pluginRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pluginRoot));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _integrity = integrity ?? throw new ArgumentNullException(nameof(integrity));
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _builtIns = Array.AsReadOnly((builtIns ?? []).ToArray());
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PluginDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        var states = await _stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var stateMap = states.ToDictionary(
            state => (state.PluginId, state.PluginVersion),
            state => state);
        var discovered = new List<PluginDescriptor>();
        foreach (var definition in _builtIns
                     .OrderBy(value => value.Manifest.PluginId, StringComparer.Ordinal)
                     .ThenBy(value => ParseVersion(value.Manifest.PluginVersion)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validation = _parser.Parse(
                JsonSerializer.SerializeToUtf8Bytes(
                    definition.Manifest,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) },
                    }),
                expectedBuiltIn: true);
            if (!validation.IsValid)
            {
                discovered.Add(InvalidDescriptor(
                    definition.Manifest.PluginId,
                    "built-in",
                    PluginOriginKind.BuiltIn,
                    string.Join(" ", validation.Issues.Select(issue => issue.Message))));
                continue;
            }

            discovered.Add(new PluginDescriptor(
                validation.Manifest,
                "built-in",
                new PluginProvenance(
                    PluginOriginKind.BuiltIn,
                    "OmniSorSe application assembly",
                    _timeProvider.GetUtcNow().ToUniversalTime()),
                PluginLifecycleState.Ready,
                Compatibility(validation.Manifest!),
                PluginIntegrityStatus.NotApplicable,
                null,
                true,
                true,
                new HashSet<PluginCapability>(validation.Manifest!.Capabilities),
                [],
                null,
                false));
        }

        try
        {
            if (Directory.Exists(_pluginRoot))
            {
                var rootInfo = new DirectoryInfo(_pluginRoot);
                if (rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    _diagnostics.Record(
                        PluginDiagnosticKind.Discovery,
                        "external",
                        "The controlled plugin root is a reparse point and was not scanned.",
                        "plugin.root-reparse");
                }
                else
                {
                    foreach (var directory in CandidateDirectories(rootInfo))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (discovered.Count >= PluginLimits.MaximumPlugins)
                        {
                            _diagnostics.Record(
                                PluginDiagnosticKind.Discovery,
                                "external",
                                "The plugin discovery limit was reached.");
                            break;
                        }

                        discovered.Add(await DiscoverExternalAsync(
                            directory,
                            stateMap,
                            cancellationToken).ConfigureAwait(false));
                    }
                }
            }
            else
            {
                _diagnostics.Record(
                    PluginDiagnosticKind.Discovery,
                    "external",
                    "The controlled plugin directory does not exist; built-in plugins remain available.");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _diagnostics.Record(
                PluginDiagnosticKind.Discovery,
                "external",
                "The controlled plugin directory is unavailable; safe startup continues.",
                exception.GetType().Name);
        }

        MarkDuplicateInstalls(discovered);
        SelectVersions(discovered);
        var resolved = _dependencies.Resolve(Array.AsReadOnly(discovered.ToArray()));
        _diagnostics.Record(
            PluginDiagnosticKind.Discovery,
            "host",
            $"Manifest-first discovery completed with {resolved.Count} plugin installation(s).");
        return new PluginDiscoveryResult(resolved, _diagnostics.List());
    }

    private async Task<PluginDescriptor> DiscoverExternalAsync(
        DirectoryInfo directory,
        IReadOnlyDictionary<(string PluginId, string PluginVersion), PluginStateEntry> states,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(directory.FullName, "plugin.json");
        var parsed = _parser.ParseFile(manifestPath);
        if (!parsed.IsValid || parsed.Manifest is null)
        {
            _diagnostics.Record(
                PluginDiagnosticKind.ManifestParsing,
                directory.Name,
                "An external plugin manifest was rejected.",
                parsed.Issues.FirstOrDefault()?.Code);
            return InvalidDescriptor(
                directory.Name,
                directory.FullName,
                PluginOriginKind.ControlledFolder,
                string.Join(" ", parsed.Issues.Select(issue => issue.Message)));
        }

        var manifest = parsed.Manifest;
        var entryAssemblyPath = Path.GetFullPath(Path.Combine(
            directory.FullName,
            manifest.EntryAssembly));
        if (!IsWithinDirectory(directory.FullName, entryAssemblyPath) ||
            HasReparsePoint(directory.FullName, entryAssemblyPath) ||
            !File.Exists(entryAssemblyPath) ||
            !IsManagedAssembly(entryAssemblyPath))
        {
            _diagnostics.Record(
                PluginDiagnosticKind.Validation,
                manifest.PluginId,
                "The external plugin installation is incomplete or its entry assembly is corrupt.",
                "plugin.entry-invalid");
            return new PluginDescriptor(
                manifest,
                directory.FullName,
                new PluginProvenance(
                    PluginOriginKind.ControlledFolder,
                    directory.FullName,
                    _timeProvider.GetUtcNow().ToUniversalTime()),
                PluginLifecycleState.Invalid,
                PluginCompatibilityState.Compatible,
                PluginIntegrityStatus.Failed,
                null,
                false,
                false,
                new HashSet<PluginCapability>(),
                [],
                "The entry assembly is missing or is not a managed .NET assembly.",
                false);
        }

        var compatibility = Compatibility(manifest);
        states.TryGetValue((manifest.PluginId, manifest.PluginVersion), out var state);
        var enabled = state?.Enabled == true && state.Quarantined == false;
        var lifecycle = state?.Quarantined == true
            ? PluginLifecycleState.Quarantined
            : compatibility != PluginCompatibilityState.Compatible
                ? PluginLifecycleState.Incompatible
                : enabled
                    ? PluginLifecycleState.Ready
                    : PluginLifecycleState.Disabled;
        var integrityStatus = PluginIntegrityStatus.NotCalculated;
        string? calculatedHash = null;
        string? lastError = state?.LastError;
        var grantedCapabilities = state is null
            ? new HashSet<PluginCapability>()
            : state.GrantedCapabilities
                .Where(manifest.Capabilities.Contains)
                .ToHashSet();
        if (state is not null &&
            state.GrantedCapabilities.Any(capability => !manifest.Capabilities.Contains(capability)))
        {
            enabled = false;
            lifecycle = PluginLifecycleState.Disabled;
            lastError = "Stored capability grants no longer match the manifest. Review and enable the plugin again.";
        }

        if (enabled || state?.AcceptedIntegrityHash is not null)
        {
            try
            {
                calculatedHash = await _integrity.CalculateAsync(
                    directory.FullName,
                    cancellationToken).ConfigureAwait(false);
                integrityStatus = state?.AcceptedIntegrityHash is null ||
                                  string.Equals(
                                      state.AcceptedIntegrityHash,
                                      calculatedHash,
                                      StringComparison.OrdinalIgnoreCase)
                    ? PluginIntegrityStatus.Verified
                    : PluginIntegrityStatus.Changed;
                if (integrityStatus == PluginIntegrityStatus.Changed)
                {
                    enabled = false;
                    lifecycle = PluginLifecycleState.Disabled;
                    lastError = "Plugin contents changed after approval. Review and enable the plugin again.";
                    _diagnostics.Record(
                        PluginDiagnosticKind.IntegrityChanged,
                        manifest.PluginId,
                        lastError,
                        "plugin.integrity-changed");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                enabled = false;
                integrityStatus = PluginIntegrityStatus.Failed;
                lifecycle = PluginLifecycleState.Invalid;
                lastError = "Plugin integrity could not be calculated safely.";
                _diagnostics.Record(
                    PluginDiagnosticKind.IntegrityHashing,
                    manifest.PluginId,
                    lastError,
                    exception.GetType().Name);
            }
        }

        return new PluginDescriptor(
            manifest,
            directory.FullName,
            new PluginProvenance(
                PluginOriginKind.ControlledFolder,
                directory.FullName,
                _timeProvider.GetUtcNow().ToUniversalTime()),
            lifecycle,
            compatibility,
            integrityStatus,
            calculatedHash,
            false,
            enabled,
            grantedCapabilities,
            [],
            lastError,
            false);
    }

    private static IEnumerable<DirectoryInfo> CandidateDirectories(DirectoryInfo root)
    {
        foreach (var pluginDirectory in root.EnumerateDirectories()
                     .Where(directory =>
                         !directory.Name.StartsWith('_') &&
                         !directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                     .OrderBy(directory => directory.Name, StringComparer.Ordinal))
        {
            if (File.Exists(Path.Combine(pluginDirectory.FullName, "plugin.json")))
            {
                yield return pluginDirectory;
                continue;
            }

            foreach (var versionDirectory in pluginDirectory.EnumerateDirectories()
                         .Where(directory =>
                             !directory.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
                             File.Exists(Path.Combine(directory.FullName, "plugin.json")))
                         .OrderBy(directory => directory.Name, StringComparer.Ordinal))
            {
                yield return versionDirectory;
            }
        }
    }

    private static void MarkDuplicateInstalls(List<PluginDescriptor> plugins)
    {
        foreach (var pluginGroup in plugins
                     .Where(plugin => plugin.Manifest is not null)
                     .GroupBy(plugin => plugin.PluginId, StringComparer.Ordinal))
        {
            var builtIns = pluginGroup.Where(plugin => plugin.IsBuiltIn).ToArray();
            if (builtIns.Length > 0)
            {
                foreach (var external in pluginGroup.Where(plugin => !plugin.IsBuiltIn))
                {
                    var index = plugins.IndexOf(external);
                    plugins[index] = external with
                    {
                        LifecycleState = PluginLifecycleState.Invalid,
                        IsEnabled = false,
                        LastError = "The plugin ID is reserved by a built-in plugin.",
                    };
                }

                continue;
            }

            foreach (var duplicateGroup in pluginGroup
                         .GroupBy(plugin => plugin.PluginVersion, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
            {
                foreach (var duplicate in duplicateGroup)
                {
                    var index = plugins.IndexOf(duplicate);
                    plugins[index] = duplicate with
                    {
                        LifecycleState = PluginLifecycleState.Invalid,
                        IsEnabled = false,
                        LastError = "The same plugin ID and version is installed more than once.",
                    };
                }
            }
        }
    }

    private static void SelectVersions(List<PluginDescriptor> plugins)
    {
        foreach (var group in plugins
                     .Where(plugin =>
                         plugin.Manifest is not null &&
                         plugin.LifecycleState != PluginLifecycleState.Invalid)
                     .GroupBy(plugin => plugin.PluginId, StringComparer.Ordinal))
        {
            var selected = group
                .Where(plugin => plugin.IsEnabled)
                .OrderByDescending(plugin => ParseVersion(plugin.PluginVersion))
                .ThenBy(plugin => plugin.InstallationPath, StringComparer.Ordinal)
                .FirstOrDefault() ??
                group
                    .OrderByDescending(plugin => ParseVersion(plugin.PluginVersion))
                    .ThenBy(plugin => plugin.InstallationPath, StringComparer.Ordinal)
                    .First();
            var index = plugins.IndexOf(selected);
            plugins[index] = selected with { IsSelectedVersion = true };
        }
    }

    private static PluginCompatibilityState Compatibility(PluginManifest manifest)
    {
        if (manifest.ManifestSchemaVersion != PluginLimits.CurrentManifestSchemaVersion)
        {
            return PluginCompatibilityState.UnsupportedManifest;
        }

        if (!PluginLimits.IsRuntimeCompatible(manifest.RuntimeCompatibility))
        {
            return PluginCompatibilityState.RuntimeIncompatible;
        }

        var currentRuntimeIdentifier = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        if (manifest.SupportedRuntimeIdentifiers.Count > 0 &&
            !manifest.SupportedRuntimeIdentifiers.Contains(
                currentRuntimeIdentifier,
                StringComparer.OrdinalIgnoreCase))
        {
            return PluginCompatibilityState.PlatformIncompatible;
        }

        if (manifest.ContainsNativeDependencies &&
            manifest.SupportedRuntimeIdentifiers.Count == 0)
        {
            return PluginCompatibilityState.PlatformIncompatible;
        }

        _ = PluginManifestParser.TryVersion(manifest.MinimumOpenSorSeVersion, out var minimum);
        if (PluginLimits.HostVersion < minimum)
        {
            return PluginCompatibilityState.HostVersionTooOld;
        }

        if (manifest.MaximumOpenSorSeVersion is { } maximumText)
        {
            _ = PluginManifestParser.TryVersion(maximumText, out var maximum);
            if (PluginLimits.HostVersion > maximum)
            {
                return PluginCompatibilityState.HostVersionTooNew;
            }
        }

        return PluginCompatibilityState.Compatible;
    }

    private static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            return reader.HasMetadata && reader.PEHeaders.CorHeader is not null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return false;
        }
    }

    private static bool IsWithinDirectory(string directory, string candidate)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var comparison = OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparison;
        return string.Equals(root, candidate, comparison) ||
               candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    private static bool HasReparsePoint(string root, string candidate)
    {
        var current = new FileInfo(candidate).Directory;
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        while (current is not null &&
               !string.Equals(current.FullName, normalizedRoot, PathComparison))
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            current = current.Parent;
        }

        return current is null ||
               new FileInfo(candidate).Exists &&
               new FileInfo(candidate).Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static StringComparison PathComparison =>
        OpenSorSe.Core.Platform.PlatformServices.CurrentPathSemantics.Comparison;

    private PluginDescriptor InvalidDescriptor(
        string pluginId,
        string path,
        PluginOriginKind origin,
        string message) =>
        new(
            null,
            path,
            new PluginProvenance(
                origin,
                path,
                _timeProvider.GetUtcNow().ToUniversalTime()),
            PluginLifecycleState.Invalid,
            PluginCompatibilityState.UnsupportedManifest,
            origin == PluginOriginKind.BuiltIn
                ? PluginIntegrityStatus.NotApplicable
                : PluginIntegrityStatus.Failed,
            null,
            false,
            false,
            new HashSet<PluginCapability>(),
            [],
            message,
            false);

    private static Version ParseVersion(string version)
    {
        _ = PluginManifestParser.TryVersion(version, out var parsed);
        return parsed;
    }
}

/// <summary>
/// Resolves exact plugin versions and transitive dependencies as a deterministic fail-closed graph.
/// </summary>
/// <remarks>
/// Missing required dependencies, version mismatch, cycles, duplicate
/// identities, and contribution conflicts block the affected activation set.
/// An enabled known-working version is not silently displaced by a newly
/// installed disabled higher version.
/// </remarks>
public sealed class PluginDependencyResolver : IPluginDependencyResolver
{
    private readonly IPluginDiagnostics _diagnostics;

    public PluginDependencyResolver(IPluginDiagnostics diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public IReadOnlyList<PluginDescriptor> Resolve(IReadOnlyList<PluginDescriptor> discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        var result = discovered.ToArray();
        var selected = result
            .Where(plugin =>
                plugin.Manifest is not null &&
                plugin.IsSelectedVersion &&
                plugin.LifecycleState is not PluginLifecycleState.Invalid and
                    not PluginLifecycleState.Incompatible)
            .ToDictionary(plugin => plugin.PluginId, StringComparer.Ordinal);

        for (var index = 0; index < result.Length; index++)
        {
            var plugin = result[index];
            if (plugin.Manifest is null ||
                !plugin.IsSelectedVersion ||
                !plugin.IsEnabled ||
                plugin.LifecycleState is PluginLifecycleState.Invalid or
                    PluginLifecycleState.Incompatible or
                    PluginLifecycleState.Quarantined)
            {
                continue;
            }

            var errors = new List<string>();
            foreach (var dependency in plugin.Manifest.Dependencies
                         .OrderBy(value => value.PluginId, StringComparer.Ordinal))
            {
                if (!selected.TryGetValue(dependency.PluginId, out var installed) ||
                    !installed.IsEnabled)
                {
                    if (!dependency.Optional)
                    {
                        errors.Add($"Required plugin \"{dependency.PluginId}\" is missing or disabled.");
                    }

                    continue;
                }

                _ = PluginManifestParser.TryVersion(installed.PluginVersion, out var installedVersion);
                _ = PluginManifestParser.TryVersion(dependency.MinimumVersion, out var minimumVersion);
                if (installedVersion < minimumVersion)
                {
                    errors.Add(
                        $"Plugin \"{dependency.PluginId}\" {installed.PluginVersion} is below required version {dependency.MinimumVersion}.");
                }

                if (dependency.MaximumVersion is { } maximumText)
                {
                    _ = PluginManifestParser.TryVersion(maximumText, out var maximumVersion);
                    if (installedVersion > maximumVersion)
                    {
                        errors.Add(
                            $"Plugin \"{dependency.PluginId}\" {installed.PluginVersion} exceeds supported version {maximumText}.");
                    }
                }
            }

            if (errors.Count > 0)
            {
                result[index] = plugin with
                {
                    LifecycleState = PluginLifecycleState.DependencyBlocked,
                    DependencyErrors = Array.AsReadOnly(errors.ToArray()),
                };
            }
        }

        var enabled = result
            .Where(plugin =>
                plugin.Manifest is not null &&
                plugin.IsSelectedVersion &&
                plugin.IsEnabled &&
                plugin.LifecycleState == PluginLifecycleState.Ready)
            .ToDictionary(plugin => plugin.PluginId, StringComparer.Ordinal);
        foreach (var cycle in FindCycles(enabled))
        {
            for (var index = 0; index < result.Length; index++)
            {
                if (!cycle.Contains(result[index].PluginId, StringComparer.Ordinal))
                {
                    continue;
                }

                var errors = result[index].DependencyErrors
                    .Append($"Dependency cycle detected: {string.Join(" -> ", cycle)}.")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                result[index] = result[index] with
                {
                    LifecycleState = PluginLifecycleState.DependencyBlocked,
                    DependencyErrors = Array.AsReadOnly(errors),
                };
                _diagnostics.Record(
                    PluginDiagnosticKind.DependencyResolution,
                    result[index].PluginId,
                    errors[^1],
                    "plugin.dependency-cycle");
            }
        }

        PropagateBlockedDependencies(result);

        return Array.AsReadOnly(result
            .OrderBy(plugin => plugin.PluginId, StringComparer.Ordinal)
            .ThenByDescending(plugin => ParseVersion(plugin.PluginVersion))
            .ThenBy(plugin => plugin.InstallationPath, StringComparer.Ordinal)
            .ToArray());
    }

    private static void PropagateBlockedDependencies(PluginDescriptor[] plugins)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            var selected = plugins
                .Where(plugin => plugin.Manifest is not null && plugin.IsSelectedVersion)
                .ToDictionary(plugin => plugin.PluginId, StringComparer.Ordinal);
            for (var index = 0; index < plugins.Length; index++)
            {
                var plugin = plugins[index];
                if (plugin.Manifest is null ||
                    !plugin.IsSelectedVersion ||
                    !plugin.IsEnabled ||
                    plugin.LifecycleState != PluginLifecycleState.Ready)
                {
                    continue;
                }

                var unavailable = plugin.Manifest.Dependencies
                    .Where(dependency => !dependency.Optional)
                    .Select(dependency => selected.GetValueOrDefault(dependency.PluginId))
                    .FirstOrDefault(dependency =>
                        dependency is null ||
                        !dependency.IsEnabled ||
                        dependency.LifecycleState != PluginLifecycleState.Ready);
                if (unavailable is null &&
                    plugin.Manifest.Dependencies
                        .Where(dependency => !dependency.Optional)
                        .All(dependency => selected.ContainsKey(dependency.PluginId)))
                {
                    continue;
                }

                var errors = plugin.DependencyErrors
                    .Append("A required plugin is unavailable because its dependency graph did not resolve to a ready state.")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                plugins[index] = plugin with
                {
                    LifecycleState = PluginLifecycleState.DependencyBlocked,
                    DependencyErrors = Array.AsReadOnly(errors),
                };
                changed = true;
            }
        }
    }

    public static IReadOnlyList<PluginDescriptor> ActivationOrder(
        IReadOnlyList<PluginDescriptor> plugins)
    {
        var active = plugins
            .Where(plugin =>
                plugin.Manifest is not null &&
                plugin.IsSelectedVersion &&
                plugin.IsEnabled &&
                plugin.LifecycleState == PluginLifecycleState.Ready)
            .ToDictionary(plugin => plugin.PluginId, StringComparer.Ordinal);
        var indegree = active.Keys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
        var dependants = active.Keys.ToDictionary(
            key => key,
            _ => new List<string>(),
            StringComparer.Ordinal);
        foreach (var plugin in active.Values)
        {
            foreach (var dependency in plugin.Manifest!.Dependencies.Where(value => !value.Optional))
            {
                if (!active.ContainsKey(dependency.PluginId))
                {
                    continue;
                }

                indegree[plugin.PluginId]++;
                dependants[dependency.PluginId].Add(plugin.PluginId);
            }
        }

        var queue = new SortedSet<string>(
            indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key),
            StringComparer.Ordinal);
        var ordered = new List<PluginDescriptor>();
        while (queue.Count > 0)
        {
            var next = queue.Min!;
            queue.Remove(next);
            ordered.Add(active[next]);
            foreach (var dependant in dependants[next].OrderBy(value => value, StringComparer.Ordinal))
            {
                indegree[dependant]--;
                if (indegree[dependant] == 0)
                {
                    queue.Add(dependant);
                }
            }
        }

        return Array.AsReadOnly(ordered.ToArray());
    }

    private static IReadOnlyList<IReadOnlyList<string>> FindCycles(
        IReadOnlyDictionary<string, PluginDescriptor> plugins)
    {
        var cycles = new List<IReadOnlyList<string>>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var id in plugins.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            Visit(id);
        }

        return cycles;

        void Visit(string id)
        {
            if (visited.Contains(id))
            {
                return;
            }

            if (!visiting.Add(id))
            {
                var start = stack.IndexOf(id);
                if (start >= 0)
                {
                    var cycle = stack.Skip(start).Append(id).ToArray();
                    if (!cycles.Any(existing =>
                            existing.Count == cycle.Length &&
                            existing.OrderBy(value => value, StringComparer.Ordinal)
                                .SequenceEqual(cycle.OrderBy(value => value, StringComparer.Ordinal))))
                    {
                        cycles.Add(Array.AsReadOnly(cycle));
                    }
                }

                return;
            }

            stack.Add(id);
            foreach (var dependency in plugins[id].Manifest!.Dependencies
                         .Where(value => !value.Optional && plugins.ContainsKey(value.PluginId))
                         .OrderBy(value => value.PluginId, StringComparer.Ordinal))
            {
                Visit(dependency.PluginId);
            }

            stack.RemoveAt(stack.Count - 1);
            visiting.Remove(id);
            visited.Add(id);
        }
    }

    private static Version ParseVersion(string version)
    {
        _ = PluginManifestParser.TryVersion(version, out var parsed);
        return parsed;
    }
}

/// <summary>
/// Owns the active in-process contribution set and rejects ambiguous replacement.
/// </summary>
/// <remarks>
/// Registration requires a manifest declaration and effective capability for
/// each extension point. Conflicts fail rather than replacing an active
/// contribution. Deactivation removes every contribution owned by the exact
/// plugin/version.
/// </remarks>
public sealed class PluginContributionRegistry : IPluginContributionRegistry
{
    private readonly object _sync = new();
    private readonly List<PluginContributionRegistration> _registrations = [];
    private readonly IPluginDiagnostics _diagnostics;

    public PluginContributionRegistry(IPluginDiagnostics diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public PluginOperationResult Register(
        PluginDescriptor owner,
        IReadOnlyList<IExtensionContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(contributions);
        if (owner.Manifest is null)
        {
            return new PluginOperationResult(false, "A valid manifest is required before registration.");
        }

        var manifest = owner.Manifest;
        if (contributions.Count > PluginLimits.MaximumContributionsPerPlugin ||
            contributions.Any(value => value is null))
        {
            return new PluginOperationResult(false, "The plugin returned invalid or excessive contributions.");
        }

        var proposed = new List<PluginContributionRegistration>();
        foreach (var contribution in contributions)
        {
            var kind = Kind(contribution);
            var requiredCapability = RequiredCapability(kind);
            if (!owner.GrantedCapabilities.Contains(requiredCapability))
            {
                return new PluginOperationResult(
                    false,
                    $"Contribution \"{contribution.Id}\" requires granted capability {requiredCapability}.");
            }

            var declaration = manifest.Contributions.SingleOrDefault(value =>
                string.Equals(value.ContributionId, contribution.Id, StringComparison.Ordinal) &&
                value.ExtensionPoint == kind);
            if (declaration is null ||
                !string.Equals(declaration.DisplayName, contribution.DisplayName, StringComparison.Ordinal) ||
                declaration.Priority != contribution.Priority)
            {
                return new PluginOperationResult(
                    false,
                    $"Contribution \"{contribution.Id}\" does not match the validated manifest.");
            }

            proposed.Add(new PluginContributionRegistration(
                declaration.ContributionId,
                manifest.PluginId,
                manifest.PluginVersion,
                kind,
                declaration.DisplayName,
                declaration.Priority,
                PluginLifecycleState.Active,
                owner.Provenance,
                contribution));
        }

        if (proposed.Select(value => value.ContributionId)
                .Distinct(StringComparer.Ordinal)
                .Count() != proposed.Count)
        {
            return new PluginOperationResult(false, "The plugin returned duplicate contribution IDs.");
        }

        lock (_sync)
        {
            var conflict = proposed.FirstOrDefault(candidate =>
                _registrations.Any(existing =>
                    string.Equals(
                        existing.ContributionId,
                        candidate.ContributionId,
                        StringComparison.Ordinal)));
            if (conflict is not null)
            {
                return new PluginOperationResult(
                    false,
                    $"Contribution ID \"{conflict.ContributionId}\" is already registered; no contribution was replaced.");
            }

            _registrations.AddRange(proposed);
            _registrations.Sort(Compare);
        }

        _diagnostics.Record(
            PluginDiagnosticKind.ContributionRegistration,
            manifest.PluginId,
            $"Registered {proposed.Count} validated contribution(s).");
        return new PluginOperationResult(true, $"Registered {proposed.Count} contribution(s).", owner);
    }

    public void RemovePlugin(string pluginId, string pluginVersion)
    {
        lock (_sync)
        {
            _registrations.RemoveAll(value =>
                string.Equals(value.PluginId, pluginId, StringComparison.Ordinal) &&
                string.Equals(value.PluginVersion, pluginVersion, StringComparison.Ordinal));
        }
    }

    public IReadOnlyList<PluginContributionRegistration> List()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_registrations.ToArray());
        }
    }

    public PluginContributionRegistration? Find(
        string pluginId,
        string contributionId,
        ExtensionPointKind extensionPoint)
    {
        lock (_sync)
        {
            return _registrations.SingleOrDefault(value =>
                string.Equals(value.PluginId, pluginId, StringComparison.Ordinal) &&
                string.Equals(value.ContributionId, contributionId, StringComparison.Ordinal) &&
                value.ExtensionPoint == extensionPoint);
        }
    }

    private static ExtensionPointKind Kind(IExtensionContribution contribution) =>
        contribution switch
        {
            IMetadataProvider => ExtensionPointKind.MetadataProvider,
            IContentExtractor => ExtensionPointKind.ContentExtractor,
            IFileClassifier => ExtensionPointKind.FileClassifier,
            IRecipeFieldProvider => ExtensionPointKind.RecipeFieldProvider,
            IDuplicateSignalProvider => ExtensionPointKind.DuplicateSignalProvider,
            IWorkflowCapabilityProvider => ExtensionPointKind.WorkflowCapabilityProvider,
            IImportFormatProvider => ExtensionPointKind.ImportFormatProvider,
            IExportFormatProvider => ExtensionPointKind.ExportFormatProvider,
            _ => throw new ArgumentException(
                "The contribution does not implement a supported extension point.",
                nameof(contribution)),
        };

    private static PluginCapability RequiredCapability(ExtensionPointKind kind) =>
        kind switch
        {
            ExtensionPointKind.MetadataProvider => PluginCapability.ReadFileMetadata,
            ExtensionPointKind.ContentExtractor => PluginCapability.ReadFileContents,
            ExtensionPointKind.FileClassifier => PluginCapability.ReadFileMetadata,
            ExtensionPointKind.RecipeFieldProvider => PluginCapability.ContributeRecipeFields,
            ExtensionPointKind.DuplicateSignalProvider => PluginCapability.ReadFileMetadata,
            ExtensionPointKind.WorkflowCapabilityProvider =>
                PluginCapability.ContributeWorkflowCapabilities,
            ExtensionPointKind.ImportFormatProvider => PluginCapability.ImportConfiguration,
            ExtensionPointKind.ExportFormatProvider => PluginCapability.ExportReports,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static int Compare(
        PluginContributionRegistration first,
        PluginContributionRegistration second)
    {
        var kind = first.ExtensionPoint.CompareTo(second.ExtensionPoint);
        if (kind != 0)
        {
            return kind;
        }

        var priority = second.Priority.CompareTo(first.Priority);
        return priority != 0
            ? priority
            : string.Compare(first.ContributionId, second.ContributionId, StringComparison.Ordinal);
    }
}

/// <summary>
/// Resolves an exact workflow plugin reference against the active contribution registry.
/// </summary>
/// <remarks>
/// Resolution never falls back to another version or provider. An unavailable
/// capability returns the stable fail-closed diagnostic consumed by workflow
/// and watcher presentation.
/// </remarks>
public sealed class PluginContributionResolver : IPluginContributionResolver
{
    private readonly IPluginContributionRegistry _registry;

    public PluginContributionResolver(IPluginContributionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public PluginOperationResult Resolve(IReadOnlyList<PluginContributionReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        foreach (var reference in references.Where(value => value.Required))
        {
            var registration = _registry.Find(
                reference.PluginId,
                reference.ContributionId,
                reference.ExtensionPoint);
            if (registration is null)
            {
                return new PluginOperationResult(
                    false,
                    $"Plugin capability unavailable — review workflow profile. Contribution \"{reference.ContributionId}\" from \"{reference.PluginId}\" is unavailable.");
            }

            if (reference.PluginVersion is not null &&
                !string.Equals(
                    reference.PluginVersion,
                    registration.PluginVersion,
                    StringComparison.Ordinal))
            {
                return new PluginOperationResult(
                    false,
                    $"Plugin capability unavailable — review workflow profile. Contribution \"{reference.ContributionId}\" resolved to plugin version {registration.PluginVersion}, not required version {reference.PluginVersion}.");
            }
        }

        return new PluginOperationResult(true, "All required plugin contributions are available.");
    }
}
