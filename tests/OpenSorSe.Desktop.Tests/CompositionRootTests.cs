using System.Reflection;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies the production dependency graph can be constructed without launching Avalonia.</summary>
public sealed class CompositionRootTests
{
    /// <summary>Resolves the production shell while the service provider validates every registration.</summary>
    [Fact]
    public void CreateServiceProvider_ResolvesMainViewModel()
    {
        var factory = typeof(App).GetMethod(
            "CreateServiceProvider",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(factory);
        var provider = Assert.IsAssignableFrom<IServiceProvider>(factory.Invoke(null, null));

        try
        {
            var main = Assert.IsType<MainViewModel>(provider.GetService(typeof(MainViewModel)));
            Assert.NotNull(main.Settings.PlatformDiagnostics);
            Assert.Equal(
                Enum.GetValues<PlatformCapabilityKind>().Length,
                main.Settings.PlatformDiagnostics.Capabilities.Count);
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }
}
