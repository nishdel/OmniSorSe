#pragma warning disable CS1591

using System.Text.Json.Nodes;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

public sealed class WorkflowImportExportTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.WorkflowImport.Tests",
        Guid.NewGuid().ToString("N"));

    public WorkflowImportExportTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAndImportAsCopy_RoundTripsHumanInspectableConfigurationWithoutSecrets()
    {
        var (library, transfer) = CreateServices();
        var json = await transfer.ExportProfileAsync(
            BuiltInWorkflowIds.GeneralDocuments,
            CancellationToken.None);

        var result = await transfer.ImportAsync(
            json,
            WorkflowImportConflictPolicy.ImportAsCopy,
            CancellationToken.None);

        Assert.True(result.Imported);
        Assert.NotNull(result.ImportedId);
        var imported = await library.GetProfileAsync(result.ImportedId!, CancellationToken.None);
        Assert.NotNull(imported);
        Assert.False(imported.IsBuiltIn);
        Assert.Equal(WorkflowOriginKind.Imported, imported.Origin.Kind);
        Assert.Contains("\"ContentType\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ApplicationVersion\": \"1.3.0\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedModel", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Endpoint", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            library.GetDiagnostics(),
            diagnostic => diagnostic.Kind == WorkflowDiagnosticKind.Export);
        Assert.Contains(
            library.GetDiagnostics(),
            diagnostic => diagnostic.Kind == WorkflowDiagnosticKind.Import);
    }

    [Fact]
    public async Task Import_RejectsSchemaMismatchUnsafeTemplateAndMissingDependency()
    {
        var (_, transfer) = CreateServices();
        var recipeJson = await transfer.ExportRecipeAsync(
            BuiltInWorkflowIds.GeneralDocumentRecipe,
            CancellationToken.None);
        var invalidSchema = JsonNode.Parse(recipeJson)!.AsObject();
        invalidSchema["SchemaVersion"] = 99;
        var schemaResult = await transfer.ImportAsync(
            invalidSchema.ToJsonString(),
            WorkflowImportConflictPolicy.ImportAsCopy,
            CancellationToken.None);
        Assert.False(schemaResult.Imported);
        Assert.Contains("schema", schemaResult.Message, StringComparison.OrdinalIgnoreCase);

        var unsafeRecipe = JsonNode.Parse(recipeJson)!.AsObject();
        unsafeRecipe["Recipe"]!["DestinationTemplate"] = "../Outside";
        var unsafeResult = await transfer.ImportAsync(
            unsafeRecipe.ToJsonString(),
            WorkflowImportConflictPolicy.ImportAsCopy,
            CancellationToken.None);
        Assert.False(unsafeResult.Imported);
        Assert.Contains("travers", unsafeResult.Message, StringComparison.OrdinalIgnoreCase);

        var profileJson = await transfer.ExportProfileAsync(
            BuiltInWorkflowIds.GeneralDocuments,
            CancellationToken.None);
        var missingDependency = JsonNode.Parse(profileJson)!.AsObject();
        missingDependency["Profile"]!["SortingRecipeIds"] = new JsonArray("recipe:not-installed");
        missingDependency["DependencyReferences"] = new JsonArray("recipe:not-installed");
        var dependencyResult = await transfer.ImportAsync(
            missingDependency.ToJsonString(),
            WorkflowImportConflictPolicy.ImportAsCopy,
            CancellationToken.None);
        Assert.False(dependencyResult.Imported);
        Assert.Contains("unavailable", dependencyResult.Message, StringComparison.OrdinalIgnoreCase);

        missingDependency["ItemId"] = "profile:mismatched-envelope";
        var mismatchedEnvelope = await transfer.ImportAsync(
            missingDependency.ToJsonString(),
            WorkflowImportConflictPolicy.ImportAsCopy,
            CancellationToken.None);
        Assert.False(mismatchedEnvelope.Imported);
        Assert.Contains("payload", mismatchedEnvelope.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_EnforcesConflictChoiceDepthAndSizeBounds()
    {
        var (_, transfer) = CreateServices();
        var json = await transfer.ExportRecipeAsync(
            BuiltInWorkflowIds.GeneralDocumentRecipe,
            CancellationToken.None);

        var cancelled = await transfer.ImportAsync(
            json,
            WorkflowImportConflictPolicy.Cancel,
            CancellationToken.None);
        var replaceBuiltIn = await transfer.ImportAsync(
            json,
            WorkflowImportConflictPolicy.ReplaceUserCreated,
            CancellationToken.None);
        var oversized = await transfer.ImportAsync(
            new string('x', checked((int)WorkflowLibraryLimits.MaximumImportBytes + 1)),
            WorkflowImportConflictPolicy.ImportAsCopy,
            CancellationToken.None);
        var deeplyNested = await transfer.ImportAsync(
            new string('[', 40) + "0" + new string(']', 40),
            WorkflowImportConflictPolicy.ImportAsCopy,
            CancellationToken.None);

        Assert.False(cancelled.Imported);
        Assert.Contains("cancelled", cancelled.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(replaceBuiltIn.Imported);
        Assert.Contains("cannot be replaced", replaceBuiltIn.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(oversized.Imported);
        Assert.Contains("size", oversized.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(deeplyNested.Imported);
        Assert.Contains("deep", deeplyNested.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Import_RejectsDestructiveOrUnsupportedRecipeRuleCapability()
    {
        var (_, transfer) = CreateServices();
        var json = await transfer.ExportRecipeAsync(
            BuiltInWorkflowIds.GeneralDocumentRecipe,
            CancellationToken.None);
        var root = JsonNode.Parse(json)!.AsObject();
        root["Recipe"]!["Rules"] = JsonNode.Parse(
            """
            [
              {
                "Id": "rule:delete",
                "Name": "Delete",
                "Priority": 1,
                "Conditions": [
                  { "Kind": "ExtensionEquals", "StringValue": ".pdf" }
                ],
                "Action": { "Kind": "Delete" },
                "IsEnabled": true
              }
            ]
            """);

        var result = await transfer.ImportAsync(
            root.ToJsonString(),
            WorkflowImportConflictPolicy.ImportAsCopy,
            CancellationToken.None);

        Assert.False(result.Imported);
        Assert.Contains("non-destructive", result.Message, StringComparison.OrdinalIgnoreCase);

        root["Recipe"]!["Rules"]![0]!["Action"] = JsonNode.Parse(
            """{ "Kind": "Move", "DestinationPath": "C:\\Outside" }""");
        var externalMove = await transfer.ImportAsync(
            root.ToJsonString(),
            WorkflowImportConflictPolicy.ImportAsCopy,
            CancellationToken.None);
        Assert.False(externalMove.Imported);
        Assert.Contains("declarative", externalMove.Message, StringComparison.OrdinalIgnoreCase);
    }

    private (WorkflowLibraryService Library, WorkflowImportExportService Transfer) CreateServices()
    {
        var engine = new WorkflowTemplateEngine();
        var validator = new WorkflowValidator(engine);
        var library = new WorkflowLibraryService(
            new JsonWorkflowLibraryStore(
                Path.Combine(_workspace, "library.json"),
                validator,
                new LoggingService()),
            validator,
            new EmptyUsageInspector());
        return (library, new WorkflowImportExportService(library, validator));
    }

    private sealed class EmptyUsageInspector : IWorkflowUsageInspector
    {
        public Task<WorkflowUsageInfo> InspectAsync(string itemId, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowUsageInfo(itemId, [], [], 0));
    }
}
