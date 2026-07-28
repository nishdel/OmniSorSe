using OpenSorSe.Core.Configuration;
using OpenSorSe.Desktop.ViewModels;
using System.Text;
using System.Xml.Linq;

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

    /// <summary>Verifies critical workflows expose named controls and polite live-region status updates.</summary>
    [Fact]
    public void Accessibility_CriticalViewsExposeNamesAndLiveStatus()
    {
        var views = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["MainWindow.axaml"] = 10,
            ["ScanProgressView.axaml"] = 4,
            ["WatchedFoldersView.axaml"] = 10,
            ["ChangePlanReviewView.axaml"] = 7,
            ["UndoHistoryView.axaml"] = 3,
            ["PluginsView.axaml"] = 5,
            ["NotificationCenterView.axaml"] = 2,
        };
        var viewsDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenSorSe.Desktop",
            "Views");

        foreach (var (fileName, minimumNamedControls) in views)
        {
            var document = XDocument.Load(Path.Combine(viewsDirectory, fileName));
            var names = document
                .Descendants()
                .Attributes()
                .Where(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
                .Select(attribute => attribute.Value)
                .ToArray();

            Assert.True(
                names.Length >= minimumNamedControls,
                $"{fileName} exposes only {names.Length} accessible names.");
            Assert.All(names, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        }

        foreach (var fileName in new[]
                 {
                     "MainWindow.axaml",
                     "ScanProgressView.axaml",
                     "WatchedFoldersView.axaml",
                     "ChangePlanReviewView.axaml",
                     "UndoHistoryView.axaml",
                     "PluginsView.axaml",
                     "NotificationCenterView.axaml",
                 })
        {
            var source = File.ReadAllText(Path.Combine(viewsDirectory, fileName));
            Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", source, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenSorSe.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The OpenSorSe repository root could not be located.");
    }
}
