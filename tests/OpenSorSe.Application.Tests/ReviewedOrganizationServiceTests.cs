#pragma warning disable CA2007, CS1591

using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.AI;
using OpenSorSe.AI;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;
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
    public async Task Preview_EditsAndRejectionsChangeOnlyProposalAndRetainReasons()
    {
        var first = await AddDocumentAsync("file:edit", "edit.pdf");
        var rejected = await AddDocumentAsync("file:reject", "reject.pdf");
        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}", "Documents/Bills"), [first.FileId, rejected.FileId])
            {
                Strategy = OrganizationStrategy.Fresh,
                Edits = [new(first.FileId, "Finance/Invoices/renamed.pdf"), new(rejected.FileId, null, true)],
            }, CancellationToken.None);

        Assert.True(proposal.CanCreateChangePlan);
        Assert.Equal(1, proposal.ProjectedFileActionCount);
        Assert.EndsWith(Path.Combine("Finance", "Invoices", "renamed.pdf"), proposal.Rows[0].TargetPath);
        Assert.Contains(proposal.Rows[0].Reasons, reason => reason.Contains("edited", StringComparison.OrdinalIgnoreCase));
        Assert.True(proposal.Rows[1].IsRejected);
        Assert.False(proposal.Rows[1].IsEligible);
        Assert.Equal("file:edit", await File.ReadAllTextAsync(first.FullPath));
        Assert.True(File.Exists(rejected.FullPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "Finance")));
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("C:/elsewhere/escape.pdf")]
    [InlineData("\\\\server\\share\\escape.pdf")]
    [InlineData("Finance/CON.pdf")]
    [InlineData("Finance/changed.exe")]
    public async Task Preview_UnsafeEditedTargetBlocksReview(string target)
    {
        var document = await AddDocumentAsync("file:unsafe", "unsafe.pdf");
        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}", "Documents"), [document.FileId])
            {
                Edits = [new(document.FileId, target)],
            }, CancellationToken.None);

        Assert.False(proposal.CanCreateChangePlan);
        Assert.Equal(OrganizationProposalReadiness.CannotPropose, Assert.Single(proposal.Rows).Readiness);
        Assert.True(File.Exists(document.FullPath));
    }

    [Theory]
    [InlineData(OrganizationStrategy.Preserve, "Existing")]
    [InlineData(OrganizationStrategy.Improve, "Existing/Organized")]
    [InlineData(OrganizationStrategy.Fresh, "Organized")]
    public async Task Preview_StrategiesKeepOrReplaceExistingHierarchy(OrganizationStrategy strategy, string expected)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Existing"));
        var document = await AddDocumentAsync("file:strategy", Path.Combine("Existing", "file.pdf"));
        var proposal = await Service().PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}_new", "Organized"), [document.FileId])
            {
                Strategy = strategy,
            }, CancellationToken.None);

        var row = Assert.Single(proposal.Rows);
        Assert.Equal(expected.Replace('/', Path.DirectorySeparatorChar), row.ProposedRelativeDestination);
        Assert.True(row.IsEligible);
        Assert.NotEmpty(row.Reasons);
    }

    [Fact]
    public async Task CreateChangePlan_LiveSourceChangeInvalidatesEditedPreviewWithoutIndexUpdate()
    {
        var document = await AddDocumentAsync("file:live", "live.pdf");
        var service = Service();
        var proposal = await service.PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}", "Documents"), [document.FileId])
            {
                Edits = [new(document.FileId, "Finance/live.pdf")],
            }, CancellationToken.None);
        await File.AppendAllTextAsync(document.FullPath, "changed after preview");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateChangePlanAsync(proposal, "test:edited", CancellationToken.None));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RememberedEdits_PersistAndGuideOnlyTheirSourceWithoutChangingSourceFiles()
    {
        var document = await AddDocumentAsync("file:preference", "bill.pdf");
        var historyPath = Path.Combine(_root, "decisions.json");
        var service = Service(new JsonDecisionHistoryStore(historyPath, new LoggingService()));
        var recipe = Recipe("{originalName}", "Documents/Bills");
        var proposal = await service.PreviewAsync(new OrganizationPreviewRequest(recipe, [document.FileId])
        {
            Strategy = OrganizationStrategy.Fresh,
            Edits = [new(document.FileId, "Finance/Invoices/bill.pdf")],
        }, CancellationToken.None);
        Assert.False(File.Exists(historyPath));
        await service.RememberPreferencesAsync(proposal, CancellationToken.None);

        var reopened = Service(new JsonDecisionHistoryStore(historyPath, new LoggingService()));
        var learned = await reopened.PreviewAsync(new OrganizationPreviewRequest(recipe, [document.FileId])
        {
            Strategy = OrganizationStrategy.Fresh,
        }, CancellationToken.None);
        Assert.EndsWith(Path.Combine("Finance", "Invoices", "bill.pdf"), Assert.Single(learned.Rows).TargetPath);
        Assert.Contains(learned.Rows[0].Reasons, reason => reason.Contains("preference", StringComparison.OrdinalIgnoreCase));

        _evidence.Sources = [Source("source:different", _root)];
        _evidence.Documents[0] = document with { SourceId = "source:different" };
        var unrelated = await reopened.PreviewAsync(new OrganizationPreviewRequest(recipe, [document.FileId]), CancellationToken.None);
        Assert.EndsWith(Path.Combine("Documents", "Bills", "bill.pdf"), Assert.Single(unrelated.Rows).TargetPath);
        Assert.Equal("file:preference", await File.ReadAllTextAsync(document.FullPath));
    }

    [Fact]
    public async Task ValidatedAiClassification_IsProposalEvidenceWithPendingChangePlanApproval()
    {
        var document = await AddDocumentAsync("file:ai", "scan.pdf");
        _evidence.Documents[0] = document with
        {
            ContentIntelligence = new IndexedContentIntelligence
            {
                Provider = "ollama-indexing",
                ProviderVersion = "test:model",
                ProcessingFingerprint = "test:fingerprint",
                DocumentType = "Invoice",
                Category = "Finance",
            },
        };
        var service = Service();
        var preview = await service.PreviewAsync(new OrganizationPreviewRequest(
            Recipe("{originalName}", "{theme}/{documentType}", ["theme", "documentType"]), [document.FileId]), CancellationToken.None);
        var row = Assert.Single(preview.Rows);
        Assert.True(row.UsesAiEvidence);
        Assert.Contains(row.Evidence, mapping => mapping.EvidenceSource.Contains("not user-confirmed", StringComparison.Ordinal));
        var plan = await service.CreateChangePlanAsync(preview, "test:ai-organization", CancellationToken.None);
        var move = Assert.Single(plan.Actions, action => action.ActionType == ChangeActionType.MoveFile);
        Assert.Equal(ChangeSuggestionSource.Ai, move.SuggestionSource);
        Assert.Equal(ChangeApprovalState.Pending, move.ApprovalState);
        Assert.True(File.Exists(document.FullPath));
    }

    [Fact]
    public async Task EditedTargets_CollisionsBlockUntilOneMoveIsRejected()
    {
        var first = await AddDocumentAsync("file:first", "first.pdf");
        var second = await AddDocumentAsync("file:second", "second.pdf");
        var request = new OrganizationPreviewRequest(Recipe("{originalName}", "Documents"), [first.FileId, second.FileId])
        {
            Edits = [new(first.FileId, "Finance/same.pdf"), new(second.FileId, "Finance/same.pdf")],
        };
        var service = Service();
        var collision = await service.PreviewAsync(request, CancellationToken.None);
        Assert.False(collision.CanCreateChangePlan);
        Assert.All(collision.Rows, row => Assert.Contains(row.Conflicts, conflict => conflict.Contains("same normalized target", StringComparison.Ordinal)));

        var resolved = await service.PreviewAsync(request with
        {
            Edits = [new(first.FileId, "Finance/same.pdf"), new(second.FileId, null, true)],
        }, CancellationToken.None);
        Assert.True(resolved.CanCreateChangePlan);
        Assert.Equal(1, resolved.ProjectedFileActionCount);
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
    public async Task EditedRecipePlan_ExecutesThroughExistingJournalAndUndoRestoresSource()
    {
        var document = await AddDocumentAsync("file:undo", "undo.txt");
        var pathSemantics = OperatingSystem.IsWindows()
            ? (IPathSemantics)new WindowsPathSemantics()
            : new LinuxPathSemantics();
        var capabilities = new SupportedFileSystemCapabilities();
        var gateway = new PhysicalFileSystemGateway(
            pathSemantics,
            FileIdentityProviderFactory.CreateCurrent(),
            capabilities);
        var validator = new ChangePlanValidator(gateway, pathSemantics, capabilities);
        var planStore = new InMemoryChangePlanStore();
        var journal = new InMemoryOperationJournalStore();
        var service = new ReviewedOrganizationService(
            _evidence,
            new WorkflowTemplateEngine(),
            new ChangePlanFactory(gateway, validator, planStore));
        var preview = await service.PreviewAsync(
            new OrganizationPreviewRequest(Recipe("{originalName}_reviewed", "Organized"), [document.FileId])
            {
                Edits = [new(document.FileId, "Custom/undo_edited.txt")],
            },
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
        Assert.True(executed.Succeeded, executed.Summary);
        Assert.False(File.Exists(document.FullPath));
        Assert.True(File.Exists(Path.Combine(_root, "Custom", "undo_edited.txt")));

        var undone = await executor.UndoAsync(executed.Operation.OperationId, null, null, CancellationToken.None);
        Assert.Equal(OperationStatus.Undone, undone.Operation.Status);
        Assert.True(File.Exists(document.FullPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "Custom")));
    }

    private ReviewedOrganizationService Service(IDecisionHistoryStore? decisions = null)
    {
        var fileSystem = new PhysicalFileSystemGateway();
        return new ReviewedOrganizationService(
            _evidence,
            new WorkflowTemplateEngine(),
            new ChangePlanFactory(
                fileSystem,
                new ChangePlanValidator(fileSystem),
                new JsonChangePlanStore(Path.Combine(_root, "plans.json"), new LoggingService())),
            decisions: decisions);
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

    private sealed class SupportedFileSystemCapabilities : IFileSystemCapabilities
    {
        public FileLinkInspection InspectLink(string path) =>
            new(false, null, null, "Test paths are not links.");

        public bool CanWriteDirectory(string path, out string explanation)
        {
            explanation = "The test directory is writable.";
            return true;
        }

        public long? GetAvailableFreeSpace(string path) => long.MaxValue;

        public bool AreOnSameFileSystem(
            string firstPath,
            string secondPath,
            out string explanation)
        {
            explanation = "Temporary test paths share one filesystem.";
            return true;
        }
    }

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
