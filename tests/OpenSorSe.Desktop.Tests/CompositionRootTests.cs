using System.Reflection;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies the production dependency graph can be constructed without launching Avalonia.</summary>
public sealed class CompositionRootTests
{
    /// <summary>Resolves the production shell while the service provider validates every registration.</summary>
    [Fact]
    public async Task CreateServiceProvider_ResolvesMainViewModelAndDisposesAsync()
    {
        var factory = typeof(App).GetMethod(
            "CreateServiceProvider",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(factory);
        var provider = Assert.IsAssignableFrom<IServiceProvider>(factory.Invoke(null, null));

        try
        {
            var main = Assert.IsType<MainViewModel>(provider.GetService(typeof(MainViewModel)));
            var graphLifecycle = Assert.IsType<SqliteGraphStorageLifecycle>(
                provider.GetService(typeof(IGraphStorageLifecycle)));
            Assert.IsType<RelationshipGraphAuthorityBridge>(
                provider.GetService(typeof(IGraphLegacyAuthorityBridge)));
            Assert.IsType<GraphDecisionService>(provider.GetService(typeof(IGraphDecisionService)));
            Assert.NotNull(main.Settings.PlatformDiagnostics);
            Assert.NotNull(graphLifecycle);
            Assert.Equal(
                Enum.GetValues<PlatformCapabilityKind>().Length,
                main.Settings.PlatformDiagnostics.Capabilities.Count);
        }
        finally
        {
            if (provider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                (provider as IDisposable)?.Dispose();
            }
        }
    }
}
