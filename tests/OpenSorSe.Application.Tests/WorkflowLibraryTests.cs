#pragma warning disable CS1591

using System.Text.Json.Nodes;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

public sealed class WorkflowLibraryTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.WorkflowLibrary.Tests",
        Guid.NewGuid().ToString("N"));

    public WorkflowLibraryTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task BuiltInsAndUserLifecycle_AreDurableVersionedAndNonMutating()
    {
        var path = Path.Combine(_workspace, "workflow-library.json");
        var service = CreateService(path);

        var initialProfiles = await service.ListProfilesAsync(true, CancellationToken.None);
        var initialRecipes = await service.ListRecipesAsync(true, CancellationToken.None);
        Assert.Equal(5, initialProfiles.Count(profile => profile.IsBuiltIn));
        Assert.Equal(5, initialRecipes.Count(recipe => recipe.IsBuiltIn));
        var invoiceRecipe = initialRecipes.Single(
            recipe => recipe.Id == BuiltInWorkflowIds.InvoiceRecipe);
        var invoicePreview = new WorkflowTemplateEngine().Evaluate(
            invoiceRecipe,
            new RecipeEvaluationContext(
                _workspace,
                Path.Combine(_workspace, "invoice.pdf"),
                new Dictionary<string, RecipeFieldValue>
                {
                    ["filesystemModifiedDate"] = new("2026-07-26T00:00:00Z", "test metadata"),
                }));
        Assert.True(invoicePreview.IsValid);
        Assert.Contains(Path.Combine("Invoices", "2026"), invoicePreview.ProposedDestinationPath!);

        var duplicate = await service.DuplicateProfileAsync(
            BuiltInWorkflowIds.GeneralDocuments,
            "My documents",
            CancellationToken.None);
        var edited = await service.UpdateProfileAsync(
            duplicate with { Description = "Edited without changing the canonical built-in." },
            CancellationToken.None);
        var archived = await service.SetProfileArchivedAsync(edited.Id, true, CancellationToken.None);
        var restored = await service.SetProfileArchivedAsync(edited.Id, false, CancellationToken.None);

        Assert.False(duplicate.IsBuiltIn);
        Assert.Equal(2, edited.Revision);
        Assert.True(archived.IsArchived);
        Assert.False(restored.IsArchived);
        Assert.Equal(4, restored.Revision);
        Assert.Equal(
            "Balanced local processing for common office documents.",
            (await service.GetProfileAsync(BuiltInWorkflowIds.GeneralDocuments, CancellationToken.None))!.Description);

        var reloaded = CreateService(path);
        Assert.Equal(
            restored.Revision,
            (await reloaded.GetProfileAsync(restored.Id, CancellationToken.None))!.Revision);
        Assert.True(await reloaded.DeleteProfileAsync(restored.Id, CancellationToken.None));
        Assert.Null(await reloaded.GetProfileAsync(restored.Id, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_workspace, "*.tmp", SearchOption.TopDirectoryOnly));

        var failedSaveService = new WorkflowLibraryService(
            new FailingSaveStore(),
            new WorkflowValidator(new WorkflowTemplateEngine()),
            new EmptyUsageInspector());
        await Assert.ThrowsAsync<IOException>(() =>
            failedSaveService.DuplicateProfileAsync(
                BuiltInWorkflowIds.GeneralDocuments,
                "Cannot persist",
                CancellationToken.None));
        Assert.DoesNotContain(
            await failedSaveService.ListProfilesAsync(true, CancellationToken.None),
            profile => profile.Name == "Cannot persist");
    }

    [Fact]
    public async Task CorruptStorage_IsPreservedAndBuiltInsRemainAvailable()
    {
        var path = Path.Combine(_workspace, "workflow-library.json");
        const string corrupt = "{ this is not valid JSON";
        await File.WriteAllTextAsync(path, corrupt);
        var service = CreateService(path);

        var profiles = await service.ListProfilesAsync(false, CancellationToken.None);

        Assert.Equal(5, profiles.Count);
        Assert.NotNull(service.RecoveryMessage);
        Assert.Equal(corrupt, await File.ReadAllTextAsync(path));
        Assert.NotNull(service.PreservedCorruptCopyPath);
        Assert.True(File.Exists(service.PreservedCorruptCopyPath));
        Assert.Equal(corrupt, await File.ReadAllTextAsync(service.PreservedCorruptCopyPath!));

        var nestedPath = Path.Combine(_workspace, "workflow-library-nested.json");
        var validService = CreateService(nestedPath);
        await validService.DuplicateProfileAsync(
            BuiltInWorkflowIds.GeneralDocuments,
            "Malformed nested collection",
            CancellationToken.None);
        var nested = JsonNode.Parse(await File.ReadAllTextAsync(nestedPath))!.AsObject();
        nested["Profiles"]![0]!["Files"]!["IncludedFileTypes"] = null;
        await File.WriteAllTextAsync(nestedPath, nested.ToJsonString());
        var nestedRecovery = CreateService(nestedPath);
        Assert.Equal(
            5,
            (await nestedRecovery.ListProfilesAsync(false, CancellationToken.None)).Count);
        Assert.NotNull(nestedRecovery.RecoveryMessage);
        Assert.Null(nested["Profiles"]![0]!["Files"]!["IncludedFileTypes"]);
    }

    [Fact]
    public async Task SchemaOne_IsMigratedAtomicallyToCurrentSchema()
    {
        var path = Path.Combine(_workspace, "workflow-library.json");
        var service = CreateService(path);
        await service.DuplicateProfileAsync(
            BuiltInWorkflowIds.MinimalLocalProcessing,
            "Low cost",
            CancellationToken.None);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        json["SchemaVersion"] = 1;
        await File.WriteAllTextAsync(path, json.ToJsonString());

        var migrated = CreateService(path);
        await migrated.InitializeAsync(CancellationToken.None);
        var rewritten = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();

        Assert.Equal(
            WorkflowLibraryLimits.CurrentLibrarySchemaVersion,
            rewritten["SchemaVersion"]!.GetValue<int>());
        Assert.Contains(
            migrated.GetDiagnostics(),
            diagnostic => diagnostic.Kind == WorkflowDiagnosticKind.Migration);
        Assert.Empty(Directory.GetFiles(_workspace, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task DuplicateIdentityAndMissingDependencies_AreRejected()
    {
        var path = Path.Combine(_workspace, "workflow-library.json");
        var engine = new WorkflowTemplateEngine();
        var validator = new WorkflowValidator(engine);
        var store = new JsonWorkflowLibraryStore(path, validator, new LoggingService());
        var first = UserProfile("profile:one", "One");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(
                [first, first with { Name = "Two" }],
                [],
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(
                [first with { SortingRecipeIds = ["recipe:missing"] }],
                [],
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(
                [first with
                {
                    Id = BuiltInWorkflowIds.GeneralDocuments,
                    Name = "Built-in identity collision",
                }],
                [],
                CancellationToken.None));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ReferencedRecipe_CannotBeDeletedUntilDependencyIsResolved()
    {
        var service = CreateService(Path.Combine(_workspace, "workflow-library.json"));
        var recipe = await service.DuplicateRecipeAsync(
            BuiltInWorkflowIds.GeneralDocumentRecipe,
            "My filing recipe",
            CancellationToken.None);
        var profile = await service.CreateProfileAsync(
            UserProfile("profile:uses-recipe", "Uses recipe") with
            {
                SortingRecipeIds = [recipe.Id],
            },
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteRecipeAsync(recipe.Id, CancellationToken.None));

        Assert.Contains("referenced", error.Message, StringComparison.OrdinalIgnoreCase);
        await service.DeleteProfileAsync(profile.Id, CancellationToken.None);
        Assert.True(await service.DeleteRecipeAsync(recipe.Id, CancellationToken.None));
    }

    private static WorkflowLibraryService CreateService(string path)
    {
        var engine = new WorkflowTemplateEngine();
        var validator = new WorkflowValidator(engine);
        return new WorkflowLibraryService(
            new JsonWorkflowLibraryStore(path, validator, new LoggingService()),
            validator,
            new EmptyUsageInspector());
    }

    private static WorkflowProfile UserProfile(string id, string name) =>
        BuiltInWorkflowLibrary.Profiles[0] with
        {
            Id = id,
            Name = name,
            Description = "User profile",
            IsBuiltIn = false,
            Origin = new WorkflowProfileOrigin(WorkflowOriginKind.UserCreated),
            SortingRecipeIds = [],
        };

    private sealed class EmptyUsageInspector : IWorkflowUsageInspector
    {
        public Task<WorkflowUsageInfo> InspectAsync(string itemId, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowUsageInfo(itemId, [], [], 0));
    }

    private sealed class FailingSaveStore : IWorkflowLibraryStore
    {
        public Task<WorkflowLibraryLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowLibraryLoadResult([], [], null, null, false));

        public Task SaveAsync(
            IReadOnlyList<WorkflowProfile> profiles,
            IReadOnlyList<SortingRecipe> recipes,
            CancellationToken cancellationToken) =>
            Task.FromException(new IOException("Controlled persistence failure."));
    }
}
