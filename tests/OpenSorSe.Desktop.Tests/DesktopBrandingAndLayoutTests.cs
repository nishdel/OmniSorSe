using OpenSorSe.Core.Configuration;
using OpenSorSe.Desktop.ViewModels;
using System.Text;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies release-candidate shell branding and bounded Files-page layout contracts.</summary>
public sealed class DesktopBrandingAndLayoutTests
{
    /// <summary>Verifies the desktop shell exposes the approved concise product identity and packaged icon.</summary>
    [Fact]
    public void Branding_UsesOfficialPackagedIdentity()
    {
        Assert.Equal("OpenSorSe", DesktopBranding.ProductName);
        Assert.Equal("OPEN SORT AND SEARCH", DesktopBranding.ExpandedName);
        Assert.Equal("Find clarity in your files", DesktopBranding.Tagline);
        var assembly = typeof(DesktopBranding).Assembly;
        var resourceName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.Contains("AvaloniaResources", StringComparison.Ordinal));
        using var resourceStream = Assert.IsAssignableFrom<Stream>(assembly.GetManifestResourceStream(resourceName));
        using var memory = new MemoryStream();
        resourceStream.CopyTo(memory);
        Assert.Contains(
            "opensorse-app-icon.png",
            Encoding.UTF8.GetString(memory.ToArray()),
            StringComparison.Ordinal);
    }

    /// <summary>Verifies the splitter contract protects both panes and supports a responsive star-width range.</summary>
    [Fact]
    public void FilesLayout_UsesSafeMinimumWidthsAndResponsiveRatioBounds()
    {
        Assert.Equal(450, ResultsViewModel.MinimumFileTableWidth);
        Assert.Equal(320, ResultsViewModel.MinimumDetailsPanelWidth);
        Assert.InRange(
            FeatureSettings.DefaultFilesPageDetailsPanelWidthRatio,
            FeatureSettings.MinimumFilesPageDetailsPanelWidthRatio,
            FeatureSettings.MaximumFilesPageDetailsPanelWidthRatio);
        Assert.True(FeatureSettings.MinimumFilesPageDetailsPanelWidthRatio > 0);
        Assert.True(FeatureSettings.MaximumFilesPageDetailsPanelWidthRatio < 1);
    }
}
