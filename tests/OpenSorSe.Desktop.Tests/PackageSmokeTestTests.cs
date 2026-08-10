using OpenSorSe.Core.Platform;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Exercises the production composition root with isolated synthetic persistence.</summary>
public sealed class PackageSmokeTestTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.PackageSmoke.Tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>Verifies production startup and bounded shutdown complete without a UI.</summary>
    [Fact]
    public async Task RunAsync_IsolatedAbsoluteRoot_StartsAndStopsProductionServices()
    {
        var exitCode = await PackageSmokeTest.RunAsync(_dataRoot);

        Assert.Equal(0, exitCode);
        var expectedPaths = new ApplicationPathProvider(
            PlatformServices.CurrentPlatform,
            _ => null,
            _dataRoot,
            _dataRoot).Paths;
        Assert.All(
            new[]
            {
                expectedPaths.ConfigurationDirectory,
                expectedPaths.DataDirectory,
                expectedPaths.StateDirectory,
                expectedPaths.CacheDirectory,
                expectedPaths.DiagnosticsDirectory,
                expectedPaths.PluginDirectory,
            },
            path => Assert.True(Directory.Exists(path), $"Expected package-smoke directory: {path}"));
    }

    /// <summary>Verifies smoke validation cannot target an implicit or relative user-data path.</summary>
    [Fact]
    public async Task RunAsync_RelativeRoot_FailsBeforeWriting()
    {
        var exitCode = await PackageSmokeTest.RunAsync("relative-smoke-data");

        Assert.Equal(1, exitCode);
        Assert.False(Directory.Exists("relative-smoke-data"));
    }

    /// <summary>Removes only the isolated data created by this test instance.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
