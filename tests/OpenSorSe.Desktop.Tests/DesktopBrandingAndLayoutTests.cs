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
        Assert.Equal("OmniSorSe", DesktopBranding.ProductName);
        Assert.Equal("OMNI SORT AND SEARCH", DesktopBranding.ExpandedName);
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
            ["SemanticSearchView.axaml"] = 10,
            ["CollectionsView.axaml"] = 12,
            ["KnowledgeGraphView.axaml"] = 30,
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
                     "SemanticSearchView.axaml",
                     "CollectionsView.axaml",
                     "KnowledgeGraphView.axaml",
                 })
        {
            var source = File.ReadAllText(Path.Combine(viewsDirectory, fileName));
            Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", source, StringComparison.Ordinal);
        }
    }

    /// <summary>Verifies relationship explanations and index-only controls are keyboard and screen-reader reachable.</summary>
    [Fact]
    public void CollectionsView_ExposesEvidencePrivacyAndManualControlsAccessibly()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenSorSe.Desktop",
            "Views",
            "CollectionsView.axaml"));

        Assert.Contains("Relationship.Explanation", source, StringComparison.Ordinal);
        Assert.Contains("Relationship.Algorithm", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmRelationshipCommand", source, StringComparison.Ordinal);
        Assert.Contains("AlwaysRelateRelationshipCommand", source, StringComparison.Ordinal);
        Assert.Contains("NeverRelateCommand", source, StringComparison.Ordinal);
        Assert.Contains("MergeCollectionCommand", source, StringComparison.Ordinal);
        Assert.Contains("SplitMemberCommand", source, StringComparison.Ordinal);
        Assert.Contains("ForgetFileRelationshipsCommand", source, StringComparison.Ordinal);
        Assert.Contains("ForgetSourceRelationshipsCommand", source, StringComparison.Ordinal);
        Assert.Contains("original file remains unchanged", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AutomationProperties.Name", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies collection and Related Files layouts retain bounded keyboard-scroll regions.</summary>
    [Fact]
    public void RelationshipViews_UseOneVisibleDestinationAndBoundedScrollableContent()
    {
        var views = Path.Combine(FindRepositoryRoot(), "src", "OpenSorSe.Desktop", "Views");
        var collections = File.ReadAllText(Path.Combine(views, "CollectionsView.axaml"));
        var related = File.ReadAllText(Path.Combine(views, "KnowledgeGraphView.axaml"));

        Assert.Contains("RowDefinitions=\"Auto,*\"", collections, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Scrollable Smart Collections\"", collections, StringComparison.Ordinal);
        Assert.Contains("Header=\"Related Files\" IsVisible=\"False\"", collections, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment=\"Stretch\"", related, StringComparison.Ordinal);
        Assert.Contains("<ScrollViewer Grid.Column=\"1\">", related, StringComparison.Ordinal);
        Assert.Contains("<VirtualizingStackPanel", related, StringComparison.Ordinal);
    }

    /// <summary>Verifies scan-depth choices use outcome language and are exposed to accessibility APIs.</summary>
    [Fact]
    public void ProgressiveIndexing_UsesPlainLanguageAccessibleChoicesAndHelp()
    {
        var views = Path.Combine(FindRepositoryRoot(), "src", "OpenSorSe.Desktop", "Views");
        var scan = File.ReadAllText(Path.Combine(views, "FolderSelectionView.axaml"));
        var settings = File.ReadAllText(Path.Combine(views, "SettingsView.axaml"));
        var labels = InitialScanDepthOptions.All.Select(option => option.Label).ToArray();
        var help = HelpCatalog.Get(HelpTopicId.SemanticSearch);

        Assert.Equal(["Fast — searchable first", "Deep initial analysis"], labels);
        Assert.Contains("AutomationProperties.Name=\"Initial indexing schedule\"", scan, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Initial scan scheduling\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("BaseFirst makes", settings, StringComparison.Ordinal);
        Assert.Contains(help.Workflow, step => step.Contains("searchable first", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("deeper analysis", help.CommonErrors, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies Search naming and help remain plain-language and available beyond pointer hover.</summary>
    [Fact]
    public void SearchView_UsesAccessiblePlainLanguageHelpAndNoLegacyMeaningLabel()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenSorSe.Desktop",
            "Views",
            "SemanticSearchView.axaml");
        var source = File.ReadAllText(path);
        var document = XDocument.Parse(source);
        var help = Assert.Single(
            document.Descendants(),
            element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name" &&
                attribute.Value == "About Search coverage"));

        Assert.Equal("True", help.Attribute("Focusable")?.Value);
        Assert.NotNull(help.Attributes().SingleOrDefault(attribute =>
            attribute.Name.LocalName == "ToolTip.Tip"));
        Assert.Contains(
            "document text",
            help.Attributes().Single(attribute =>
                attribute.Name.LocalName == "AutomationProperties.HelpText").Value,
            StringComparison.Ordinal);
        Assert.NotNull(help.Attribute("Command"));
        Assert.Contains("Text=\"Search your files\"", source, StringComparison.Ordinal);
        Assert.Contains("Search filenames, contents, images, audio and video", source, StringComparison.Ordinal);
        Assert.Contains("Include broader Related Files context in Search", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SearchModeText}\"", source, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IncludeGraphContext, Mode=TwoWay}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding GraphCoverageText}\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Meaning Search", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("embedding", source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies major persistent-workflow pages expose keyboard-addressable contextual Help.</summary>
    [Theory]
    [InlineData("WatchedFoldersView.axaml", "Watched Folders help")]
    [InlineData("WorkflowsView.axaml", "Workflows help")]
    public void MajorWorkflowPages_ExposeContextualHelp(string fileName, string accessibleName)
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenSorSe.Desktop",
            "Views",
            fileName));

        Assert.Contains("Command=\"{Binding HelpCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains($"AutomationProperties.Name=\"{accessibleName}\"", source, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies v1.8 result evidence and privacy actions work beyond pointer hover.</summary>
    [Fact]
    public void SearchView_ExposesKeyboardAndClickResultEvidenceAndPrivacyActions()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenSorSe.Desktop",
            "Views",
            "SemanticSearchView.axaml"));

        Assert.Contains("Header=\"Why this result?\"", source, StringComparison.Ordinal);
        Assert.Contains("InspectIndexedDataCommand", source, StringComparison.Ordinal);
        Assert.Contains("RemoveFilterCommand", source, StringComparison.Ordinal);
        Assert.Contains("ClearFiltersCommand", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmForgetFileCommand", source, StringComparison.Ordinal);
        Assert.Contains("Snippet.AccessibleText", source, StringComparison.Ordinal);
        Assert.Contains("Snippet.MatchedText", source, StringComparison.Ordinal);
        Assert.Contains("SourceIndicator", source, StringComparison.Ordinal);
        Assert.Contains("CoverageIndicator", source, StringComparison.Ordinal);
        Assert.Contains("VerifyFileCommand", source, StringComparison.Ordinal);
        Assert.Contains("RefreshMetadataCommand", source, StringComparison.Ordinal);
        Assert.Contains("RefreshTextCommand", source, StringComparison.Ordinal);
        Assert.Contains("RefreshOcrCommand", source, StringComparison.Ordinal);
        Assert.Contains("RegenerateSummaryCommand", source, StringComparison.Ordinal);
        Assert.Contains("RegenerateSemanticCommand", source, StringComparison.Ordinal);
        Assert.Contains("CopyFullPathCommand", source, StringComparison.Ordinal);
        Assert.Contains("UseAiAssistance", source, StringComparison.Ordinal);
        Assert.Contains("IsBackgroundProgressIndeterminate", source, StringComparison.Ordinal);
        Assert.Contains("cannot add files", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("original source file is never deleted or modified", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AutomationProperties.Name", source, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"{Binding}\"", source, StringComparison.Ordinal);
    }

    /// <summary>Facets and Saved Views expose bounded scrolling, live coverage, and named keyboard actions.</summary>
    [Fact]
    public void SearchView_FacetedDiscoveryIsBoundedAndAccessible()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenSorSe.Desktop",
            "Views",
            "SemanticSearchView.axaml"));

        Assert.Contains("AutomationProperties.Name=\"Available Search facets\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"300\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Clear all active Search filters\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Show files with unresolved Moderate Smart Tag suggestions\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Save current query and filters as a new Saved View\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Open selected Saved View\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Update selected Saved View from current query and filters\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Delete selected Saved View\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CandidateCoverageText}\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", source, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("The OmniSorSe repository root could not be located.");
    }
}
