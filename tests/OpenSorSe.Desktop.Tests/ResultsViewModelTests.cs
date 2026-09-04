using OpenSorSe.Application.Models;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies bounded, read-only snapshot exploration and exact-duplicate review behavior.
/// </summary>
public sealed class ResultsViewModelTests
{
    /// <summary>Files rows resolve through their current paths to bounded durable organization identities.</summary>
    [Fact]
    public async Task OrganizationSelection_ResolvesScanRowsToExplicitDurableFileIds()
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["C:\\Selected\\one.pdf"] = "index:durable:one",
            ["C:\\Selected\\two.pdf"] = "index:durable:two",
        };
        using var viewModel = new ResultsViewModel(
            new Configuration(),
            null,
            null,
            null,
            null,
            new SmartTags([], paths));
        await viewModel.LoadSnapshotAsync(CreateSnapshot(files:
        [
            CreateFile("scan:row:1", "C:\\Selected\\one.pdf", DuplicateStatus.Unique, null),
            CreateFile("scan:row:2", "C:\\Selected\\two.pdf", DuplicateStatus.Unique, null),
        ]));

        viewModel.SetOrganizationSelection(viewModel.PageRows.Reverse());
        await viewModel.OrganizeSelectedFilesCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.OrganizationSelectedCount);
        Assert.True(viewModel.OrganizeSelectedFilesCommand.CanExecute(null));
        Assert.Contains("maximum 1000", viewModel.OrganizationSelectionText, StringComparison.Ordinal);
        Assert.Equal(["index:durable:two", "index:durable:one"], viewModel.Organization.SelectedFileIds);
        Assert.DoesNotContain(viewModel.Organization.SelectedFileIds, id => id.StartsWith("scan:", StringComparison.Ordinal));
    }

    /// <summary>Verifies divider commands persist a bounded preference without changing other settings.</summary>
    [Fact]
    public async Task DetailsPanelResizeCommands_PersistBoundedRatioAndResetDefault()
    {
        var configuration = new Configuration();
        using var viewModel = new ResultsViewModel(configuration, null);

        await viewModel.SetDetailsPanelWidthRatioAsync(0.44);
        Assert.Equal(0.44, configuration.Current.Features.FilesPageDetailsPanelWidthRatio);

        await viewModel.WidenDetailsPanelCommand.ExecuteAsync(null);
        await viewModel.WidenDetailsPanelCommand.ExecuteAsync(null);
        Assert.Equal(
            FeatureSettings.MaximumFilesPageDetailsPanelWidthRatio,
            configuration.Current.Features.FilesPageDetailsPanelWidthRatio);

        await viewModel.SetDetailsPanelWidthRatioAsync(double.NaN);
        Assert.Equal(
            FeatureSettings.DefaultFilesPageDetailsPanelWidthRatio,
            configuration.Current.Features.FilesPageDetailsPanelWidthRatio);

        await viewModel.ResetDetailsPanelWidthCommand.ExecuteAsync(null);
        Assert.Equal(
            FeatureSettings.DefaultFilesPageDetailsPanelWidthRatio,
            viewModel.DetailsPanelWidthRatio);
    }

    /// <summary>
    /// Verifies loading keeps review data in the immutable snapshot and publishes only one bounded page.
    /// </summary>
    [Fact]
    public async Task LoadSnapshotAsync_PresentsSummaryWarningsAndBoundedRows()
    {
        var snapshot = CreateSnapshot(files: [
            CreateFile("file:0", "C:\\Selected\\first.txt", DuplicateStatus.Duplicate, "group:opaque"),
            CreateFile("file:1", "C:\\Selected\\second.txt", DuplicateStatus.Duplicate, "group:opaque")]);
        using var viewModel = new ResultsViewModel();

        await viewModel.LoadSnapshotAsync(snapshot);

        Assert.Same(snapshot, viewModel.Snapshot);
        Assert.Equal(new ResultsSummary(2, 1, 1, 2, 1), viewModel.Summary);
        Assert.Equal(2, viewModel.PageRows.Count);
        Assert.Equal("first.txt", viewModel.PageRows[0].FileName);
        Assert.Equal("Exact duplicate", viewModel.PageRows[0].DuplicateStatus);
        Assert.Equal("Yes", viewModel.PageRows[0].PlannedOperation);
        Assert.Equal(["A test scan warning."], viewModel.Warnings);
        Assert.Single(viewModel.Directories);
        Assert.Single(viewModel.PlannedOperations);
        Assert.True(viewModel.HasResults);
        Assert.True(viewModel.HasWarnings);
        Assert.False(viewModel.IsLoading);
    }

    /// <summary>
    /// Verifies filters reset paging, selection is derived from a bounded row, and no-all page can be requested.
    /// </summary>
    [Fact]
    public async Task QueryAndPaging_FilterSortAndSelectWithoutRetainingStaleDetails()
    {
        var files = Enumerable.Range(0, 250)
            .Select(index => CreateFile(
                $"file:{index}",
                $"C:\\Selected\\{index:D3}.txt",
                index % 2 == 0 ? DuplicateStatus.Duplicate : DuplicateStatus.Unique,
                index % 2 == 0 ? "group:opaque" : null,
                size: index,
                category: index % 3 == 0 ? FileCategory.Document : FileCategory.Code))
            .ToArray();
        using var viewModel = new ResultsViewModel();
        await viewModel.LoadSnapshotAsync(CreateSnapshot(files));

        Assert.Equal(200, viewModel.PageRows.Count);
        Assert.True(viewModel.CanGoNextPage);
        viewModel.NextPageCommand.Execute(null);
        await viewModel.RefreshAsync();
        Assert.Equal(50, viewModel.PageRows.Count);

        viewModel.SelectedDuplicateFilter = ResultDuplicateFilter.ExactDuplicatesOnly;
        await viewModel.RefreshAsync();
        Assert.Equal(125, viewModel.Page.TotalItemCount);
        Assert.Equal(0, viewModel.Page.PageIndex);
        viewModel.SelectedRow = viewModel.PageRows[0];
        Assert.NotNull(viewModel.SelectedDetails);
        Assert.Equal(viewModel.PageRows[0].FullPath, viewModel.SelectedDetails!.FullPath);

        viewModel.QueryText = "does-not-match";
        await viewModel.RefreshAsync();
        Assert.False(viewModel.HasFilterResults);
        Assert.True(viewModel.HasNoFilterResults);
        Assert.Null(viewModel.SelectedDetails);
        Assert.DoesNotContain(viewModel.AvailablePageSizes, pageSize => pageSize <= 0 || pageSize > 500);
    }

    /// <summary>
    /// Verifies exact duplicate review uses opaque group routing and exposes no execution behavior.
    /// </summary>
    [Fact]
    public async Task DuplicateReview_ShowGroupFilesRoutesToFilteredExplorer()
    {
        var snapshot = CreateSnapshot(files: [
            CreateFile("file:0", "C:\\One\\same.txt", DuplicateStatus.Duplicate, "group:opaque"),
            CreateFile("file:1", "C:\\Two\\same.txt", DuplicateStatus.Duplicate, "group:opaque"),
            CreateFile("file:2", "C:\\Two\\other.txt", DuplicateStatus.Unique, null)]);
        using var viewModel = new ResultsViewModel();
        await viewModel.LoadSnapshotAsync(snapshot);

        viewModel.OpenDuplicateReviewCommand.Execute(null);
        Assert.True(viewModel.IsDuplicateReviewVisible);
        var groupRow = Assert.Single(viewModel.DuplicateReview.VisibleGroupRows);
        Assert.Equal("same.txt, same.txt", groupRow.MemberSummary);
        Assert.Equal("2 B", groupRow.CommonFileSize);
        Assert.Equal("2 B", groupRow.PotentialReclaimableSpace);
        viewModel.DuplicateReview.SelectedGroup = Assert.Single(viewModel.DuplicateReview.VisibleGroups);
        Assert.Equal("2 B", viewModel.DuplicateReview.SelectedGroupPotentialReclaimableText);
        viewModel.DuplicateReview.ShowGroupFilesCommand.Execute(null);
        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsExplorerVisible);
        Assert.Equal("group:opaque", viewModel.Query.DuplicateGroupId);
        Assert.Equal(2, viewModel.PageRows.Count);
        Assert.All(viewModel.PageRows, row => Assert.Equal("Exact duplicate", row.DuplicateStatus));
    }

    /// <summary>
    /// Verifies an unavailable duplicate detector result remains a usable explorer with a clear limitation.
    /// </summary>
    [Fact]
    public async Task LoadSnapshotAsync_DuplicateDataUnavailable_DisablesDuplicateReview()
    {
        var snapshot = CreateSnapshot([CreateFile("file:0", "C:\\Only\\entry.txt", DuplicateStatus.Unknown, null)], duplicateDataAvailable: false);
        using var viewModel = new ResultsViewModel();

        await viewModel.LoadSnapshotAsync(snapshot);

        Assert.False(viewModel.CanOpenDuplicateReview);
        Assert.Contains(viewModel.Warnings, warning => warning.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.Single(viewModel.PageRows);
    }

    /// <summary>
    /// Verifies an accepted application-owned tag restored from a catalog participates in result details and is eligible for later persistence.
    /// </summary>
    [Fact]
    public async Task LoadSnapshotAsync_PersistedAcceptedTag_RestoresWithoutPersistingDeterministicTags()
    {
        var file = CreateFile("file:0", "C:\\Selected\\invoice.txt", DuplicateStatus.Unique, null);
        var accepted = new TagAssociation(
            "tag:invoice:finance",
            file.Id,
            "Finance",
            "finance",
            "Topic",
            TagSource.UserApproved,
            TagAcceptanceState.Accepted,
            null,
            DateTimeOffset.UnixEpoch);
        using var viewModel = new ResultsViewModel();

        await viewModel.LoadSnapshotAsync(CreateSnapshot([file]), [accepted]);
        viewModel.SelectedRow = Assert.Single(viewModel.PageRows);

        Assert.Contains("Finance", viewModel.SelectedDetails!.Tags, StringComparison.Ordinal);
        Assert.Equal([accepted], viewModel.GetPersistableTags());
    }

    /// <summary>
    /// Verifies explicit user tags update deterministic search and can be removed without changing the snapshot.
    /// </summary>
    [Fact]
    public async Task UserTagCommands_AddSearchAndRemoveApplicationMetadata()
    {
        var snapshot = CreateSnapshot([CreateFile("file:0", "C:\\Selected\\invoice.txt", DuplicateStatus.Unique, null)]);
        using var viewModel = new ResultsViewModel();
        var persistenceEvents = 0;
        viewModel.PersistedTagsChanged += (_, _) => persistenceEvents++;
        await viewModel.LoadSnapshotAsync(snapshot);
        viewModel.SelectedRow = Assert.Single(viewModel.PageRows);

        viewModel.UserTagText = "Quarterly Review, Finance";
        await viewModel.AddUserTagsCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.GetPersistableTags().Count);
        Assert.Equal(3, viewModel.UserTags.Count);
        Assert.Equal(1, persistenceEvents);
        Assert.Same(snapshot, viewModel.Snapshot);
        viewModel.QueryText = "quarterly-review";
        await viewModel.RefreshAsync();
        Assert.Single(viewModel.PageRows);

        viewModel.SelectedRow = Assert.Single(viewModel.PageRows);
        viewModel.SelectedUserTag = viewModel.UserTags.Single(tag => tag.DisplayName == "Quarterly Review");
        await viewModel.RemoveSelectedTagCommand.ExecuteAsync(null);

        Assert.Single(viewModel.GetPersistableTags());
        Assert.Equal(2, persistenceEvents);
        Assert.Empty(viewModel.PageRows);
        Assert.Contains("not changed", viewModel.UserTagStatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies deterministic extension tags are protected and accepted application metadata cannot grow past its bound.
    /// </summary>
    [Fact]
    public async Task UserTagCommands_ProtectDeterministicTagAndRejectCapacityOverflow()
    {
        var file = CreateFile("file:0", "C:\\Selected\\invoice.txt", DuplicateStatus.Unique, null);
        var persisted = Enumerable.Range(0, 12)
            .Select(index => new TagAssociation(
                $"tag:user:{index}",
                file.Id,
                $"User {index}",
                $"user-{index}",
                "User",
                TagSource.UserApproved,
                TagAcceptanceState.Accepted,
                null,
                DateTimeOffset.UnixEpoch))
            .ToArray();
        using var viewModel = new ResultsViewModel();
        var persistenceEvents = 0;
        viewModel.PersistedTagsChanged += (_, _) => persistenceEvents++;
        await viewModel.LoadSnapshotAsync(CreateSnapshot([file]), persisted);
        viewModel.SelectedRow = Assert.Single(viewModel.PageRows);

        viewModel.SelectedUserTag = viewModel.UserTags.Single(tag => tag.Source == "Derived");
        Assert.False(viewModel.RemoveSelectedTagCommand.CanExecute(null));
        viewModel.UserTagText = "Overflow";
        await viewModel.AddUserTagsCommand.ExecuteAsync(null);

        Assert.Equal(12, viewModel.GetPersistableTags().Count);
        Assert.Equal(0, persistenceEvents);
        Assert.Contains("at most 12", viewModel.UserTagStatusText, StringComparison.OrdinalIgnoreCase);

        viewModel.UserTagText = "User 0";
        await viewModel.AddUserTagsCommand.ExecuteAsync(null);

        Assert.Equal(12, viewModel.GetPersistableTags().Count);
        Assert.Equal(0, persistenceEvents);
        Assert.Contains("already", viewModel.UserTagStatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies generated candidates can be accepted or rejected and rejected state persists in the content store.</summary>
    [Fact]
    public async Task GeneratedTagCommands_PersistAcceptanceAndRejection()
    {
        var file = CreateFile("file:0", "C:\\Selected\\invoice.txt", DuplicateStatus.Unique, null);
        var first = GeneratedTag(file.FullPath, "invoice");
        var second = GeneratedTag(file.FullPath, "receipt");
        var store = new ContentStore(new ContentRecord(
            file.FullPath,
            2,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            [],
            null,
            "invoice receipt",
            OcrStatus.Completed,
            "fake",
            [])
        {
            Tags = [first, second],
        });
        using var viewModel = new ResultsViewModel(new Configuration(), null, null, store);
        await viewModel.LoadSnapshotAsync(CreateSnapshot([file]));

        viewModel.SelectedRow = Assert.Single(viewModel.PageRows);
        await WaitUntilAsync(() => viewModel.UserTags.Count >= 3);
        viewModel.SelectedUserTag = viewModel.UserTags.Single(tag => tag.DisplayName == "invoice");
        await viewModel.AcceptSuggestedTagCommand.ExecuteAsync(null);
        viewModel.SelectedUserTag = viewModel.UserTags.Single(tag => tag.DisplayName == "receipt");
        await viewModel.RejectSuggestedTagCommand.ExecuteAsync(null);

        Assert.Contains(store.Record.Tags, tag =>
            tag.TagId == first.TagId &&
            tag.AcceptanceState == TagAcceptanceState.Accepted);
        Assert.Contains(store.Record.Tags, tag =>
            tag.TagId == second.TagId &&
            tag.AcceptanceState == TagAcceptanceState.Rejected);
        Assert.DoesNotContain(viewModel.GetPersistableTags(), tag =>
            tag.TagId == first.TagId || tag.TagId == second.TagId);
    }

    /// <summary>Verifies visible row selection immediately supplies the AI rename context without a query refresh.</summary>
    [Fact]
    public async Task SelectedRow_ImmediatelyEnablesRenameSuggestionContext()
    {
        var file = CreateFile("file:0", "C:\\Selected\\invoice.txt", DuplicateStatus.Unique, null);
        var configuration = new AiEnabledConfiguration();
        using var viewModel = new ResultsViewModel(configuration, new NoopAiService());
        await viewModel.LoadSnapshotAsync(CreateSnapshot([file]));

        Assert.Null(viewModel.AiSuggestions.SelectedFile);
        Assert.False(viewModel.AiSuggestions.GenerateSuggestionCommand.CanExecute(null));

        viewModel.SelectedRow = Assert.Single(viewModel.PageRows);

        Assert.Equal(file.Id, viewModel.AiSuggestions.SelectedFile?.Id);
        Assert.True(viewModel.AiSuggestions.GenerateSuggestionCommand.CanExecute(null));
        Assert.Contains("Ready", viewModel.AiSuggestions.RenameActionAvailabilityText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies reconciliation preserves logical selection while replacing stale path projections.</summary>
    [Fact]
    public async Task ApplyReconciledSnapshotAsync_PreservesSelectionByFileIdentity()
    {
        var original = CreateSnapshot([
            CreateFile("file:stable", "C:\\Selected\\before.txt", DuplicateStatus.Unique, null),
        ]);
        using var viewModel = new ResultsViewModel();
        await viewModel.LoadSnapshotAsync(original);
        viewModel.QueryText = "txt";
        await viewModel.RefreshAsync();
        viewModel.SelectedRow = Assert.Single(viewModel.PageRows);

        var movedFile = original.Files[0] with
        {
            FullPath = "C:\\Selected\\after.txt",
            DisplayFileName = "after.txt",
            HasPlannedOperation = false,
        };
        var reconciled = original with
        {
            Files = Array.AsReadOnly([movedFile]),
            PlannedOperations = Array.AsReadOnly<ResultPlannedOperation>([]),
        };

        await viewModel.ApplyReconciledSnapshotAsync(reconciled);

        Assert.Equal("txt", viewModel.QueryText);
        Assert.Equal("file:stable", viewModel.SelectedRow?.FileId);
        Assert.Equal("C:\\Selected\\after.txt", viewModel.SelectedDetails?.FullPath);
        Assert.DoesNotContain(viewModel.PageRows, row => row.FullPath.EndsWith("before.txt", StringComparison.Ordinal));
    }

    /// <summary>Verifies secondary filters use progressive disclosure without resetting their values.</summary>
    [Fact]
    public void ToggleFilters_PreservesFilterStateWhileDrawerIsHidden()
    {
        using var viewModel = new ResultsViewModel();
        viewModel.SelectedDuplicateFilter = ResultDuplicateFilter.ExactDuplicatesOnly;

        viewModel.ToggleFiltersCommand.Execute(null);
        Assert.True(viewModel.AreFiltersVisible);
        viewModel.ToggleFiltersCommand.Execute(null);

        Assert.False(viewModel.AreFiltersVisible);
        Assert.Equal(ResultDuplicateFilter.ExactDuplicatesOnly, viewModel.SelectedDuplicateFilter);
    }

    /// <summary>Verifies Search-to-Files uses stable identity and restores the prior Files projection.</summary>
    [Fact]
    public async Task DiscoveryDocument_OpenAndReturn_PreservesStableIdentityAndPriorFilesState()
    {
        var original = CreateSnapshot([
            CreateFile("scan:file", "C:\\Selected\\ordinary.txt", DuplicateStatus.Unique, null),
        ]);
        using var viewModel = new ResultsViewModel();
        await viewModel.LoadSnapshotAsync(original);
        viewModel.QueryText = "ordinary";
        await viewModel.RefreshAsync();

        var indexed = CreateProgressiveDocument("index:stable", "C:\\Indexed\\renamed-invoice.pdf");
        var context = new DiscoveryWorkflowContext(
            new DiscoveryQueryState("invoice", []),
            "saved:finance",
            indexed.FileId,
            false,
            indexed.SourceId,
            [indexed.FileId]);

        await viewModel.OpenDiscoveryDocumentAsync(indexed, context);

        Assert.True(viewModel.IsDiscoveryContextActive);
        Assert.Equal("index:stable", Assert.Single(viewModel.PageRows).FileId);
        Assert.Equal("index:stable", viewModel.SelectedRow?.FileId);
        Assert.Contains("Saved search", viewModel.DiscoveryContextText, StringComparison.Ordinal);

        await viewModel.EndDiscoveryDocumentAsync();

        Assert.False(viewModel.IsDiscoveryContextActive);
        Assert.Same(original, viewModel.Snapshot);
        Assert.Equal("ordinary", viewModel.QueryText);
        Assert.Equal("scan:file", Assert.Single(viewModel.PageRows).FileId);
    }

    /// <summary>Verifies a moved indexed file can be reopened by stable identity without resurrecting the old path.</summary>
    [Fact]
    public async Task DiscoveryDocument_MovedPath_ReusesStableIdentityAndPublishesOnlyCurrentPath()
    {
        using var viewModel = new ResultsViewModel();
        var before = CreateProgressiveDocument("index:stable", "C:\\Indexed\\before.pdf");
        var context = new DiscoveryWorkflowContext(
            new DiscoveryQueryState("invoice", []), null, before.FileId, false, before.SourceId, [before.FileId]);
        await viewModel.OpenDiscoveryDocumentAsync(before, context);

        var after = before with { FullPath = "C:\\Indexed\\after.pdf", FileName = "after.pdf" };
        await viewModel.OpenDiscoveryDocumentAsync(after, context);

        var row = Assert.Single(viewModel.PageRows);
        Assert.Equal("index:stable", row.FileId);
        Assert.Equal("C:\\Indexed\\after.pdf", row.FullPath);
        Assert.DoesNotContain(viewModel.PageRows, item => item.FullPath.EndsWith("before.pdf", StringComparison.Ordinal));
    }

    /// <summary>Verifies bounded continuous review announces previous/next navigation without changing tag authority itself.</summary>
    [Fact]
    public async Task DiscoveryReview_UsesCapturedResultOrderForKeyboardNavigation()
    {
        using var viewModel = new ResultsViewModel();
        var document = CreateProgressiveDocument("index:2", "C:\\Indexed\\second.pdf");
        var context = new DiscoveryWorkflowContext(
            new DiscoveryQueryState(string.Empty, []), null, document.FileId, true, document.SourceId,
            ["index:1", "index:2", "index:3"]);
        await viewModel.OpenDiscoveryDocumentAsync(document, context);
        DiscoveryReviewDirection? requested = null;
        viewModel.ReviewNavigationRequested += (_, direction) => requested = direction;

        Assert.True(viewModel.IsContinuousReviewActive);
        Assert.Equal("Result 2 of 3", viewModel.ReviewPositionText);
        Assert.True(viewModel.PreviousReviewItemCommand.CanExecute(null));
        Assert.True(viewModel.NextReviewItemCommand.CanExecute(null));

        viewModel.NextReviewItemCommand.Execute(null);
        Assert.Equal(DiscoveryReviewDirection.Next, requested);
        viewModel.PreviousReviewItemCommand.Execute(null);
        Assert.Equal(DiscoveryReviewDirection.Previous, requested);
    }

    /// <summary>Verifies reviewed organization suggestions receive only user-authoritative or Strong deterministic evidence.</summary>
    [Fact]
    public async Task OrganizationEvidence_ExcludesUnresolvedModerateClassifications()
    {
        var tags = new SmartTags([
            SmartTag("file:stable", "theme.finance", SmartTagType.Theme, "Finance", ContentIntelligenceConfidence.Strong, SmartTagAssignmentState.Automatic),
            SmartTag("file:stable", "document-type.invoice", SmartTagType.DocumentType, "Invoice", ContentIntelligenceConfidence.Moderate, SmartTagAssignmentState.Accepted),
            SmartTag("file:stable", "theme.travel", SmartTagType.Theme, "Travel", ContentIntelligenceConfidence.Moderate, SmartTagAssignmentState.Suggested),
            SmartTag("file:stable", "user.review", SmartTagType.UserTag, "Review", ContentIntelligenceConfidence.Strong, SmartTagAssignmentState.Accepted),
        ]);
        var file = CreateFile("file:stable", "C:\\Selected\\invoice.pdf", DuplicateStatus.Unique, null);
        using var viewModel = new ResultsViewModel(new Configuration(), null, null, null, null, tags);
        await viewModel.LoadSnapshotAsync(CreateSnapshot([file]));
        viewModel.SelectedRow = Assert.Single(viewModel.PageRows);
        await WaitUntilAsync(() => viewModel.UserTags.Count == 4);

        Assert.Contains("Theme: Finance — Strong deterministic", viewModel.AiSuggestions.OrganizationEvidenceText, StringComparison.Ordinal);
        Assert.Contains("Document Type: Invoice — Accepted", viewModel.AiSuggestions.OrganizationEvidenceText, StringComparison.Ordinal);
        Assert.Contains("User Tag: Review — User-created", viewModel.AiSuggestions.OrganizationEvidenceText, StringComparison.Ordinal);
        Assert.DoesNotContain("Travel", viewModel.AiSuggestions.OrganizationEvidenceText, StringComparison.Ordinal);
    }

    private static ResultsSnapshot CreateSnapshot(IReadOnlyList<ResultFile> files, bool duplicateDataAvailable = true)
    {
        var groups = duplicateDataAvailable && files.Count(file => file.DuplicateGroupId == "group:opaque") >= 2
            ? Array.AsReadOnly<ResultDuplicateGroup>([
                new ResultDuplicateGroup("group:opaque", 1, Array.AsReadOnly(["file:0", "file:1"]), 2, 2, 2),
            ])
            : Array.AsReadOnly(Array.Empty<ResultDuplicateGroup>());
        var issues = duplicateDataAvailable
            ? Array.AsReadOnly<ResultIssue>([new ResultIssue("Scanning", ResultIssueSeverity.Warning, "A test scan warning.")])
            : Array.AsReadOnly<ResultIssue>([new ResultIssue("Exact duplicates", ResultIssueSeverity.Warning, "Exact duplicate review was unavailable for this completed scan.")]);
        var operations = Array.AsReadOnly<ResultPlannedOperation>([
            new ResultPlannedOperation("plan:0", PlannedOperationKind.Move, files[0].Id, "C:\\Destination\\first.txt", "Rule"),
        ]);
        var updatedFiles = files.Select((file, index) => file with { HasPlannedOperation = index == 0 }).ToArray();
        return new ResultsSnapshot(
            "session:test",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            Array.AsReadOnly(updatedFiles),
            Array.AsReadOnly<ResultDirectory>([new ResultDirectory("C:\\Selected", "Selected")]),
            groups,
            operations,
            issues,
            new ResultsSnapshotStatistics(updatedFiles.Length, 1, groups.Count, groups.Sum(group => group.MemberCount), 1, issues.Count, 0),
            duplicateDataAvailable);
    }

    private static ResultFile CreateFile(
        string id,
        string path,
        DuplicateStatus duplicateStatus,
        string? groupId,
        long size = 2,
        FileCategory category = FileCategory.Document) =>
        new(
            id,
            path,
            CrossPlatformPath.GetFileName(path),
            ".txt",
            size,
            DateTimeOffset.UnixEpoch,
            category,
            category.ToString(),
            duplicateStatus,
            groupId,
            false);

    private static ProgressiveSearchDocument CreateProgressiveDocument(string fileId, string path) => new()
    {
        FileId = fileId,
        FullPath = path,
        FileName = CrossPlatformPath.GetFileName(path),
        RelativePath = CrossPlatformPath.GetFileName(path),
        FolderName = "Indexed",
        Extension = Path.GetExtension(path),
        FileType = "Document",
        SourceId = "source:docs",
        SourceName = "Documents",
        Length = 42,
        ModifiedTimeUtc = DateTimeOffset.UnixEpoch,
        IsFullyIndexed = true,
    };

    private static FileSmartTag SmartTag(
        string fileId,
        string tagId,
        SmartTagType type,
        string label,
        ContentIntelligenceConfidence confidence,
        SmartTagAssignmentState state) => new()
        {
            FileId = fileId,
            Definition = new SmartTagDefinition
            {
                TagId = tagId,
                Type = type,
                CanonicalKey = tagId,
                DisplayName = label,
                TaxonomyVersion = type == SmartTagType.UserTag ? "user" : "1.0",
                Origin = type == SmartTagType.UserTag ? SmartTagOrigin.User : SmartTagOrigin.BuiltInTaxonomy,
                IsBuiltIn = type != SmartTagType.UserTag,
            },
            Confidence = confidence,
            Origin = type == SmartTagType.UserTag ? SmartTagOrigin.User : SmartTagOrigin.DeterministicClassifier,
            State = state,
            Decision = state == SmartTagAssignmentState.Accepted ? SmartTagDecision.Accepted : SmartTagDecision.None,
            Evidence = [new SmartTagEvidence(ContentEvidenceSourceKind.ExtractedText, "fixture", "Matched bounded local evidence")],
            UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        };

    private static TagAssociation GeneratedTag(string path, string value) => new(
        $"tag:generated:{value}",
        path,
        value,
        value,
        "OCR candidate",
        TagSource.OcrCandidate,
        TagAcceptanceState.Suggested,
        "Generated locally",
        DateTimeOffset.UnixEpoch)
    {
        Confidence = 0.5,
        UpdatedAtUtc = DateTimeOffset.UnixEpoch,
        SourceFingerprint = "2:0",
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class Configuration : IConfigurationService
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

    private sealed class AiEnabledConfiguration : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new()
        {
            Ai = new AiSettings
            {
                Enabled = true,
                FileRenameSuggestionsEnabled = true,
                SelectedModel = "local-model",
            },
        };

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopAiService : IAiSuggestionService
    {
        public Task<AiConnectionResult> TestConnectionAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new AiConnectionResult(AiAvailabilityState.Connected, "Connected", []));

        public Task<AiConnectionResult> DiscoverModelsAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new AiConnectionResult(AiAvailabilityState.Connected, "Connected", [new AiModel("local-model", "local-model")]));

        public Task<AiFileRenameResult> GenerateFileRenameAsync(AiFileRenameRequest request, AiSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new AiFileRenameResult(AiAvailabilityState.NoSuggestion, "No suggestion", null));

        public Task<AiFolderStructureResult> GenerateFolderStructureAsync(AiFolderStructureRequest request, AiSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new AiFolderStructureResult(AiAvailabilityState.NoSuggestion, "No suggestion", null));

        public Task<AiDecisionResult> RecordDecisionAsync(AiSuggestionDecision decision, AiSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new AiDecisionResult(AiAvailabilityState.ModelSelected, "Saved"));

        public Task<AiDecisionResult> ResetDecisionHistoryAsync(ApplicationSettings settings, CancellationToken cancellationToken) =>
            Task.FromResult(new AiDecisionResult(AiAvailabilityState.ModelSelected, "Reset"));
    }

    private sealed class ContentStore(ContentRecord initial) : IContentStore
    {
        public ContentRecord Record { get; private set; } = initial;
        public Task<ContentRecord?> GetAsync(string fullPath, CancellationToken cancellationToken) =>
            Task.FromResult<ContentRecord?>(Record);
        public Task<IReadOnlyList<ContentRecord>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContentRecord>>([Record]);
        public Task UpsertAsync(ContentRecord record, CancellationToken cancellationToken)
        {
            Record = record;
            return Task.CompletedTask;
        }
        public Task RemoveMissingAsync(IReadOnlyCollection<string> knownPaths, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SmartTags(
        IReadOnlyList<FileSmartTag> tags,
        IReadOnlyDictionary<string, string>? fileIdsByPath = null) : ISmartTagService
    {
        public Task<SmartTagOperationResult> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new SmartTagOperationResult(false, 0, "Ready"));
        public Task<IReadOnlyList<SmartTagDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SmartTagDefinition>>(tags.Select(tag => tag.Definition).ToArray());
        public Task<string?> ResolveActiveFileIdAsync(string fullPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(fileIdsByPath?.GetValueOrDefault(fullPath) ?? tags.FirstOrDefault()?.FileId);
        public Task<IReadOnlyDictionary<string, string>> ResolveActiveFileIdsAsync(
            IReadOnlyList<string> fullPaths,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(fullPaths
                .Where(path => fileIdsByPath?.ContainsKey(path) == true)
                .ToDictionary(path => path, path => fileIdsByPath![path], StringComparer.Ordinal));
        public Task<IReadOnlyList<FileSmartTag>> GetFileTagsAsync(string fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FileSmartTag>>(tags.Where(tag => tag.FileId == fileId).ToArray());
        public Task<SmartTagOperationResult> AddUserTagAsync(string fileId, string displayName, CancellationToken cancellationToken = default) => Operation();
        public Task<SmartTagOperationResult> DecideAsync(string fileId, string tagId, SmartTagDecision decision, CancellationToken cancellationToken = default) => Operation();
        public Task<SmartTagOperationResult> RemoveAsync(string fileId, string tagId, CancellationToken cancellationToken = default) => Operation();
        public Task<SmartTagOperationResult> ResetDecisionsAsync(string? fileId, CancellationToken cancellationToken = default) => Operation();
        public Task<SmartTagOperationResult> ClearGeneratedAsync(string? fileId, CancellationToken cancellationToken = default) => Operation();
        public Task<IReadOnlyList<string>> FilterAsync(SmartTagFilter filter, int maximumCount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        private static Task<SmartTagOperationResult> Operation() =>
            Task.FromResult(new SmartTagOperationResult(true, 1, "Applied"));
    }
}
