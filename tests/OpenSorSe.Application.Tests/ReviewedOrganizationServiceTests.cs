#pragma warning disable CA2007, CS1591

using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Logging;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;

namespace OpenSorSe.Application.Tests;

public sealed class ReviewedOrganizationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "OmniSorSe.ReviewedOrganization.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly EvidenceSource _evidence = new();

    public ReviewedOrganizationServiceTests()
    {
        Directory.CreateDirectory(_root);
        _evidence.Sources = [Source("source:one", _root)];
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Preview_UsesAcceptedAndStrongDeterministicTagsWithExplicitDates()
    {
        var document = await AddDocumentAsync(
            "file:invoice",
            "scan_0042.pdf",
            tags:
            [
                Tag("file:invoice", "theme:finance", SmartTagType.Theme, "Finance", SmartTagAssignmentState.Automatic, ContentIntelligenceConfidence.Strong),
                Tag("file:invoice", "type:invoice", SmartTagType.DocumentType, "Invoice", SmartTagAssignmentState.Accepted, ContentIntelligenceConfidence.Moderate),
                Tag("file:invoice", "theme:travel", SmartTagType.Theme, "Travel", SmartTagAssignmentState.Suggested, ContentIntelligenceConfidence.Moderate),
            ]);
        var recipe = Recipe(
            "{filesystemModifiedDate:yyyy-MM-dd}_{documentType}_{originalName}",
            "{theme}/{documentType}",
            ["filesystemModifiedDate", "documentType", "originalName", "theme"]);

        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(recipe, [document.FileId]),
            CancellationToken.None);

        var row = Assert.Single(proposal.Rows);
        Assert.Equal(OrganizationProposalReadiness.Reliable, row.Readiness);
        Assert.EndsWith(Path.Combine("Finance", "Invoice", "2026-05-03_Invoice_scan_0042.pdf"), row.TargetPath);
        Assert.Contains(row.Evidence, item => item.Token == "{theme}" && item.EvidenceSource.Contains("Strong deterministic", StringComparison.Ordinal));
        Assert.Contains(row.Evidence, item => item.Token == "{documentType}" && item.EvidenceSource.Contains("accepted", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(row.Evidence, item => item.Value == "Travel");
        Assert.True(proposal.HasSensitivePathEvidence);
        Assert.True(proposal.CanCreateChangePlan);
    }

    [Fact]
    public async Task Preview_MultipleEligibleThemesCannotResolveSingularToken()
    {
        var document = await AddDocumentAsync(
            "file:ambiguous",
            "ambiguous.pdf",
            tags:
            [
                Tag("file:ambiguous", "theme:finance", SmartTagType.Theme, "Finance", SmartTagAssignmentState.Accepted, ContentIntelligenceConfidence.Strong),
                Tag("file:ambiguous", "theme:legal", SmartTagType.Theme, "Legal", SmartTagAssignmentState.Accepted, ContentIntelligenceConfidence.Strong),
            ]);

        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}", "{theme}", ["theme"]), [document.FileId]),
            CancellationToken.None);

        var row = Assert.Single(proposal.Rows);
        Assert.Equal(OrganizationProposalReadiness.CannotPropose, row.Readiness);
        Assert.Contains("theme", row.MissingEvidence, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(row.Warnings, warning => warning.Contains("Multiple eligible Theme", StringComparison.Ordinal));
        Assert.False(proposal.CanCreateChangePlan);
    }

    [Fact]
    public async Task Preview_ExcludesModerateLimitedAndRejectedClassifications()
    {
        var document = await AddDocumentAsync(
            "file:untrusted",
            "untrusted.pdf",
            tags:
            [
                Tag("file:untrusted", "theme:moderate", SmartTagType.Theme, "Finance", SmartTagAssignmentState.Suggested, ContentIntelligenceConfidence.Moderate),
                Tag("file:untrusted", "theme:limited", SmartTagType.Theme, "Legal", SmartTagAssignmentState.Suggested, ContentIntelligenceConfidence.Limited),
                Tag("file:untrusted", "theme:rejected", SmartTagType.Theme, "Travel", SmartTagAssignmentState.Suggested, ContentIntelligenceConfidence.Strong) with { Decision = SmartTagDecision.Rejected },
            ]);

        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}", "{theme}", ["theme"]), [document.FileId]),
            CancellationToken.None);

        var row = Assert.Single(proposal.Rows);
        Assert.Equal(OrganizationProposalReadiness.CannotPropose, row.Readiness);
        Assert.DoesNotContain(row.Evidence, item => item.Token == "{theme}");
        Assert.Contains("theme", row.MissingEvidence, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_ExplicitFallbackIsDisclosedAsNeedsReview()
    {
        var document = await AddDocumentAsync("file:fallback", "fallback.pdf");
        var recipe = Recipe("{documentType}_{originalName}", string.Empty, ["documentType"]) with
        {
            FallbackValues = new Dictionary<string, string> { ["documentType"] = "Unclassified" },
            Normalization = Recipe("x", string.Empty).Normalization with
            {
                MissingValuePolicy = WorkflowMissingValuePolicy.UseFallback,
            },
        };

        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(recipe, [document.FileId]),
            CancellationToken.None);

        var row = Assert.Single(proposal.Rows);
        Assert.Equal(OrganizationProposalReadiness.NeedsReview, row.Readiness);
        Assert.Contains("documentType", row.Fallbacks, StringComparer.OrdinalIgnoreCase);
        Assert.EndsWith("Unclassified_fallback.pdf", row.TargetPath);
    }

    [Fact]
    public async Task Preview_AlwaysPreservesOriginalExtensionExactly()
    {
        var document = await AddDocumentAsync("file:extension", "REPORT.PDF");
        var recipe = Recipe("renamed.txt", string.Empty) with { PreserveExtension = false };

        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(recipe, [document.FileId]),
            CancellationToken.None);

        var row = Assert.Single(proposal.Rows);
        Assert.Equal("renamed.txt.PDF", row.ProposedFileName);
        Assert.True(proposal.Recipe.PreserveExtension);
    }

    [Fact]
    public async Task Preview_MissingStableIdIsVisibleAndBlocksPlan()
    {
        var document = await AddDocumentAsync("file:known", "known.txt");

        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}_sorted", string.Empty), [document.FileId, "file:missing"]),
            CancellationToken.None);

        Assert.Equal(2, proposal.Rows.Count);
        var missing = proposal.Rows.Single(row => row.FileId == "file:missing");
        Assert.Equal(OrganizationProposalReadiness.CannotPropose, missing.Readiness);
        Assert.Contains(missing.Conflicts, value => value.Contains("no longer resolves", StringComparison.Ordinal));
        Assert.False(proposal.CanCreateChangePlan);
    }

    [Fact]
    public async Task Preview_RejectsCrossSourceSelection()
    {
        var otherRoot = Path.Combine(_root, "other-source");
        Directory.CreateDirectory(otherRoot);
        _evidence.Sources = [Source("source:one", _root), Source("source:two", otherRoot)];
        var first = await AddDocumentAsync("file:one", "one.txt");
        var secondPath = Path.Combine(otherRoot, "two.txt");
        await File.WriteAllTextAsync(secondPath, "two");
        _evidence.Documents.Add(Document("file:two", secondPath, "source:two"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().PreviewAsync(
                new OrganizationPreviewRequest(Recipe("{originalName}_sorted", string.Empty), [first.FileId, "file:two"]),
                CancellationToken.None));

        Assert.Contains("one current indexed source", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_DuplicateGeneratedTargetsBlocksEveryCollidingRow()
    {
        var first = await AddDocumentAsync("file:a", "a.txt");
        var second = await AddDocumentAsync("file:b", "b.txt");
        var recipe = Recipe("same", "Organized");

        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(recipe, [first.FileId, second.FileId]),
            CancellationToken.None);

        Assert.All(proposal.Rows, row =>
        {
            Assert.Equal(OrganizationProposalReadiness.CannotPropose, row.Readiness);
            Assert.Contains(row.Conflicts, conflict => conflict.Contains("same normalized target", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Preview_ConservativelyBlocksUnicodeNormalizationCollisions()
    {
        var first = await AddDocumentAsync("file:unicode:one", "Café.pdf");
        var second = await AddDocumentAsync("file:unicode:two", "Cafe\u0301.pdf");
        var baseRecipe = Recipe("{originalName}", "Organized", ["originalName"]);
        var recipe = baseRecipe with
        {
            Normalization = baseRecipe.Normalization with { NormalizeUnicode = false },
        };

        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(recipe, [first.FileId, second.FileId]),
            CancellationToken.None);

        Assert.All(proposal.Rows, row =>
        {
            Assert.Equal(OrganizationProposalReadiness.CannotPropose, row.Readiness);
            Assert.Contains(row.Conflicts, conflict => conflict.Contains("same normalized target", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Preview_BudgetsInferredDirectoriesBeforePlanCreation()
    {
        var ids = new List<string>();
        for (var index = 0; index < 501; index++)
        {
            var document = await AddDocumentAsync($"file:{index}", $"item-{index:D4}.txt");
            ids.Add(document.FileId);
        }

        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}_sorted", "{originalName}"), ids),
            CancellationToken.None);

        Assert.Equal(501, proposal.ProjectedFileActionCount);
        Assert.Equal(501, proposal.ProjectedDirectoryActionCount);
        Assert.Equal(1002, proposal.ProjectedActionCount);
        Assert.False(proposal.CanCreateChangePlan);
        Assert.Contains(proposal.Warnings, warning => warning.Contains("safe limit is 1000", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "PerformanceRegression")]
    public async Task Preview_AllowsExactlyOneThousandTotalActions()
    {
        var ids = new List<string>();
        for (var index = 0; index < 999; index++)
        {
            var document = await AddDocumentAsync($"file:{index}", $"item-{index:D4}.txt");
            ids.Add(document.FileId);
        }

        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}_sorted", "Organized"), ids),
            CancellationToken.None);

        Assert.Equal(999, proposal.ProjectedFileActionCount);
        Assert.Equal(1, proposal.ProjectedDirectoryActionCount);
        Assert.Equal(1000, proposal.ProjectedActionCount);
        Assert.True(proposal.CanCreateChangePlan);
    }

    [Fact]
    public async Task Preview_RejectsSelectionOverOneThousandWithoutQueryingEvidence()
    {
        var ids = Enumerable.Range(0, 1001).Select(index => $"file:{index}").ToArray();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Service().PreviewAsync(
                new OrganizationPreviewRequest(Recipe("{originalName}_sorted", string.Empty), ids),
                CancellationToken.None));

        Assert.Equal(0, _evidence.DocumentQueryCount);
    }

    [Fact]
    public async Task Preview_PreCancelledStopsBeforeEvidenceQuery()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service().PreviewAsync(
                new OrganizationPreviewRequest(Recipe("{originalName}_sorted", string.Empty), ["file:any"]),
                cancellation.Token));

        Assert.Equal(0, _evidence.DocumentQueryCount);
    }

    [Fact]
    public async Task CreateChangePlan_UsesFreshPreviewAndExistingSafetyBoundary()
    {
        var document = await AddDocumentAsync("file:move", "move.txt");
        var service = Service();
        var proposal = await service.PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}_sorted", "Organized"), [document.FileId]),
            CancellationToken.None);

        var plan = await service.CreateChangePlanAsync(proposal, "discovery:test", CancellationToken.None);

        Assert.Equal(ChangePlanStatus.AwaitingReview, plan.Status);
        Assert.Equal(2, plan.Actions.Count);
        Assert.Contains(plan.Actions, action => action.ActionType == ChangeActionType.CreateDirectory);
        var move = Assert.Single(plan.Actions, action => action.ActionType == ChangeActionType.MoveFile);
        Assert.NotNull(move.SourceIdentity);
        Assert.Equal(proposal.Recipe.Id, move.WorkflowProvenance!.RecipeId);
        Assert.True(File.Exists(document.FullPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "Organized")));
    }

    [Fact]
    public async Task CreateChangePlan_RejectsStalePreviewAfterIndexedPathChanges()
    {
        var document = await AddDocumentAsync("file:stale", "stale.txt");
        var service = Service();
        var proposal = await service.PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}_sorted", string.Empty), [document.FileId]),
            CancellationToken.None);
        var movedPath = Path.Combine(_root, "moved.txt");
        File.Move(document.FullPath, movedPath);
        _evidence.Documents[0] = document with { FullPath = movedPath, FileName = "moved.txt" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateChangePlanAsync(proposal, "discovery:stale", CancellationToken.None));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecipePlan_ExecutesThroughExistingJournalAndUndoRestoresSource()
    {
        var document = await AddDocumentAsync("file:undo", "undo.txt");
        var gateway = new PhysicalFileSystemGateway();
        var validator = new ChangePlanValidator(gateway);
        var planStore = new InMemoryChangePlanStore();
        var journal = new InMemoryOperationJournalStore();
        var service = new ReviewedOrganizationService(
            _evidence,
            new WorkflowTemplateEngine(),
            new ChangePlanFactory(gateway, validator, planStore));
        var preview = await service.PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}_reviewed", "Organized"), [document.FileId]),
            CancellationToken.None);
        var plan = await service.CreateChangePlanAsync(preview, "discovery:undo", CancellationToken.None);
        plan = plan with
        {
            Actions = Array.AsReadOnly(plan.Actions
                .Select(action => action with { ApprovalState = ChangeApprovalState.Approved })
                .ToArray()),
        };
        var executor = new ChangePlanExecutionService(gateway, validator, planStore, journal);

        var executed = await executor.ExecuteAsync(plan, "Reviewed organization", null, CancellationToken.None);
        Assert.True(executed.Succeeded);
        Assert.False(File.Exists(document.FullPath));
        Assert.True(File.Exists(Path.Combine(_root, "Organized", "undo_reviewed.txt")));

        var undone = await executor.UndoAsync(executed.Operation.OperationId, null, null, CancellationToken.None);
        Assert.Equal(OperationStatus.Undone, undone.Operation.Status);
        Assert.True(File.Exists(document.FullPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "Organized")));
    }

    private ReviewedOrganizationService Service()
    {
        var fileSystem = new PhysicalFileSystemGateway();
        return new ReviewedOrganizationService(
            _evidence,
            new WorkflowTemplateEngine(),
            new ChangePlanFactory(
                fileSystem,
                new ChangePlanValidator(fileSystem),
                new JsonChangePlanStore(Path.Combine(_root, "plans.json"), new LoggingService())));
    }

    private async Task<ProgressiveSearchDocument> AddDocumentAsync(
        string id,
        string fileName,
        IReadOnlyList<FileSmartTag>? tags = null)
    {
        var path = Path.Combine(_root, fileName);
        await File.WriteAllTextAsync(path, id);
        var document = Document(id, path, "source:one") with { SmartTags = tags ?? [] };
        _evidence.Documents.Add(document);
        return document;
    }

    private static ProgressiveSearchDocument Document(string id, string path, string sourceId) => new()
    {
        FileId = id,
        FullPath = path,
        FileName = Path.GetFileName(path),
        RelativePath = Path.GetFileName(path),
        FolderName = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty,
        Extension = Path.GetExtension(path),
        FileType = "Document",
        SourceId = sourceId,
        Length = new FileInfo(path).Length,
        CreationTimeUtc = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        ModifiedTimeUtc = new DateTimeOffset(2026, 5, 3, 0, 0, 0, TimeSpan.Zero),
        IsFullyIndexed = true,
    };

    private static IndexingSource Source(string id, string root) =>
        new(id, root, Path.GetFileName(root), OpenSorSe.Core.Configuration.IndexingLevel.Standard, true, true, 0, []);

    private static SortingRecipe Recipe(
        string naming,
        string destination,
        IReadOnlyList<string>? required = null) =>
        BuiltInWorkflowLibrary.Recipes[0] with
        {
            Id = "recipe:test",
            Name = "Test organization recipe",
            NamingTemplate = naming,
            DestinationTemplate = destination,
            RequiredFields = required ?? ["originalName"],
            OptionalFields = [],
            FallbackValues = new Dictionary<string, string>(),
            Applicability = new RecipeApplicability([], []),
            PreserveExtension = true,
        };

    private static FileSmartTag Tag(
        string fileId,
        string tagId,
        SmartTagType type,
        string display,
        SmartTagAssignmentState state,
        ContentIntelligenceConfidence confidence) => new()
        {
            FileId = fileId,
            Definition = new SmartTagDefinition
            {
                TagId = tagId,
                Type = type,
                CanonicalKey = tagId,
                DisplayName = display,
                TaxonomyVersion = "1",
                Origin = SmartTagOrigin.BuiltInTaxonomy,
                IsBuiltIn = true,
            },
            Confidence = confidence,
            Origin = state == SmartTagAssignmentState.Automatic
                ? SmartTagOrigin.DeterministicClassifier
                : SmartTagOrigin.User,
            State = state,
            Decision = state == SmartTagAssignmentState.Accepted
                ? SmartTagDecision.Accepted
                : SmartTagDecision.None,
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        };

    private sealed class EvidenceSource : IReviewedOrganizationEvidenceSource
    {
        public List<ProgressiveSearchDocument> Documents { get; } = [];
        public IReadOnlyList<IndexingSource> Sources { get; set; } = [];
        public int DocumentQueryCount { get; private set; }

        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
            IReadOnlyList<string> fileIds,
            CancellationToken cancellationToken)
        {
            DocumentQueryCount++;
            var selected = fileIds.ToHashSet(StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>(
                Documents.Where(document => selected.Contains(document.FileId)).ToArray());
        }

        public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Sources);
    }
}
