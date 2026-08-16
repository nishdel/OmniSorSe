namespace OpenSorSe.Application.Relationships;

/// <summary>Contains one exact-file-pair manual relationship decision for logical state backup.</summary>
public sealed record RelationshipUserAuthority(
    string FirstFileId,
    string SecondFileId,
    RelationshipDecision Decision,
    RelationshipType Type,
    string? CustomType,
    bool IsManualRelationship);

/// <summary>Reports exact-file-pair relationship authority restoration without path guessing.</summary>
public sealed record RelationshipAuthorityRestoreResult(int AppliedCount, int SkippedCount);

/// <summary>Discovers deterministic, evidence-backed file relationships.</summary>
public interface IRelationshipEngine
{
    /// <summary>Gets the stable algorithm name retained with generated relationships.</summary>
    string Algorithm { get; }

    /// <summary>Gets the algorithm version used for incremental invalidation.</summary>
    string Version { get; }

    /// <summary>Builds bounded candidate-selection features for one indexed file.</summary>
    RelationshipFeatureSet CreateFeatures(RelationshipFileDocument document);

    /// <summary>Discovers bounded explainable relationships without storage or UI dependencies.</summary>
    IReadOnlyList<RelationshipProposal> Discover(
        RelationshipFileDocument target,
        IReadOnlyList<RelationshipFileDocument> candidates,
        int maximumRelationships,
        CancellationToken cancellationToken = default);
}

