#pragma warning disable CS1591

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenSorSe.Application.Plugins;
using OpenSorSe.Extensions.Abstractions;

namespace OpenSorSe.Application.Tests;

public sealed class PluginManifestTests
{
    private readonly PluginManifestParser _parser = new();

    [Fact]
    public void Parse_ValidManifest_ReturnsBoundedImmutableModel()
    {
        var result = _parser.Parse(Serialize(Manifest()));

        Assert.True(result.IsValid);
        Assert.Equal("example.plugin", result.Manifest!.PluginId);
        Assert.Single(result.Manifest.Contributions);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_MalformedJson_IsRejected()
    {
        var result = _parser.Parse("{ not json"u8);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "manifest.json");
    }

    [Fact]
    public void Parse_OversizedManifest_IsRejectedBeforeJsonParsing()
    {
        var result = _parser.Parse(new byte[PluginLimits.MaximumManifestBytes + 1]);

        Assert.False(result.IsValid);
        Assert.Equal("manifest.size", Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData("pluginId", "Bad ID", "manifest.plugin-id")]
    [InlineData("pluginId", "con", "manifest.plugin-id")]
    [InlineData("pluginVersion", "latest", "manifest.version")]
    [InlineData("minimumOpenSorSeVersion", "v1", "manifest.minimum-host")]
    [InlineData("maximumOpenSorSeVersion", "future", "manifest.maximum-host")]
    [InlineData("entryAssembly", "../evil.dll", "manifest.entry-assembly")]
    [InlineData("entryAssembly", "C:/evil.dll", "manifest.entry-assembly")]
    [InlineData("entryAssembly", "entry.exe", "manifest.entry-assembly")]
    [InlineData("entryType", "Bad Type", "manifest.entry-type")]
    [InlineData("displayName", "", "manifest.required-text")]
    [InlineData("runtimeCompatibility", "", "manifest.required-text")]
    public void Parse_InvalidScalar_IsRejected(
        string property,
        string value,
        string expectedCode)
    {
        var json = Node(Manifest());
        json[property] = value;

        var result = _parser.Parse(JsonSerializer.SerializeToUtf8Bytes(json));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    [Fact]
    public void Parse_UnsupportedSchema_IsRejected()
    {
        var result = _parser.Parse(Serialize(Manifest() with { ManifestSchemaVersion = 99 }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "manifest.schema");
    }

    [Fact]
    public void Parse_ExternalClaimingBuiltIn_IsRejected()
    {
        var result = _parser.Parse(Serialize(Manifest() with { BuiltIn = true }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "manifest.origin");
    }

    [Fact]
    public void Parse_DuplicateContributionIds_AreRejected()
    {
        var contribution = Manifest().Contributions[0];
        var result = _parser.Parse(Serialize(Manifest() with
        {
            Contributions = [contribution, contribution],
        }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "manifest.contribution-duplicate");
    }

    [Fact]
    public void Parse_ConflictingDependencies_AreRejected()
    {
        var dependency = new PluginDependency("dependency.plugin", "1.0.0");
        var result = _parser.Parse(Serialize(Manifest() with
        {
            Dependencies = [dependency, dependency with { MinimumVersion = "2.0.0" }],
        }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "manifest.dependency-conflict");
    }

    [Fact]
    public void Parse_SelfDependencyAndInvalidRange_AreRejected()
    {
        var result = _parser.Parse(Serialize(Manifest() with
        {
            Dependencies =
            [
                new PluginDependency("example.plugin", "2.0.0", "1.0.0"),
            ],
        }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "manifest.dependency-self");
        Assert.Contains(result.Issues, issue => issue.Code == "manifest.dependency-range");
    }

    [Fact]
    public void Parse_UnknownField_IsRejected()
    {
        var json = Node(Manifest());
        json["executePowerShell"] = true;

        var result = _parser.Parse(JsonSerializer.SerializeToUtf8Bytes(json));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "manifest.json");
    }

    [Fact]
    public void Parse_IncompatibleRuntime_RemainsInspectableButWarned()
    {
        var result = _parser.Parse(Serialize(Manifest() with
        {
            RuntimeCompatibility = "net9.0",
        }));

        Assert.True(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Code == "manifest.runtime" && !issue.IsBlocking);
    }

    [Fact]
    public void Parse_NativeDependenciesRequireBoundedRuntimeIdentifiers()
    {
        var missing = _parser.Parse(Serialize(Manifest() with
        {
            ContainsNativeDependencies = true,
        }));
        var duplicate = _parser.Parse(Serialize(Manifest() with
        {
            SupportedRuntimeIdentifiers = ["linux-x64", "LINUX-X64"],
        }));

        Assert.False(missing.IsValid);
        Assert.Contains(missing.Issues, issue => issue.Code == "manifest.native-platform");
        Assert.False(duplicate.IsValid);
        Assert.Contains(duplicate.Issues, issue => issue.Code == "manifest.runtime-identifiers");

        var matching = _parser.Parse(Serialize(Manifest() with
        {
            ContainsNativeDependencies = true,
            SupportedRuntimeIdentifiers =
                [System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier],
        }));
        Assert.True(matching.IsValid);
    }

    internal static PluginManifest Manifest(
        string id = "example.plugin",
        string version = "1.0.0") =>
        new(
            PluginLimits.CurrentManifestSchemaVersion,
            id,
            "Example Plugin",
            "Example bounded plugin manifest.",
            version,
            "Example Publisher",
            "MIT",
            "1.4.0",
            "1.5.99",
            "net8.0",
            "plugin.dll",
            "Example.Plugin.EntryPoint",
            [
                new PluginManifestContribution(
                    "example.metadata",
                    ExtensionPointKind.MetadataProvider,
                    "Example Metadata"),
            ],
            [PluginCapability.ReadFileMetadata],
            [],
            "https://example.test/plugin",
            "https://example.test/source",
            false,
            null);

    internal static byte[] Serialize(PluginManifest manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions());

    internal static JsonObject Node(PluginManifest manifest) =>
        JsonNode.Parse(Serialize(manifest))!.AsObject();

    internal static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) },
    };
}

public sealed class PluginDiscoveryTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.PluginDiscovery.Tests",
        Guid.NewGuid().ToString("N"));

    public PluginDiscoveryTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }

