using OpenSorSe.Application.Relationships;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Validates provider-neutral relationship presentation and index-only user control.</summary>
public sealed class CollectionsViewModelTests
{
    /// <summary>Retained relationships are shown while optional model lookup is still pending.</summary>
    [Fact]
    public async Task RelatedFiles_PublishesEvidenceBeforeSemanticLookupCompletes()
    {
        var semantic = new ControlledSemanticService();
        using var viewModel = new CollectionsViewModel(new RelationshipServiceStub(), semantic);
        await viewModel.RefreshAsync();

        viewModel.SelectedFile = viewModel.Files[0];

        Assert.True(viewModel.IsBusy);
        Assert.Single(viewModel.RelatedFiles);
        Assert.Single(viewModel.Corrections);
        Assert.Empty(viewModel.SemanticSuggestions);
        Assert.Contains("Loaded", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("Checking", viewModel.SemanticStatusText, StringComparison.Ordinal);
        var finished = ObserveSemanticCompletion(viewModel, "Semantic results for first.");
        semantic.FirstResult.SetResult(ControlledSemanticService.Result("first"));
        await finished;

        Assert.Equal("semantic-first", Assert.Single(viewModel.SemanticSuggestions).FileId);
    }

    /// <summary>A superseded lookup is cancelled and its late result cannot prevent the latest selection from loading.</summary>
    [Fact]
    public async Task RelatedFiles_SelectionDuringLookupRefreshesLatestAndDiscardsLateResults()
    {
        var semantic = new ControlledSemanticService();
        using var viewModel = new CollectionsViewModel(new RelationshipServiceStub(), semantic);
        await viewModel.RefreshAsync();
        viewModel.SelectedFile = viewModel.Files[0];
        var finished = ObserveSemanticCompletion(viewModel, "Semantic results for second.");

        viewModel.SelectedFile = viewModel.Files[1];
        Assert.True(semantic.FirstCancellation.IsCancellationRequested);
        Assert.Empty(viewModel.SemanticSuggestions);
        // Simulate a provider that completes despite cancellation; the presentation must
        // discard this result and drain the queued latest selection without an Apply click.
        semantic.FirstResult.SetResult(ControlledSemanticService.Result("first"));
        await finished;

        Assert.Equal(["first", "second"], semantic.RequestedIds);
        Assert.Equal("second", viewModel.SelectedFile.FileId);
        Assert.Single(viewModel.RelatedFiles);
        Assert.Equal("semantic-second", Assert.Single(viewModel.SemanticSuggestions).FileId);
        Assert.False(viewModel.IsBusy);
    }

    private static Task ObserveSemanticCompletion(CollectionsViewModel viewModel, string expectedStatus)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (!viewModel.IsBusy && viewModel.SemanticStatusText == expectedStatus)
            {
                viewModel.PropertyChanged -= Observe;
                finished.TrySetResult();
            }
        }
        viewModel.PropertyChanged += Observe;
        return finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class ControlledSemanticService : ISemanticRelatedFilesService
    {
        public TaskCompletionSource<SemanticRelatedFilesResult> FirstResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken FirstCancellation { get; private set; }
        public List<string> RequestedIds { get; } = [];
        public Task<SemanticRelatedFilesResult> GetRelatedAsync(string fileId, CancellationToken cancellationToken = default)
        {
            RequestedIds.Add(fileId);
            if (fileId == "first")
            {
                FirstCancellation = cancellationToken;
                return FirstResult.Task;
            }
            return Task.FromResult(Result(fileId));
        }
        public static SemanticRelatedFilesResult Result(string id) => new(
            [new SemanticRelatedFile("semantic-" + id, id + ".txt", id + ".txt", "Similar meaning only")],
            "Semantic results for " + id + ".");
    }

