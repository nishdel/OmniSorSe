using OpenSorSe.Application.Relationships;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Validates provider-neutral relationship presentation and index-only user control.</summary>
public sealed class CollectionsViewModelTests
{
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
        Assert.Equal(1, service.ForgetFileCount);
        Assert.True(service.LastExcludeFuture);
        Assert.Contains("original", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies relationship operations surface safe failures without leaking an exception into the UI thread.</summary>
    [Fact]
    public async Task RepairFailure_IsPresentedSafely()
    {
        var service = new RelationshipServiceStub { FailRepair = true };
        using var viewModel = new CollectionsViewModel(service);

        await viewModel.RepairCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
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

        public Task<RelationshipOperationResult> UnlinkAsync(string relationshipId, bool neverRelate = false, CancellationToken cancellationToken = default) => Success("unlinked");
        public Task<RelationshipOperationResult> SetDecisionAsync(string relationshipId, RelationshipDecision decision, CancellationToken cancellationToken = default)
        {
            LastDecision = decision;
            return Success("saved");
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
        public Task<RelationshipOperationResult> RepairAsync(CancellationToken cancellationToken = default) =>
            FailRepair ? Task.FromException<RelationshipOperationResult>(new InvalidDataException("synthetic")) : Success("consistent");

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