        DeleteEmptyParent(_workspace);
    }

    [Fact]
    public async Task Discover_NoDirectory_ReturnsBuiltInsAndSafeDiagnostic()
    {
        var service = Service(
            Path.Combine(_workspace, "missing"),
            builtIns: BuiltInPluginCatalog.Definitions);

        var result = await service.DiscoverAsync(CancellationToken.None);

        Assert.Equal(4, result.Plugins.Count);
        Assert.All(result.Plugins, plugin =>
        {
            Assert.True(plugin.IsBuiltIn);
            Assert.True(plugin.IsEnabled);
            Assert.Equal(PluginLifecycleState.Ready, plugin.LifecycleState);
        });
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Summary.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Discover_EmptyDirectory_ReturnsNoExternalPlugins()
    {
        var root = Path.Combine(_workspace, "empty");
        Directory.CreateDirectory(root);

        var result = await Service(root).DiscoverAsync(CancellationToken.None);

        Assert.Empty(result.Plugins);
    }

    [Fact]
    public async Task Discover_NewExternalPlugin_IsDisabledWithoutAssemblyLoading()
    {
        var root = Path.Combine(_workspace, "plugins");
        Install(root, PluginManifestTests.Manifest());

        var result = await Service(root).DiscoverAsync(CancellationToken.None);

        var plugin = Assert.Single(result.Plugins);
        Assert.False(plugin.IsEnabled);
        Assert.Equal(PluginLifecycleState.Disabled, plugin.LifecycleState);
        Assert.Equal(PluginIntegrityStatus.NotCalculated, plugin.IntegrityStatus);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly =>
            string.Equals(assembly.GetName().Name, "Example.Plugin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discover_PlatformConstrainedPlugin_FailsClosedOnRuntimeMismatch()
    {
        var root = Path.Combine(_workspace, "runtime-constrained");
        Install(root, PluginManifestTests.Manifest() with
        {
            ContainsNativeDependencies = true,
            SupportedRuntimeIdentifiers = ["unsupported-test-rid"],
        });

        var result = await Service(root).DiscoverAsync(CancellationToken.None);

        var plugin = Assert.Single(result.Plugins);
        Assert.Equal(PluginCompatibilityState.PlatformIncompatible, plugin.Compatibility);
        Assert.Equal(PluginLifecycleState.Incompatible, plugin.LifecycleState);
        Assert.False(plugin.IsEnabled);
    }

    [Fact]
    public async Task Discover_MissingOrCorruptEntryAssembly_IsInvalid()
    {
        var root = Path.Combine(_workspace, "plugins");
        var directory = Install(root, PluginManifestTests.Manifest(), copyAssembly: false);
        var missing = await Service(root).DiscoverAsync(CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(directory, "plugin.dll"), "not an assembly");
        var corrupt = await Service(root).DiscoverAsync(CancellationToken.None);

        Assert.Equal(PluginLifecycleState.Invalid, Assert.Single(missing.Plugins).LifecycleState);
        Assert.Equal(PluginLifecycleState.Invalid, Assert.Single(corrupt.Plugins).LifecycleState);
    }

    [Fact]
    public async Task Discover_IncompatibleHostAndRuntime_AreInspectableButNotReady()
    {
        var hostRoot = Path.Combine(_workspace, "host");
        Install(hostRoot, PluginManifestTests.Manifest() with
        {
            MinimumOpenSorSeVersion = "2.0.0",
            MaximumOpenSorSeVersion = "2.9.0",
        });
        var runtimeRoot = Path.Combine(_workspace, "runtime");
        Install(runtimeRoot, PluginManifestTests.Manifest() with
        {
            RuntimeCompatibility = "net9.0",
        });

        var host = Assert.Single((await Service(hostRoot).DiscoverAsync(CancellationToken.None)).Plugins);
        var runtime = Assert.Single((await Service(runtimeRoot).DiscoverAsync(CancellationToken.None)).Plugins);

        Assert.Equal(PluginCompatibilityState.HostVersionTooOld, host.Compatibility);
        Assert.Equal(PluginLifecycleState.Incompatible, host.LifecycleState);
        Assert.Equal(PluginCompatibilityState.RuntimeIncompatible, runtime.Compatibility);
        Assert.Equal(PluginLifecycleState.Incompatible, runtime.LifecycleState);
    }

    [Fact]
    public async Task Discover_MultipleVersions_SelectsHighestDeterministically()
    {
        var root = Path.Combine(_workspace, "versions");
        Install(root, PluginManifestTests.Manifest(version: "1.0.0"));
        Install(root, PluginManifestTests.Manifest(version: "1.2.0"));

        var result = await Service(root).DiscoverAsync(CancellationToken.None);

        Assert.Equal(2, result.Plugins.Count);
        Assert.Equal("1.2.0", Assert.Single(result.Plugins, plugin => plugin.IsSelectedVersion).PluginVersion);
    }

    [Fact]
    public async Task Discover_DisabledHigherVersion_DoesNotDisplaceEnabledWorkingVersion()
    {
        var root = Path.Combine(_workspace, "enabled-version");
        Install(root, PluginManifestTests.Manifest(version: "1.0.0"));
        Install(root, PluginManifestTests.Manifest(version: "2.0.0"));
        var state = new MemoryStateStore(
        [
            new PluginStateEntry(
                "example.plugin",
                "1.0.0",
                true,
                new HashSet<PluginCapability> { PluginCapability.ReadFileMetadata },
                null,
                0,
                false,
                DateTimeOffset.UtcNow,
                null),
        ]);

        var result = await Service(root, state).DiscoverAsync(CancellationToken.None);

        var selected = Assert.Single(result.Plugins, plugin => plugin.IsSelectedVersion);
        Assert.Equal("1.0.0", selected.PluginVersion);
        Assert.True(selected.IsEnabled);
        Assert.False(result.Plugins.Single(plugin => plugin.PluginVersion == "2.0.0").IsEnabled);
    }

    [Fact]
    public async Task Discover_ExternalPluginCannotShadowBuiltInPluginId()
    {
        var root = Path.Combine(_workspace, "built-in-shadow");
        var builtIn = BuiltInPluginCatalog.Definitions[0];
        Install(root, PluginManifestTests.Manifest(builtIn.Manifest.PluginId, "9.0.0"));

        var result = await Service(
            root,
            builtIns: [builtIn]).DiscoverAsync(CancellationToken.None);

        Assert.True(Assert.Single(result.Plugins, plugin => plugin.IsBuiltIn).IsSelectedVersion);
        var external = Assert.Single(result.Plugins, plugin => !plugin.IsBuiltIn);
        Assert.Equal(PluginLifecycleState.Invalid, external.LifecycleState);
        Assert.Contains("reserved", external.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Discover_DuplicateIdAndVersion_IsInvalidWithoutLastLoadedWins()
    {
        var root = Path.Combine(_workspace, "duplicates");
        Install(root, PluginManifestTests.Manifest(), outerName: "first");
        Install(root, PluginManifestTests.Manifest(), outerName: "second");

        var result = await Service(root).DiscoverAsync(CancellationToken.None);

        Assert.Equal(2, result.Plugins.Count);
        Assert.All(result.Plugins, plugin =>
        {
            Assert.Equal(PluginLifecycleState.Invalid, plugin.LifecycleState);
            Assert.False(plugin.IsSelectedVersion);
        });
    }

    [Fact]
    public async Task Discover_EnabledIntegrityChange_DisablesPlugin()
    {
        var root = Path.Combine(_workspace, "integrity");
        var directory = Install(root, PluginManifestTests.Manifest());
        var integrity = new PluginIntegrityService();
        var accepted = await integrity.CalculateAsync(directory, CancellationToken.None);
        var state = new MemoryStateStore(
        [
            new PluginStateEntry(
                "example.plugin",
                "1.0.0",
                true,
                new HashSet<PluginCapability> { PluginCapability.ReadFileMetadata },
                accepted,
                0,
                false,
                DateTimeOffset.UtcNow,
                null),
        ]);
        await File.AppendAllTextAsync(Path.Combine(directory, "plugin.dll"), "changed");

        var result = await Service(root, state).DiscoverAsync(CancellationToken.None);

        var plugin = Assert.Single(result.Plugins);
        Assert.False(plugin.IsEnabled);
        Assert.Equal(PluginIntegrityStatus.Changed, plugin.IntegrityStatus);
        Assert.Contains("changed", plugin.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DependencyResolver_VersionConflict_BlocksDependentDeterministically()
    {
        var diagnostics = new PluginDiagnostics();
        var resolver = new PluginDependencyResolver(diagnostics);
        var a = Descriptor(
            PluginManifestTests.Manifest("a.plugin") with
            {
                Dependencies = [new PluginDependency("b.plugin", "2.0.0")],
            });
        var b = Descriptor(
            PluginManifestTests.Manifest("b.plugin", "1.0.0"));

        var result = resolver.Resolve([a, b]);

        Assert.Equal(
            PluginLifecycleState.DependencyBlocked,
            result.Single(plugin => plugin.PluginId == "a.plugin").LifecycleState);
        Assert.Equal(
            PluginLifecycleState.Ready,
            result.Single(plugin => plugin.PluginId == "b.plugin").LifecycleState);
        Assert.Contains(result.SelectMany(plugin => plugin.DependencyErrors), error =>
            error.Contains("below required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DependencyResolver_Cycle_BlocksEveryCycleMember()
    {
        var diagnostics = new PluginDiagnostics();
        var resolver = new PluginDependencyResolver(diagnostics);
        var a = Descriptor(
            PluginManifestTests.Manifest("a.plugin") with
            {
                Dependencies = [new PluginDependency("b.plugin", "1.0.0")],
            });
        var b = Descriptor(
            PluginManifestTests.Manifest("b.plugin") with
            {
                Dependencies = [new PluginDependency("a.plugin", "1.0.0")],
            });

        var result = resolver.Resolve([b, a]);

        Assert.All(result, plugin =>
        {
            Assert.Equal(PluginLifecycleState.DependencyBlocked, plugin.LifecycleState);
            Assert.Contains(plugin.DependencyErrors, error =>
                error.Contains("cycle", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void DependencyResolver_PropagatesBlockedRequiredDependencyTransitively()
    {
        var resolver = new PluginDependencyResolver(new PluginDiagnostics());
        var first = Descriptor(
            PluginManifestTests.Manifest("first.plugin") with
            {
                Dependencies = [new PluginDependency("second.plugin", "1.0.0")],
            });
        var second = Descriptor(
            PluginManifestTests.Manifest("second.plugin") with
            {
                Dependencies = [new PluginDependency("missing.plugin", "1.0.0")],
            });

        var result = resolver.Resolve([first, second]);

        Assert.All(result, plugin =>
            Assert.Equal(PluginLifecycleState.DependencyBlocked, plugin.LifecycleState));
        Assert.Contains(
            result.Single(plugin => plugin.PluginId == "first.plugin").DependencyErrors,
            error => error.Contains("dependency graph", StringComparison.OrdinalIgnoreCase));
    }

    private PluginDiscoveryService Service(
        string root,
        IPluginStateStore? state = null,
        IReadOnlyList<BuiltInPluginDefinition>? builtIns = null)
    {
        var diagnostics = new PluginDiagnostics();
        var dependencies = new PluginDependencyResolver(diagnostics);
        return new PluginDiscoveryService(
            root,
            new PluginManifestParser(),
            state ?? new MemoryStateStore([]),
            new PluginIntegrityService(),
            dependencies,
            diagnostics,
            builtIns);
    }

    private static string Install(
        string root,
        PluginManifest manifest,
        bool copyAssembly = true,
        string? outerName = null)
    {
        var directory = Path.Combine(
            root,
            outerName ?? manifest.PluginId,
            manifest.PluginVersion);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(
            Path.Combine(directory, "plugin.json"),
            PluginManifestTests.Serialize(manifest));
        if (copyAssembly)
        {
            File.Copy(
                typeof(PluginManifestTests).Assembly.Location,
                Path.Combine(directory, manifest.EntryAssembly));
        }

        return directory;
    }

    private static PluginDescriptor Descriptor(PluginManifest manifest) =>
        new(
            manifest,
            "built-in",
            new PluginProvenance(PluginOriginKind.BuiltIn, "test", DateTimeOffset.UtcNow),
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

    internal sealed class MemoryStateStore(IReadOnlyList<PluginStateEntry> initial) : IPluginStateStore
    {
        private IReadOnlyList<PluginStateEntry> _entries = initial;

        public Task<IReadOnlyList<PluginStateEntry>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_entries);

        public Task SaveAsync(
            IReadOnlyList<PluginStateEntry> entries,
            CancellationToken cancellationToken)
        {
            _entries = Array.AsReadOnly(entries.ToArray());
            return Task.CompletedTask;
        }
    }
}