    /// <summary>Semantic suggestions do not acquire retained relationship authority or mutation commands.</summary>
    [Fact]
    public async Task RelatedFiles_SemanticSuggestionsAreSeparateFromEvidence()
    {
        var service = new RelationshipServiceStub();
        using var viewModel = new CollectionsViewModel(service, new SemanticService());
        await viewModel.RefreshAsync();
        viewModel.SelectedFile = viewModel.Files[0];

        Assert.Single(viewModel.RelatedFiles);
        Assert.Equal("semantic-only", Assert.Single(viewModel.SemanticSuggestions).FileId);
        Assert.Contains("not a verified relationship", viewModel.SemanticStatusText, StringComparison.Ordinal);
        Assert.Null(viewModel.SelectedRelatedFile);
        Assert.False(viewModel.MarkRelatedCommand.CanExecute(null));
    }

    /// <summary>An optional similarity failure leaves direct retained evidence inspectable.</summary>
    [Fact]
    public async Task RelatedFiles_SemanticFailurePreservesDirectRelationships()
    {
        using var viewModel = new CollectionsViewModel(new RelationshipServiceStub(), new SemanticService(fail: true));
        await viewModel.RefreshAsync();
        viewModel.SelectedFile = viewModel.Files[0];

        Assert.Single(viewModel.RelatedFiles);
        Assert.Empty(viewModel.SemanticSuggestions);
        Assert.Contains("temporarily unavailable", viewModel.SemanticStatusText, StringComparison.Ordinal);
    }

    private sealed class SemanticService(bool fail = false) : ISemanticRelatedFilesService
    {
        public Task<SemanticRelatedFilesResult> GetRelatedAsync(string fileId, CancellationToken cancellationToken = default) => fail
            ? Task.FromException<SemanticRelatedFilesResult>(new IOException("offline"))
            : Task.FromResult(new SemanticRelatedFilesResult(
                [new SemanticRelatedFile("semantic-only", "tent.txt", "tent.txt", "Model test; cosine 0.8; chunk 1")],
                "Similar meaning is not a verified relationship."));
    }

