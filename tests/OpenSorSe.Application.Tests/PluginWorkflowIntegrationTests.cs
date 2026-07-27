#pragma warning disable CS1591

using OpenSorSe.Application.Models;
using OpenSorSe.Application.Plugins;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

public sealed class PluginWorkflowIntegrationTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.PluginWorkflow.Tests",
        Guid.NewGuid().ToString("N"));

    public PluginWorkflowIntegrationTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }

        DeleteEmptyParent(_workspace);
    }

    [Fact]
    public async Task Resolver_ActiveContributionResolvesAndMissingContributionNeverFallsBack()
    {
        var (library, profile, _, reference) = await LibraryWithPluginRecipeAsync();
        var diagnostics = new PluginDiagnostics();
        var registry = RegistryWithReference(reference, diagnostics);
        var resolver = new WorkflowConfigurationResolver(
            library,
            new FixedConfiguration(),
            pluginResolver: new PluginContributionResolver(registry),
            pluginRegistry: registry);

        var available = await resolver.ResolveForManualScanAsync(
            profile.Id,
            null,
            CancellationToken.None);
        var missingResolver = new WorkflowConfigurationResolver(
            library,
            new FixedConfiguration(),
            pluginResolver: new PluginContributionResolver(
                new PluginContributionRegistry(new PluginDiagnostics())));
        var missing = await missingResolver.ResolveForManualScanAsync(
            profile.Id,
            null,
            CancellationToken.None);

        Assert.True(available.IsAvailable);
        var snapshot = Assert.Single(available.Configuration!.Snapshot.PluginContributions);
        Assert.Equal(reference.PluginId, snapshot.PluginId);
        Assert.Equal(reference.PluginVersion, snapshot.PluginVersion);
        Assert.False(missing.IsAvailable);
        Assert.Contains("Plugin capability unavailable", missing.Message, StringComparison.Ordinal);
        Assert.Null(missing.Configuration);
    }

    [Fact]
    public async Task RecipeField_CreatesNormalUnapprovedChangePlanWithPluginProvenance()
    {
        var (library, profile, recipe, reference) = await LibraryWithPluginRecipeAsync();
        var diagnostics = new PluginDiagnostics();
        var registry = RegistryWithReference(reference, diagnostics);
        var resolver = new WorkflowConfigurationResolver(
            library,
            new FixedConfiguration(),
            pluginResolver: new PluginContributionResolver(registry),
            pluginRegistry: registry);
        var resolved = (await resolver.ResolveForManualScanAsync(
            profile.Id,
            null,
            CancellationToken.None)).Configuration!;
        var source = Path.Combine(_workspace, "report.pdf");
        await File.WriteAllTextAsync(source, "unchanged");
        var info = new FileInfo(source);
        var file = new ResultFile(
            "file:plugin",
            source,
            info.Name,
            ".pdf",
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            FileCategory.Document,
            "Document",
            DuplicateStatus.Unique,
            null,
            false);
        var changePlanStore = new JsonChangePlanStore(
            Path.Combine(_workspace, "plans.json"),
            new LoggingService());
        var fileSystem = new PhysicalFileSystemGateway();
        var host = new PluginExtensionHost(registry, diagnostics);
        var service = new WorkflowRecipePlanService(
            new WorkflowTemplateEngine(),
            new ChangePlanFactory(
                fileSystem,
                new ChangePlanValidator(fileSystem),
                changePlanStore),
            new PluginRecipeFieldService(host, registry, diagnostics));

        var result = await service.CreatePlanAsync(
            resolved,
            _workspace,
            "scan:plugin",
            [file],
            CancellationToken.None);

        var plan = Assert.IsType<ChangePlan>(result.Plan);
        var action = Assert.Single(
            plan.Actions,
            value => value.ActionType is ChangeActionType.MoveFile or ChangeActionType.RenameFile);
        Assert.Equal(ChangeApprovalState.Pending, action.ApprovalState);
        Assert.Equal(ChangeValidationState.Valid, action.ValidationState);
        var provenance = Assert.IsType<ChangeWorkflowProvenance>(action.WorkflowProvenance);
        var plugin = Assert.Single(provenance.PluginContributions);
        Assert.Equal(reference.PluginId, plugin.PluginId);
        Assert.Equal(reference.PluginVersion, plugin.PluginVersion);
        Assert.Equal(reference.ContributionId, plugin.ContributionId);
        Assert.False(plugin.IsAiAssisted);
        Assert.True(File.Exists(source));
        Assert.False(Directory.Exists(Path.Combine(_workspace, "Plugin")));
        var persisted = Assert.Single(await changePlanStore.ListAsync(CancellationToken.None));
        Assert.All(persisted.Actions, value =>
            Assert.Single(value.WorkflowProvenance!.PluginContributions));
        Assert.Contains(
            PluginRecipeFieldService.FieldName(reference.PluginId, reference.ContributionId),
            recipe.RequiredFields);
    }

    [Fact]
    public void TemplateEngine_PluginFieldStillAppliesTraversalAndExecutableSyntaxProtections()
    {
        var engine = new WorkflowTemplateEngine();
        var reference = Reference();
        var fieldName = PluginRecipeFieldService.FieldName(
            reference.PluginId,
            reference.ContributionId);
        var baseRecipe = BuiltInWorkflowLibrary.Recipes[0];
        var recipe = baseRecipe with
        {
            Id = "recipe:plugin-safety",
            RequiredFields = [fieldName],
            NamingTemplate = $"{{{fieldName}}}",
            DestinationTemplate = $"{{{fieldName}}}",
            PluginFieldContributions = [reference],
        };
        var root = Path.GetFullPath(_workspace);
        var source = Path.Combine(root, "source.txt");
        var result = engine.Evaluate(
            recipe,
            new RecipeEvaluationContext(
                root,
                source,
                new Dictionary<string, RecipeFieldValue>
                {
                    [fieldName] = new RecipeFieldValue(
                        "..\\..\\outside|$(whoami).ps1",
                        "plugin"),
                }));

        Assert.True(result.IsValid);
        Assert.NotNull(result.ProposedDestinationPath);
        Assert.StartsWith(root, result.ProposedDestinationPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.GetRelativePath(root, result.ProposedDestinationPath)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            segment => segment == "..");
        Assert.DoesNotContain("|", result.ProposedDestinationPath);
        Assert.DoesNotContain("\\", result.ProposedFileName);
        Assert.EndsWith(".txt", result.ProposedFileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangedPluginVersion_RequiresExplicitReferenceReviewAndHistoricalSnapshotStaysUnchanged()
    {
        var (library, profile, _, reference) = await LibraryWithPluginRecipeAsync();
        var diagnostics = new PluginDiagnostics();
        var registry = RegistryWithReference(reference, diagnostics);
        var resolver = new WorkflowConfigurationResolver(
            library,
            new FixedConfiguration(),
            pluginResolver: new PluginContributionResolver(registry),
            pluginRegistry: registry);
        var first = await resolver.ResolveForManualScanAsync(
            profile.Id,
            null,
            CancellationToken.None);
        var historical = Assert.Single(first.Configuration!.Snapshot.PluginContributions);
        registry.RemovePlugin(reference.PluginId, reference.PluginVersion!);

        var unavailable = await resolver.ResolveForManualScanAsync(
            profile.Id,
            null,
            CancellationToken.None);

        Assert.False(unavailable.IsAvailable);
        Assert.Equal("1.4.0", historical.PluginVersion);
        Assert.Single(first.Configuration.Snapshot.PluginContributions);
    }

    [Fact]
    public async Task WatchedResolution_MissingPluginFailsClosedAndRecordsBatchFacingDiagnostic()
    {
        var (library, profile, recipe, _) = await LibraryWithPluginRecipeAsync();
        var diagnostics = new PluginDiagnostics();
        var registry = new PluginContributionRegistry(diagnostics);
        var resolver = new WorkflowConfigurationResolver(
            library,
            new FixedConfiguration(),
            pluginResolver: new PluginContributionResolver(registry),
            pluginRegistry: registry,
            pluginDiagnostics: diagnostics);
        var configuration = new OpenSorSe.Application.Watching.WatchedFolderConfiguration(
            "watch:plugin",
            _workspace,
            "Plugin watch",
            true,
            true,
            [],
            [],
            profile.Id,
            recipe.Id,
            true,
            false,
            new OpenSorSe.Application.Watching.WatchedFolderNotificationPreferences(),
            TimeSpan.FromSeconds(1),
            null,
            null,
            OpenSorSe.Application.Watching.WatchedFolderStatus.Watching,
            "catalogue:plugin")
        {
            SortingRecipeIds = [recipe.Id],
        };

        var result = await resolver.ResolveForWatchedFolderAsync(
            configuration,
            CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("Plugin capability unavailable", result.Message, StringComparison.Ordinal);
        Assert.Contains(
            diagnostics.List(),
            entry => entry.Kind == PluginDiagnosticKind.WatchedFolderResolution);
    }

    [Fact]
    public void WorkflowValidation_RequiresExactPluginVersionReferences()
    {
        var reference = Reference() with { PluginVersion = null };
        var recipe = BuiltInWorkflowLibrary.Recipes[0] with
        {
            Id = "recipe:unversioned-plugin",
            IsBuiltIn = false,
            PluginFieldContributions = [reference],
        };

        var result = new WorkflowValidator(new WorkflowTemplateEngine()).ValidateRecipe(recipe);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == "plugin.references");
    }

    private async Task<(
        WorkflowLibraryService Library,
        WorkflowProfile Profile,
        SortingRecipe Recipe,
        PluginContributionReference Reference)> LibraryWithPluginRecipeAsync()
    {
        var engine = new WorkflowTemplateEngine();
        var validator = new WorkflowValidator(engine);
        var library = new WorkflowLibraryService(
            new JsonWorkflowLibraryStore(
                Path.Combine(_workspace, $"library-{Guid.NewGuid():N}.json"),
                validator,
                new LoggingService()),
            validator,
            new EmptyUsageInspector());
        var reference = Reference();
        var fieldName = PluginRecipeFieldService.FieldName(
            reference.PluginId,
            reference.ContributionId);
        var recipe = await library.CreateRecipeAsync(
            BuiltInWorkflowLibrary.Recipes[0] with
            {
                Id = $"recipe:plugin-{Guid.NewGuid():N}",
                Name = $"Plugin recipe {Guid.NewGuid():N}",
                IsBuiltIn = false,
                Origin = new WorkflowProfileOrigin(WorkflowOriginKind.UserCreated),
                NamingTemplate = $"{{{fieldName}}}-{{originalName}}",
                DestinationTemplate = "Plugin",
                RequiredFields = [fieldName],
                OptionalFields = [],
                PluginFieldContributions = [reference],
            },
            CancellationToken.None);
        var profile = await library.CreateProfileAsync(
            BuiltInWorkflowLibrary.Profiles[0] with
            {
                Id = $"profile:plugin-{Guid.NewGuid():N}",
                Name = $"Plugin profile {Guid.NewGuid():N}",
                IsBuiltIn = false,
                Origin = new WorkflowProfileOrigin(WorkflowOriginKind.UserCreated),
                SortingRecipeIds = [recipe.Id],
            },
            CancellationToken.None);
        return (library, profile, recipe, reference);
    }

    private static PluginContributionReference Reference() =>
        new(
            "opensorse.reference.recipe-fields",
            "1.4.0",
            "reference.standard-extension-group",
            OpenSorSe.Extensions.Abstractions.ExtensionPointKind.RecipeFieldProvider);

    private static PluginContributionRegistry RegistryWithReference(
        PluginContributionReference reference,
        IPluginDiagnostics diagnostics)
    {
        var registry = new PluginContributionRegistry(diagnostics);
        var definition = BuiltInPluginCatalog.Definitions.Single(value =>
            value.Manifest.PluginId == reference.PluginId);
        var plugin = new ReferenceRecipeFieldPlugin();
        var initialized = plugin.InitializeAsync(
            new OpenSorSe.Extensions.Abstractions.PluginInitializationContext(
                new OpenSorSe.Extensions.Abstractions.PluginIdentity(
                    reference.PluginId,
                    reference.PluginVersion!,
                    definition.Manifest.DisplayName,
                    true),
                new HashSet<OpenSorSe.Extensions.Abstractions.PluginCapability>(
                    definition.Manifest.Capabilities),
                "1.4.0"),
            CancellationToken.None).GetAwaiter().GetResult();
        var descriptor = new PluginDescriptor(
            definition.Manifest,
            "built-in",
            new PluginProvenance(PluginOriginKind.BuiltIn, "tests", DateTimeOffset.UtcNow),
            PluginLifecycleState.Active,
            PluginCompatibilityState.Compatible,
            PluginIntegrityStatus.NotApplicable,
            null,
            true,
            true,
            new HashSet<OpenSorSe.Extensions.Abstractions.PluginCapability>(
                definition.Manifest.Capabilities),
            [],
            null,
            false);
        Assert.True(registry.Register(descriptor, initialized.Value!).Succeeded);
        return registry;
    }

    private static void DeleteEmptyParent(string path)
    {
        try
        {
            var parent = Directory.GetParent(path);
            if (parent?.Exists == true &&
                !parent.EnumerateFileSystemInfos().Any())
            {
                parent.Delete();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Parallel test cleanup may create another child after the empty check.
        }
    }

    private sealed class FixedConfiguration : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new();
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyUsageInspector : IWorkflowUsageInspector
    {
        public Task<WorkflowUsageInfo> InspectAsync(
            string itemId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowUsageInfo(itemId, [], [], 0));
    }
}
