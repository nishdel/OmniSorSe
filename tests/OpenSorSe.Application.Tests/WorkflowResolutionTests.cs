#pragma warning disable CS1591

using OpenSorSe.Application.Workflows;
using OpenSorSe.Application.Watching;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Application.Tests;

public sealed class WorkflowResolutionTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(),
        "OpenSorSe.WorkflowResolution.Tests",
        Guid.NewGuid().ToString("N"));

    public WorkflowResolutionTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ManualOverride_OnlyNarrowsProfileAndSnapshotRemainsHistorical()
    {
        var library = CreateLibrary();
        var profile = await library.DuplicateProfileAsync(
            BuiltInWorkflowIds.InvoicesAndReceipts,
            "My invoices",
            CancellationToken.None);
        var resolver = new WorkflowConfigurationResolver(library, EnabledConfiguration());

        var result = await resolver.ResolveForManualScanAsync(
            profile.Id,
            new WorkflowProfileOverride(
                MaximumFileSizeBytes: 2 * 1024 * 1024,
                OcrEnabled: false,
                DuplicateAnalysisEnabled: false,
                AiEnabled: false,
                GenerateChangePlans: false),
            CancellationToken.None);

        var resolved = Assert.IsType<ResolvedWorkflowConfiguration>(result.Configuration);
        Assert.True(result.IsAvailable);
        Assert.Equal(2 * 1024 * 1024, resolved.Files.MaximumFileSizeBytes);
        Assert.False(resolved.Extraction.OcrEnabled);
        Assert.False(resolved.Analysis.DuplicateAnalysisEnabled);
        Assert.False(resolved.Ai.Enabled);
        Assert.False(resolved.ChangePlans.GenerateChangePlans);
        Assert.Equal(profile.Revision, resolved.Snapshot.ProfileRevision);
        Assert.Equal(profile.Id, resolved.Snapshot.ProfileId);
        Assert.Equal("manual scan", resolved.Snapshot.ResolutionSource);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<WorkflowRecipeSnapshot>)resolved.Snapshot.Recipes).Add(
                new WorkflowRecipeSnapshot("x", "x", 1, DateTimeOffset.UnixEpoch, 0)));

        var changed = await library.UpdateProfileAsync(
            profile with { Name = "Invoices revised" },
            CancellationToken.None);
        Assert.Equal("My invoices", resolved.Snapshot.ProfileName);
        Assert.NotEqual(changed.Revision, resolved.Snapshot.ProfileRevision);

        var watched = Watched(profile.Id) with
        {
            MaximumFileSizeBytes = 4 * 1024 * 1024,
            AiAnalysisEnabled = true,
            ProfileOverride = new WorkflowProfileOverride(
                MaximumFileSizeBytes: 2 * 1024 * 1024,
                AiEnabled: false),
        };
        var watchedResult = await resolver.ResolveForWatchedFolderAsync(
            watched,
            CancellationToken.None);
        var watchedConfiguration = Assert.IsType<ResolvedWorkflowConfiguration>(
            watchedResult.Configuration);
        Assert.Equal(2 * 1024 * 1024, watchedConfiguration.Files.MaximumFileSizeBytes);
        Assert.False(watchedConfiguration.Ai.Enabled);

        var fullScanDisabled = await library.UpdateProfileAsync(
            changed with { FullScan = changed.FullScan with { Enabled = false } },
            CancellationToken.None);
        var disabledResult = await resolver.ResolveForManualScanAsync(
            fullScanDisabled.Id,
            null,
            CancellationToken.None);
        Assert.False(disabledResult.IsAvailable);
        Assert.Contains("full manual", disabledResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            library.GetDiagnostics(),
            diagnostic =>
                diagnostic.Kind == WorkflowDiagnosticKind.Resolution &&
                diagnostic.ItemId == profile.Id);
    }

    [Fact]
    public async Task WatchedLegacyDefault_IsExplicitlyMigratedButUnknownProfileNeverFallsBack()
    {
        var library = CreateLibrary();
        var resolver = new WorkflowConfigurationResolver(library, EnabledConfiguration());
        var legacy = await resolver.ResolveForWatchedFolderAsync(
            Watched("default"),
            CancellationToken.None);
        var missing = await resolver.ResolveForWatchedFolderAsync(
            Watched("profile:does-not-exist"),
            CancellationToken.None);

        Assert.True(legacy.IsAvailable);
        Assert.Equal(BuiltInWorkflowIds.GeneralDocuments, legacy.Configuration!.Profile.Id);
        Assert.Contains(legacy.Warnings, warning =>
            warning.Contains("explicitly migrated", StringComparison.OrdinalIgnoreCase));
        Assert.False(missing.IsAvailable);
        Assert.Null(missing.Configuration);
        Assert.Contains("does not exist", missing.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchivedProfileAndRecipe_AreUnavailableWithClearState()
    {
        var library = CreateLibrary();
        var profile = await library.DuplicateProfileAsync(
            BuiltInWorkflowIds.GeneralDocuments,
            "Archive candidate",
            CancellationToken.None);
        await library.SetProfileArchivedAsync(profile.Id, true, CancellationToken.None);
        var resolver = new WorkflowConfigurationResolver(library, EnabledConfiguration());

        var archivedProfile = await resolver.ResolveForManualScanAsync(
            profile.Id,
            null,
            CancellationToken.None);
        Assert.False(archivedProfile.IsAvailable);
        Assert.Contains("archived", archivedProfile.Message, StringComparison.OrdinalIgnoreCase);

        var recipe = await library.DuplicateRecipeAsync(
            BuiltInWorkflowIds.GeneralDocumentRecipe,
            "Archive recipe",
            CancellationToken.None);
        var activeProfile = await library.CreateProfileAsync(
            BuiltInWorkflowLibrary.Profiles[0] with
            {
                Id = "profile:archive-recipe",
                Name = "Uses archived recipe",
                IsBuiltIn = false,
                Origin = new WorkflowProfileOrigin(WorkflowOriginKind.UserCreated),
                SortingRecipeIds = [recipe.Id],
            },
            CancellationToken.None);
        await library.SetRecipeArchivedAsync(recipe.Id, true, CancellationToken.None);

        var archivedRecipe = await resolver.ResolveForManualScanAsync(
            activeProfile.Id,
            null,
            CancellationToken.None);
        Assert.False(archivedRecipe.IsAvailable);
        Assert.Contains("archived", archivedRecipe.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IncompatibleProfileSchema_IsRejectedBeforeExecution()
    {
        var incompatible = BuiltInWorkflowLibrary.Profiles[0] with
        {
            SchemaVersion = 99,
            Id = "profile:future",
            Name = "Future profile",
            IsBuiltIn = false,
        };
        var engine = new WorkflowTemplateEngine();
        var validator = new WorkflowValidator(engine);
        var library = new WorkflowLibraryService(
            new StubStore([incompatible], []),
            validator,
            new EmptyUsageInspector());
        var resolver = new WorkflowConfigurationResolver(library, EnabledConfiguration());

        var result = await resolver.ResolveForManualScanAsync(
            incompatible.Id,
            null,
            CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("schema", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private WorkflowLibraryService CreateLibrary()
    {
        var engine = new WorkflowTemplateEngine();
        var validator = new WorkflowValidator(engine);
        return new WorkflowLibraryService(
            new JsonWorkflowLibraryStore(
                Path.Combine(_workspace, "library.json"),
                validator,
                new LoggingService()),
            validator,
            new EmptyUsageInspector());
    }

    private static FixedConfiguration EnabledConfiguration() =>
        new(new ApplicationSettings
        {
            Content = new ContentSettings { OcrEnabled = true },
            Ai = new AiSettings
            {
                Enabled = true,
                SelectedModel = "local-test-model",
            },
        });

    private WatchedFolderConfiguration Watched(string profileId) =>
        new(
            "watch:1",
            _workspace,
            "Watched",
            true,
            true,
            [],
            [],
            profileId,
            null,
            true,
            true,
            new WatchedFolderNotificationPreferences(),
            TimeSpan.FromSeconds(2),
            null,
            null,
            WatchedFolderStatus.Watching,
            "catalog:1");

    private sealed class FixedConfiguration(ApplicationSettings settings) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = settings;
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
        public Task<WorkflowUsageInfo> InspectAsync(string itemId, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowUsageInfo(itemId, [], [], 0));
    }

    private sealed class StubStore(
        IReadOnlyList<WorkflowProfile> profiles,
        IReadOnlyList<SortingRecipe> recipes) : IWorkflowLibraryStore
    {
        public Task<WorkflowLibraryLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new WorkflowLibraryLoadResult(profiles, recipes, null, null, false));

        public Task SaveAsync(
            IReadOnlyList<WorkflowProfile> savedProfiles,
            IReadOnlyList<SortingRecipe> savedRecipes,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
