using OpenSorSe.Core.Platform;

namespace OpenSorSe.Core.Tests;

/// <summary>Verifies current release packaging diagnostics match the active host.</summary>
public sealed class PlatformCapabilityProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.PlatformCapability.Tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>Verifies package availability and trust limitations are reported truthfully.</summary>
    [Fact]
    public void PackagingCapability_MatchesCurrentPlatformReleasePolicy()
    {
        var paths = new ApplicationPathProvider(
            PlatformServices.CurrentPlatform,
            _ => null,
            _root,
            _root);
        var provider = new PlatformCapabilityProvider(
            paths,
            FileIdentityProviderFactory.CreateCurrent(),
            new ExternalToolLocator());

        var capability = provider.Get(PlatformCapabilityKind.PackagingAndUpdates);

        if (provider.Platform is HostPlatformKind.Windows or HostPlatformKind.MacOS)
        {
            Assert.Equal(PlatformSupportState.SupportedWithLimitations, capability.State);
            Assert.Contains("unsigned", capability.Explanation, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("no automatic updater", capability.Explanation, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal(PlatformSupportState.Unavailable, capability.State);
            Assert.Contains("does not publish", capability.Explanation, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Removes only synthetic application paths created by this test.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
