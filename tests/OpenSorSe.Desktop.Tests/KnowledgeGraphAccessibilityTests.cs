using System.Xml.Linq;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Protects the bounded, non-canvas Knowledge Graph accessibility contract.</summary>
public sealed class KnowledgeGraphAccessibilityTests
{
    /// <summary>Verifies graph controls are named, keyboard reachable, and expose coalesced live state.</summary>
    [Fact]
    public void View_ExposesNamedKeyboardAndLiveStatusControls()
    {
        var source = ReadView();
        var document = XDocument.Parse(source);
        var names = document.Descendants()
            .Attributes()
            .Where(attribute => attribute.Name.LocalName == "AutomationProperties.Name")
            .Select(attribute => attribute.Value)
            .ToArray();

        Assert.True(names.Length >= 30, $"KnowledgeGraphView exposes only {names.Length} accessible names.");
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", source, StringComparison.Ordinal);
        Assert.Contains("Focusable=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding HelpCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ConfirmEnableCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ReconcileCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Update Knowledge Graph from current indexed data", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ConfirmPrivacyActionCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ConfirmRepairActionCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ConfirmDecisionActionCommand}\"", source, StringComparison.Ordinal);
        Assert.Contains("Add alias to selected Knowledge Graph entity", source, StringComparison.Ordinal);
        Assert.Contains("Bounded evidence-backed Knowledge Graph facts", source, StringComparison.Ordinal);
        Assert.Contains("Knowledge Graph background resource policy", source, StringComparison.Ordinal);
        Assert.Contains("Reviewable privacy-minimized Knowledge Graph diagnostics", source, StringComparison.Ordinal);
        Assert.Contains("Confirm bounded Knowledge Graph storage maintenance", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies the stable surface pages and virtualizes bounded lists instead of drawing a complete graph.</summary>
    [Fact]
    public void View_UsesPagedVirtualizedListsAndNoGraphCanvas()
    {
        var source = ReadView();

        Assert.Contains("PreviousPageCommand", source, StringComparison.Ordinal);
        Assert.Contains("NextPageCommand", source, StringComparison.Ordinal);
        Assert.Contains("PreviousNeighborPageCommand", source, StringComparison.Ordinal);
        Assert.Contains("NextNeighborPageCommand", source, StringComparison.Ordinal);
        Assert.True(
            source.Split("<VirtualizingStackPanel", StringSplitOptions.None).Length - 1 >= 2,
            "Both node and direct-neighbor lists must explicitly virtualize.");
        Assert.DoesNotContain("<Canvas", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("all graph", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Only one-hop results", source, StringComparison.Ordinal);
    }

    /// <summary>Verifies all destructive graph controls state that original files are outside their authority.</summary>
    [Fact]
    public void View_SeparatesPrivacyActionsAndProtectsOriginalFilesInWording()
    {
        var source = ReadView();

        Assert.Contains("Disable and retain graph data", source, StringComparison.Ordinal);
        Assert.Contains("Clear derived graph", source, StringComparison.Ordinal);
        Assert.Contains("Clear graph and decisions", source, StringComparison.Ordinal);
        Assert.Contains("Forget selected item", source, StringComparison.Ordinal);
        Assert.Contains("Forget selected source", source, StringComparison.Ordinal);
        Assert.Contains("Full derived rebuild", source, StringComparison.Ordinal);
        Assert.True(
            source.Split("Original files remain unchanged", StringSplitOptions.None).Length - 1 >= 4,
            "Every destructive section must repeat the source-file safety boundary.");
    }

    /// <summary>Verifies optional graph database initialization cannot delay creation of the main window.</summary>
    [Fact]
    public void App_StartsKnowledgeGraphOffTheUiThreadAfterCreatingMainWindow()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenSorSe.Desktop",
            "App.axaml.cs"));
        var windowIndex = source.IndexOf("desktop.MainWindow = new MainWindow", StringComparison.Ordinal);
        var startCallIndex = source.IndexOf("StartKnowledgeGraphInBackground(_serviceProvider)", StringComparison.Ordinal);
        var methodIndex = source.IndexOf("private void StartKnowledgeGraphInBackground", StringComparison.Ordinal);
        var stopMethodIndex = source.IndexOf("private bool StopKnowledgeGraphSafely", StringComparison.Ordinal);

        Assert.True(windowIndex >= 0 && startCallIndex > windowIndex);
        Assert.True(methodIndex >= 0 && stopMethodIndex > methodIndex);
        var startupMethod = source[methodIndex..stopMethodIndex];
        Assert.Contains("Task.Run(", startupMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter", startupMethod, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait(", startupMethod, StringComparison.Ordinal);
    }

    /// <summary>Verifies row announcements always include explicit non-color state and actual evidence fields.</summary>
    [Fact]
    public void RowModels_ExposeTextOnlyStateAndEvidence()
    {
        var node = new KnowledgeGraphNodeRow(
            "node-1",
            "Invoice",
            "File",
            "Synthetic source",
            "Stale",
            "Repair required",
            false,
            false,
            false,
            "source-1");
        var neighbor = new KnowledgeGraphNeighborRow(
            "edge-1",
            "node-2",
            "Receipt",
            "Related file",
            "High",
            "Mechanical algorithm 1",
            "Same retained invoice number",
            "Current",
            "Valid",
            false);

        Assert.Contains("Freshness: Stale", node.AccessibleText, StringComparison.Ordinal);
        Assert.Contains("Integrity: Repair required", node.AccessibleText, StringComparison.Ordinal);
        Assert.Contains("Confidence: High", neighbor.AccessibleText, StringComparison.Ordinal);
        Assert.Contains("Evidence: Same retained invoice number", neighbor.AccessibleText, StringComparison.Ordinal);
    }

    private static string ReadView() => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "OpenSorSe.Desktop",
        "Views",
        "KnowledgeGraphView.axaml"));

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