/// <summary>Persists relationships and collections without exposing provider-specific APIs.</summary>
public interface IRelationshipStore
{
    /// <summary>Exports bounded user-created relationships and persistent pair corrections.</summary>
    Task<IReadOnlyList<RelationshipUserAuthority>> ExportRelationshipUserAuthorityAsync(
        int maximumCount,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The configured relationship store does not support logical authority export.");

    /// <summary>Restores authority only when both exact stable file identities still exist.</summary>
    Task<RelationshipAuthorityRestoreResult> RestoreRelationshipUserAuthorityAsync(
        IReadOnlyList<RelationshipUserAuthority> authority,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The configured relationship store does not support logical authority restore.");

    /// <summary>Removes only the supplied pair authority during failed-restore compensation.</summary>
    Task RemoveRelationshipUserAuthorityAsync(
        IReadOnlyList<RelationshipUserAuthority> authority,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The configured relationship store does not support logical authority compensation.");

    /// <summary>Returns one bounded indexed file document for relationship analysis.</summary>
    Task<RelationshipFileDocument?> GetRelationshipFileAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>Stores selection features independently from automatic relationship output.</summary>
    Task UpsertRelationshipFeaturesAsync(
        RelationshipFeatureSet features,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns bounded candidates selected through indexed relationship features.</summary>
    Task<IReadOnlyList<RelationshipFileDocument>> GetRelationshipCandidatesAsync(
        RelationshipFeatureSet target,
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces stale automatic output for one file and refreshes affected collections.</summary>
    Task SaveRelationshipAnalysisAsync(
        RelationshipAnalysisBatch batch,
        int maximumCollectionMembers,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a bounded file list for manual controls.</summary>
    Task<IReadOnlyList<RelationshipFileDocument>> GetRelationshipFilesAsync(
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Returns bounded direct relationships for one file.</summary>
    Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(
        string fileId,
        RelationshipType? type,
        RelationshipConfidence? minimumConfidence,
        RelatedFileSort sort,
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one inspectable relationship.</summary>
    Task<FileRelationship?> GetRelationshipAsync(string relationshipId, CancellationToken cancellationToken = default);

    /// <summary>Returns bounded Smart Collections in stable display order.</summary>
    Task<IReadOnlyList<SmartCollection>> GetCollectionsAsync(int maximumCount, CancellationToken cancellationToken = default);

    /// <summary>Returns one collection, members, relationships, and bounded timeline.</summary>
    Task<SmartCollectionDetails?> GetCollectionAsync(
        string collectionId,
        int maximumMembers,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces an explicit manual relationship.</summary>
    Task<RelationshipOperationResult> LinkFilesAsync(
        string firstFileId,
        string secondFileId,
        RelationshipType type,
        string? customType,
        bool alwaysRelate,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one relationship and optionally prevents immediate automatic recreation.</summary>
    Task<RelationshipOperationResult> UnlinkFilesAsync(
        string relationshipId,
        bool neverRelate,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a user confirmation, rejection, always, or never correction.</summary>
    Task<RelationshipOperationResult> SetRelationshipDecisionAsync(
        string relationshipId,
        RelationshipDecision decision,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Renames one virtual collection without moving original files.</summary>
    Task<RelationshipOperationResult> RenameCollectionAsync(
        string collectionId,
        string title,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Pins or unpins one virtual collection.</summary>
    Task<RelationshipOperationResult> SetCollectionPinnedAsync(
        string collectionId,
        bool pinned,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Merges two virtual collections without moving original files.</summary>
    Task<RelationshipOperationResult> MergeCollectionsAsync(
        string targetCollectionId,
        string sourceCollectionId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Removes one member from a collection and retains an anti-regeneration override.</summary>
    Task<RelationshipOperationResult> SplitCollectionMemberAsync(
        string collectionId,
        string fileId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets one virtual collection while preserving every original file.</summary>
    Task<RelationshipOperationResult> ForgetCollectionAsync(
        string collectionId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets automatic relationship data for a file and optionally excludes future analysis.</summary>
    Task<RelationshipOperationResult> ForgetFileRelationshipsAsync(
        string fileId,
        bool excludeFutureAnalysis,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets automatic relationship data for a source without changing source ownership.</summary>
    Task<RelationshipOperationResult> ForgetSourceRelationshipsAsync(
        string sourceId,
        bool excludeFutureAnalysis,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Clears automatic output so one indexed file can be rebuilt incrementally.</summary>
    Task<RelationshipOperationResult> PrepareRelationshipRebuildAsync(
        string fileId,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Returns bounded direct relationship Search expansions for seed files.</summary>
    Task<IReadOnlyList<RelationshipSearchExpansion>> GetSearchExpansionsAsync(
        IReadOnlyList<string> seedFileIds,
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Returns privacy-safe aggregate diagnostics.</summary>
    Task<RelationshipDiagnosticsSnapshot> GetRelationshipDiagnosticsAsync(CancellationToken cancellationToken = default);

    /// <summary>Repairs stale memberships, orphan evidence, and inconsistent relationship rows.</summary>
    Task<RelationshipOperationResult> RepairRelationshipsAsync(
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Coordinates incremental analysis, manual control, Search expansion, and privacy.</summary>
public interface IRelationshipService
{
    /// <summary>Analyzes one indexed file incrementally.</summary>
    Task<RelationshipAnalysisResult> AnalyzeFileAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>Returns a bounded file list for manual relationship controls.</summary>
    Task<IReadOnlyList<RelationshipFileDocument>> GetFilesAsync(
        int maximumCount = 1_000,
        CancellationToken cancellationToken = default);

    /// <summary>Returns Related Files with evidence, confidence, and origin.</summary>
    Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(
        string fileId,
        RelationshipType? type = null,
        RelationshipConfidence? minimumConfidence = null,
        RelatedFileSort sort = RelatedFileSort.Confidence,
        int maximumCount = 200,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one relationship for inspection.</summary>
    Task<FileRelationship?> GetRelationshipAsync(string relationshipId, CancellationToken cancellationToken = default);

    /// <summary>Returns bounded Smart Collections.</summary>
    Task<IReadOnlyList<SmartCollection>> GetCollectionsAsync(
        int maximumCount = 500,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one collection inspector projection.</summary>
    Task<SmartCollectionDetails?> GetCollectionAsync(string collectionId, CancellationToken cancellationToken = default);

    /// <summary>Links two indexed files manually.</summary>
    Task<RelationshipOperationResult> LinkFilesAsync(
        string firstFileId,
        string secondFileId,
        RelationshipType type,
        string? customType = null,
        bool alwaysRelate = false,
        CancellationToken cancellationToken = default);

    /// <summary>Unlinks one relationship.</summary>
    Task<RelationshipOperationResult> UnlinkAsync(
        string relationshipId,
        bool neverRelate = false,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a user correction.</summary>
    Task<RelationshipOperationResult> SetDecisionAsync(
        string relationshipId,
        RelationshipDecision decision,
        CancellationToken cancellationToken = default);

    /// <summary>Renames one virtual collection.</summary>
    Task<RelationshipOperationResult> RenameCollectionAsync(
        string collectionId,
        string title,
        CancellationToken cancellationToken = default);

    /// <summary>Pins or unpins one virtual collection.</summary>
    Task<RelationshipOperationResult> SetCollectionPinnedAsync(
        string collectionId,
        bool pinned,
        CancellationToken cancellationToken = default);

    /// <summary>Merges two collections.</summary>
    Task<RelationshipOperationResult> MergeCollectionsAsync(
        string targetCollectionId,
        string sourceCollectionId,
        CancellationToken cancellationToken = default);

    /// <summary>Splits one member from a collection.</summary>
    Task<RelationshipOperationResult> SplitCollectionMemberAsync(
        string collectionId,
        string fileId,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets one virtual collection.</summary>
    Task<RelationshipOperationResult> ForgetCollectionAsync(string collectionId, CancellationToken cancellationToken = default);

    /// <summary>Forgets one file's relationship data without changing the original file.</summary>
    Task<RelationshipOperationResult> ForgetFileAsync(
        string fileId,
        bool excludeFutureAnalysis,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets one source's relationship data without changing source files or source ownership.</summary>
    Task<RelationshipOperationResult> ForgetSourceAsync(
        string sourceId,
        bool excludeFutureAnalysis,
        CancellationToken cancellationToken = default);

    /// <summary>Rebuilds one file's automatic relationship data.</summary>
    Task<RelationshipOperationResult> RebuildFileAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>Repairs stale or inconsistent relationship storage.</summary>
    Task<RelationshipOperationResult> RepairAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns bounded relationship-aware Search expansion.</summary>
    Task<IReadOnlyList<RelationshipSearchExpansion>> ExpandSearchAsync(
        IReadOnlyList<string> seedFileIds,
        int maximumCount,
        CancellationToken cancellationToken = default);

    /// <summary>Returns privacy-safe relationship diagnostics.</summary>
    Task<RelationshipDiagnosticsSnapshot> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Supplies optional provider-neutral relationship expansion to Search.</summary>
public interface IRelationshipSearchSource
{
    /// <summary>Returns bounded direct expansions for already-ranked seed files.</summary>
    Task<IReadOnlyList<RelationshipSearchExpansion>> ExpandAsync(
        IReadOnlyList<string> seedFileIds,
        int maximumCount,
        CancellationToken cancellationToken = default);
}
