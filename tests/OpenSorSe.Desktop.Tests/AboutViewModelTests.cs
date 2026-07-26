using OpenSorSe.Desktop.ViewModels;
using System.Reflection;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies static application metadata and safe external-link requests.
/// </summary>
public sealed class AboutViewModelTests
{
    /// <summary>
    /// Verifies current application metadata is deterministic and non-empty.
    /// </summary>
    [Fact]
    public void Constructor_ExposesDeclaredApplicationMetadata()
    {
        var viewModel = new AboutViewModel();

        Assert.Equal("OpenSorSe", viewModel.ApplicationName);
        Assert.Equal("1.0.0", viewModel.Version);
        Assert.Equal(new Version(1, 0, 0, 0), typeof(AboutViewModel).Assembly.GetName().Version);
        Assert.Equal(
            "1.0.0",
            typeof(AboutViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion);
        Assert.Equal("MIT License", viewModel.License);
        Assert.NotEmpty(viewModel.Acknowledgements);
    }

    /// <summary>
    /// Verifies the view model exposes only copyable vetted HTTPS addresses and no launch command.
    /// </summary>
    [Fact]
    public void ExternalAddresses_AreVettedHttpsValuesWithoutLaunchCommands()
    {
        var viewModel = new AboutViewModel();
        var addresses = new[] { viewModel.RepositoryAddress, viewModel.DocumentationAddress };

        Assert.All(addresses, address => Assert.Equal(Uri.UriSchemeHttps, new Uri(address, UriKind.Absolute).Scheme));
        Assert.DoesNotContain(
            typeof(AboutViewModel).GetProperties(),
            property =>
                property.Name.EndsWith("Command", StringComparison.Ordinal) &&
                !string.Equals(property.Name, "HelpCommand", StringComparison.Ordinal));
    }
}
