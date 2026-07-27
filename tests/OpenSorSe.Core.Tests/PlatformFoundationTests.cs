#pragma warning disable CS1591

using OpenSorSe.Core.Platform;

namespace OpenSorSe.Core.Tests;

public sealed class PlatformFoundationTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.Platform.Tests",
        Guid.NewGuid().ToString("N"));

    public PlatformFoundationTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public void FilenamePolicies_DistinguishWindowsLinuxAndPortableNames()
    {
        var windows = new WindowsPathSemantics();
        var linux = new LinuxPathSemantics();

        Assert.False(windows.IsValidFileName("NUL.txt", FileNamePortabilityMode.CurrentPlatform, out _));
        Assert.False(windows.IsValidFileName("trailing. ", FileNamePortabilityMode.CurrentPlatform, out _));
        Assert.True(linux.IsValidFileName(".hidden", FileNamePortabilityMode.CurrentPlatform, out _));
        Assert.True(linux.IsValidFileName("report:final", FileNamePortabilityMode.CurrentPlatform, out _));
        Assert.False(linux.IsValidFileName("report:final", FileNamePortabilityMode.Portable, out _));
        Assert.True(linux.IsValidFileName("Résumé_日本語", FileNamePortabilityMode.Portable, out _));
        Assert.False(windows.IsCaseSensitive);
        Assert.True(linux.IsCaseSensitive);
        Assert.True(windows.Comparer.Equals("Casing", "casing"));
        Assert.False(linux.Comparer.Equals("Casing", "casing"));
        var upperPath = Path.Combine(_workspace, "Report.txt");
        var lowerPath = Path.Combine(_workspace, "report.txt");
        Assert.True(windows.IsCaseOnlyDifference(upperPath, lowerPath));
        Assert.False(linux.IsCaseOnlyDifference(upperPath, lowerPath));
    }

    [Fact]
    public void CurrentPathSemantics_NormalizesAndConfinesWithoutTraversal()
    {
        var semantics = PlatformServices.CurrentPathSemantics;
        var nested = Path.Combine(_workspace, "inside", "..", "inside", "file.txt");
        var escaped = Path.GetFullPath(Path.Combine(_workspace, "..", "outside.txt"));

        Assert.True(semantics.IsWithinRoot(_workspace, nested));
        Assert.False(semantics.IsWithinRoot(_workspace, escaped));
        Assert.True(Path.IsPathFullyQualified(semantics.NormalizeAbsolutePath(nested)));
    }

    [Fact]
    public void LinuxApplicationPaths_UseXdgCategoriesAndRejectRelativeOverrides()
    {
        var config = Path.Combine(_workspace, "xdg-config");
        var data = Path.Combine(_workspace, "xdg-data");
        var state = Path.Combine(_workspace, "xdg-state");
        var cache = Path.Combine(_workspace, "xdg-cache");
        var values = new Dictionary<string, string?>
        {
            ["XDG_CONFIG_HOME"] = config,
            ["XDG_DATA_HOME"] = data,
            ["XDG_STATE_HOME"] = state,
            ["XDG_CACHE_HOME"] = cache,
        };
        var provider = new ApplicationPathProvider(
            HostPlatformKind.Linux,
            name => values.GetValueOrDefault(name),
            _workspace);

        Assert.Equal(Path.Combine(config, "opensorse"), provider.Paths.ConfigurationDirectory);
        Assert.Equal(Path.Combine(data, "opensorse"), provider.Paths.DataDirectory);
        Assert.Equal(Path.Combine(state, "opensorse"), provider.Paths.StateDirectory);
        Assert.Equal(Path.Combine(cache, "opensorse"), provider.Paths.CacheDirectory);
        Assert.Equal(Path.Combine(state, "opensorse", "logs"), provider.Paths.DiagnosticsDirectory);
        Assert.Equal(Path.Combine(data, "opensorse", "plugins"), provider.Paths.PluginDirectory);

        values["XDG_CONFIG_HOME"] = "relative";
        var fallback = new ApplicationPathProvider(
            HostPlatformKind.Linux,
            name => values.GetValueOrDefault(name),
            _workspace);
        Assert.Equal(
            Path.Combine(_workspace, ".config", "opensorse"),
            fallback.Paths.ConfigurationDirectory);
    }

    [Fact]
    public void WindowsApplicationPaths_PreserveExistingLocalApplicationDataLayout()
    {
        var local = Path.Combine(_workspace, "LocalAppData");
        var provider = new ApplicationPathProvider(
            HostPlatformKind.Windows,
            _ => null,
            _workspace,
            local);
        var root = Path.Combine(local, "OpenSorSe");

        Assert.Equal(root, provider.Paths.ConfigurationDirectory);
        Assert.Equal(root, provider.Paths.DataDirectory);
        Assert.Equal(root, provider.Paths.StateDirectory);
        Assert.Equal(Path.Combine(root, "Logs"), provider.Paths.DiagnosticsDirectory);
        Assert.Equal(Path.Combine(root, "plugins"), provider.Paths.PluginDirectory);
    }

    [Fact]
    public void BestEffortIdentity_IsPreservedByRenameAndDistinguishesChangedMetadata()
    {
        var original = Path.Combine(_workspace, "identity-a.txt");
        var renamed = Path.Combine(_workspace, "identity-b.txt");
        File.WriteAllText(original, "alpha");
        var provider = new BestEffortFileIdentityProvider();
        var before = provider.Capture(original);

        File.Move(original, renamed);
        var afterRename = provider.Capture(renamed);
        File.AppendAllText(renamed, "-changed");
        var afterChange = provider.Capture(renamed);

        Assert.Equal(FileIdentityStrength.BestEffort, before.Strength);
        Assert.Equal(before.Identity, afterRename.Identity);
        Assert.NotEqual(before.Identity, afterChange.Identity);
    }

    [Fact]
    public void CurrentNativeIdentity_WhenSupported_IsStableAcrossRename()
    {
        var original = Path.Combine(_workspace, "native-a.txt");
        var renamed = Path.Combine(_workspace, "native-b.txt");
        var copied = Path.Combine(_workspace, "native-copy.txt");
        File.WriteAllText(original, "native");
        var provider = FileIdentityProviderFactory.CreateCurrent();
        var before = provider.Capture(original);

        File.Move(original, renamed);
        var after = provider.Capture(renamed);
        File.Copy(renamed, copied);
        var copy = provider.Capture(copied);

        if (provider.SupportsNativeIdentity)
        {
            Assert.Equal(FileIdentityStrength.Native, before.Strength);
            Assert.Equal(before.Identity, after.Identity);
            Assert.NotEqual(before.Identity, copy.Identity);
            Assert.NotNull(before.FileSystemId);
        }
        else
        {
            Assert.NotEqual(FileIdentityStrength.Native, before.Strength);
        }
    }

    [Fact]
    public void ExternalToolLocator_RejectsRelativeConfigurationAndMissingExecutables()
    {
        var locator = new ExternalToolLocator(PlatformServices.CurrentPlatform, _ => string.Empty);

        var relative = locator.Locate("tool", "relative/tool");
        var missing = locator.Locate(
            "tool",
            Path.Combine(_workspace, "missing-tool"));

        Assert.False(relative.IsAvailable);
        Assert.Contains("absolute", relative.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.False(missing.IsAvailable);
        Assert.Contains("does not exist", missing.Explanation, StringComparison.OrdinalIgnoreCase);
    }
}