    /// <summary>Verifies refresh and selection expose collection evidence, members, and timeline without filesystem access.</summary>
    [Fact]
    public async Task RefreshAndSelectCollection_PublishesInspectableEvidence()
    {
        var service = new RelationshipServiceStub();
        using var viewModel = new CollectionsViewModel(service);

        await viewModel.RefreshAsync();
        viewModel.SelectedCollection = Assert.Single(viewModel.Collections);

        Assert.Equal(2, viewModel.Files.Count);
        Assert.Equal(2, viewModel.Members.Count);
        Assert.Single(viewModel.Relationships);
        Assert.Equal("Same invoice number", viewModel.Relationships[0].Explanation);
        Assert.Equal(2, viewModel.Timeline.Count);
        Assert.Contains("evidence", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("relationships", viewModel.DiagnosticsText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies manual linking and file forgetting remain explicit provider-neutral index operations.</summary>
    [Fact]
    public async Task ManualLinkAndForgetFile_InvokeOnlyRelationshipService()
    {
        var service = new RelationshipServiceStub();
        using var viewModel = new CollectionsViewModel(service);
        await viewModel.RefreshAsync();
        viewModel.FirstLinkFile = viewModel.Files[0];
        viewModel.SecondLinkFile = viewModel.Files[1];
        viewModel.LinkType = RelationshipType.SamePurchase;
        viewModel.AlwaysRelate = true;

        await viewModel.LinkFilesCommand.ExecuteAsync(null);
        viewModel.SelectedFile = viewModel.Files[0];
        await viewModel.ForgetFileRelationshipsCommand.ExecuteAsync(null);

        Assert.Equal(1, service.LinkCount);
        Assert.True(service.LastAlwaysRelate);
        Assert.Equal(0, service.ForgetFileCount);
        Assert.True(viewModel.IsDestructiveConfirmationPending);
        Assert.Contains("exclude", viewModel.DestructiveConfirmationText, StringComparison.OrdinalIgnoreCase);

        await viewModel.ConfirmDestructiveActionCommand.ExecuteAsync(null);

        Assert.Equal(1, service.ForgetFileCount);
        Assert.True(service.LastExcludeFuture);
        Assert.Contains("original", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a pending authority-removing operation can be cancelled without calling storage.</summary>
    [Fact]
    public async Task Unlink_CancelledAfterReview_DoesNotInvokeService()
    {
        var service = new RelationshipServiceStub();
        using var viewModel = new CollectionsViewModel(service);
        await viewModel.RefreshAsync();
        viewModel.SelectedCollection = Assert.Single(viewModel.Collections);
        viewModel.SelectedRelationship = Assert.Single(viewModel.Relationships);

        await viewModel.UnlinkCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsDestructiveConfirmationPending);
        Assert.Equal(0, service.UnlinkCount);

        viewModel.CancelDestructiveActionCommand.Execute(null);

        Assert.False(viewModel.IsDestructiveConfirmationPending);
        Assert.Equal(0, service.UnlinkCount);
    }

    /// <summary>Verifies relationship operations surface safe failures without leaking an exception into the UI thread.</summary>
    [Fact]
    public async Task RepairFailure_IsPresentedSafely()
    {
        var service = new RelationshipServiceStub { FailRepair = true };
        using var viewModel = new CollectionsViewModel(service);

        await viewModel.RepairCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsDestructiveConfirmationPending);
        Assert.Equal(0, service.RepairCount);

        await viewModel.ConfirmDestructiveActionCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
        Assert.Equal(1, service.RepairCount);
        Assert.Contains("failed safely", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies an existing suggestion can be retained with the persistent always-relate correction.</summary>
    [Fact]
    public async Task AlwaysRelateRelationship_PersistsExplicitDecision()
    {
        var service = new RelationshipServiceStub();
        using var viewModel = new CollectionsViewModel(service);
        await viewModel.RefreshAsync();
        viewModel.SelectedCollection = Assert.Single(viewModel.Collections);
        viewModel.SelectedRelationship = Assert.Single(viewModel.Relationships);

        await viewModel.AlwaysRelateRelationshipCommand.ExecuteAsync(null);

        Assert.Equal(RelationshipDecision.AlwaysRelate, service.LastDecision);
    }

    /// <summary>Verifies direct Related Files corrections work without any Knowledge Graph service.</summary>
    [Fact]
    public async Task DirectRelatedFiles_AuthorityIsVisibleAndReversibleWithoutGraph()
    {
        var service = new RelationshipServiceStub();
        using var viewModel = new CollectionsViewModel(service);

        await viewModel.RefreshAsync();
        await viewModel.SelectFileAsync("first");
        Assert.Equal(1, viewModel.SelectedSectionIndex);
        viewModel.SelectedRelatedFile = Assert.Single(viewModel.RelatedFiles);

        await viewModel.MarkRelatedCommand.ExecuteAsync(null);
        Assert.Equal(RelationshipDecision.AlwaysRelate, service.LastDecision);
        await viewModel.MarkNotRelatedCommand.ExecuteAsync(null);
        Assert.Equal(RelationshipDecision.NeverRelate, service.LastDecision);
        await viewModel.UseAutomaticCommand.ExecuteAsync(null);

        Assert.Equal(0, service.AutomaticResetCount);
        Assert.True(viewModel.IsDestructiveConfirmationPending);

        viewModel.SelectedFile = viewModel.Files[1];

        await viewModel.ConfirmDestructiveActionCommand.ExecuteAsync(null);

        Assert.Equal(1, service.AutomaticResetCount);
        Assert.Equal("first", service.LastAutomaticFirstId);
        Assert.Equal("second", service.LastAutomaticSecondId);
        Assert.Single(viewModel.Corrections);
    }

    private sealed class RelationshipServiceStub : IRelationshipService
    {
        private static readonly DateTimeOffset Now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        private static readonly RelationshipFileDocument First = File("first", "invoice.pdf");
        private static readonly RelationshipFileDocument Second = File("second", "receipt.pdf");
        private static readonly FileRelationship Relationship = new()
        {
            Id = "relationship",
            FirstFileId = First.FileId,
            SecondFileId = Second.FileId,
            Type = RelationshipType.SamePurchase,
            Confidence = RelationshipConfidence.High,
            Evidence = [new RelationshipEvidence(RelationshipEvidenceKind.Filename, "invoice", "Same invoice number")],
            Algorithm = "test",
            AlgorithmVersion = "1",
            CreatedAtUtc = Now,
            LastValidatedAtUtc = Now,
        };
        private static readonly SmartCollection Collection = new()
        {
            Id = "collection",
            Title = "Purchase",
            Description = "Synthetic purchase.",
            RelationshipSummary = "Same invoice number",
            ContextType = RelationshipType.SamePurchase,
            Confidence = RelationshipConfidence.High,
            CreationSource = SmartCollectionCreationSource.Automatic,
            MemberCount = 2,
            LastUpdatedAtUtc = Now,
        };

        public int LinkCount { get; private set; }
        public int ForgetFileCount { get; private set; }
        public bool LastAlwaysRelate { get; private set; }
        public bool LastExcludeFuture { get; private set; }
        public RelationshipDecision? LastDecision { get; private set; }
        public int AutomaticResetCount { get; private set; }
        public string? LastAutomaticFirstId { get; private set; }
        public string? LastAutomaticSecondId { get; private set; }
        public int UnlinkCount { get; private set; }
        public int RepairCount { get; private set; }
        public bool FailRepair { get; init; }

        public Task<RelationshipAnalysisResult> AnalyzeFileAsync(string fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RelationshipAnalysisResult(fileId, 0, 0, 0, TimeSpan.Zero, false, "complete"));

        public Task<IReadOnlyList<RelationshipFileDocument>> GetFilesAsync(int maximumCount = 1000, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RelationshipFileDocument>>([First, Second]);

        public Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(
            string fileId,
            RelationshipType? type = null,
            RelationshipConfidence? minimumConfidence = null,
            RelatedFileSort sort = RelatedFileSort.Confidence,
            int maximumCount = 200,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RelatedFile>>([
                new RelatedFile
                {
                    FileId = Second.FileId,
                    FileName = Second.FileName,
                    FullPath = Second.FullPath,
                    SourceName = Second.SourceName,
                    Relationship = Relationship,
                },
            ]);

        public Task<IReadOnlyList<RelationshipPairCorrection>> GetCorrectionsAsync(
            string fileId,
            int maximumCount = 200,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RelationshipPairCorrection>>([
                new RelationshipPairCorrection(
                    First.FileId,
                    Second.FileId,
                    Second.FileId,
                    Second.FileName,
                    Second.FullPath,
                    Second.SourceName,
                    RelationshipDecision.NeverRelate,
                    RelationshipType.SamePurchase,
                    null,
                    Now,
                    true),
            ]);

        public Task<FileRelationship?> GetRelationshipAsync(string relationshipId, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileRelationship?>(Relationship);

        public Task<IReadOnlyList<SmartCollection>> GetCollectionsAsync(int maximumCount = 500, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SmartCollection>>([Collection]);

        public Task<SmartCollectionDetails?> GetCollectionAsync(string collectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SmartCollectionDetails?>(new SmartCollectionDetails(
                Collection,
                [Member(First), Member(Second)],
                [Relationship],
                [Timeline(First), Timeline(Second)]));

        public Task<RelationshipOperationResult> LinkFilesAsync(
            string firstFileId,
            string secondFileId,
            RelationshipType type,
            string? customType = null,
            bool alwaysRelate = false,
            CancellationToken cancellationToken = default)
        {
            LinkCount++;
            LastAlwaysRelate = alwaysRelate;
            return Success("The files were linked in the index. Original files were unchanged.");
        }

        public Task<RelationshipOperationResult> UnlinkAsync(string relationshipId, bool neverRelate = false, CancellationToken cancellationToken = default)
        {
            UnlinkCount++;
            return Success("unlinked");
        }
        public Task<RelationshipOperationResult> SetDecisionAsync(string relationshipId, RelationshipDecision decision, CancellationToken cancellationToken = default)
        {
            LastDecision = decision;
            return Success("saved");
        }
        public Task<RelationshipOperationResult> UseAutomaticAsync(
            string firstFileId,
            string secondFileId,
            CancellationToken cancellationToken = default)
        {
            AutomaticResetCount++;
            LastAutomaticFirstId = firstFileId;
            LastAutomaticSecondId = secondFileId;
            return Success("automatic");
        }
        public Task<RelationshipOperationResult> RenameCollectionAsync(string collectionId, string title, CancellationToken cancellationToken = default) => Success("renamed");
        public Task<RelationshipOperationResult> SetCollectionPinnedAsync(string collectionId, bool pinned, CancellationToken cancellationToken = default) => Success("pinned");
        public Task<RelationshipOperationResult> MergeCollectionsAsync(string targetCollectionId, string sourceCollectionId, CancellationToken cancellationToken = default) => Success("merged");
        public Task<RelationshipOperationResult> SplitCollectionMemberAsync(string collectionId, string fileId, CancellationToken cancellationToken = default) => Success("split");
        public Task<RelationshipOperationResult> ForgetCollectionAsync(string collectionId, CancellationToken cancellationToken = default) => Success("forgotten");

        public Task<RelationshipOperationResult> ForgetFileAsync(string fileId, bool excludeFutureAnalysis, CancellationToken cancellationToken = default)
        {
            ForgetFileCount++;
            LastExcludeFuture = excludeFutureAnalysis;
            return Success("Relationship data was forgotten. The original file was unchanged.");
        }

        public Task<RelationshipOperationResult> ForgetSourceAsync(string sourceId, bool excludeFutureAnalysis, CancellationToken cancellationToken = default) => Success("source forgotten");
        public Task<RelationshipOperationResult> RebuildFileAsync(string fileId, CancellationToken cancellationToken = default) => Success("rebuilt");
        public Task<RelationshipOperationResult> RepairAsync(CancellationToken cancellationToken = default)
        {
            RepairCount++;
            return FailRepair ? Task.FromException<RelationshipOperationResult>(new InvalidDataException("synthetic")) : Success("consistent");
        }

        public Task<IReadOnlyList<RelationshipSearchExpansion>> ExpandSearchAsync(IReadOnlyList<string> seedFileIds, int maximumCount, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RelationshipSearchExpansion>>([]);

        public Task<RelationshipDiagnosticsSnapshot> GetDiagnosticsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RelationshipDiagnosticsSnapshot(1, 1, 1, 0, 0, 0, Now, TimeSpan.FromMilliseconds(2), 2, 1, 1, "1", 0));

        private static Task<RelationshipOperationResult> Success(string message) => Task.FromResult(new RelationshipOperationResult(true, 1, 1, message));

        private static RelationshipFileDocument File(string id, string name) => new()
        {
            FileId = id,
            SourceId = "source",
            SourceName = "Synthetic source",
            FullPath = "/synthetic/" + name,
            RelativePath = name,
            FileName = name,
            FolderName = "synthetic",
            Extension = Path.GetExtension(name),
            IsFullyIndexed = true,
        };

        private static SmartCollectionMember Member(RelationshipFileDocument file) =>
            new(Collection.Id, file.FileId, file.FileName, file.FullPath, file.SourceName, CollectionMembershipSource.Automatic, Now);

        private static CollectionTimelineEvent Timeline(RelationshipFileDocument file) =>
            new(file.FileId, file.FileName, Now, "File modified", "Indexed modified time");
    }
}
