using OpenSorSe.Application.Relationships;

namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>
/// Adapts representable Knowledge Graph corrections to the existing provider-neutral v1.9
/// relationship authority. It never writes a graph-native decision for legacy-owned data.
/// </summary>
public sealed class RelationshipGraphAuthorityBridge : IGraphLegacyAuthorityBridge
{
    private const int MaximumRelationshipLookupCount = 1_000;
    private readonly IRelationshipService _relationshipService;

    /// <summary>Initializes the bridge over the existing relationship application service.</summary>
    public RelationshipGraphAuthorityBridge(IRelationshipService relationshipService)
    {
        _relationshipService = relationshipService ?? throw new ArgumentNullException(nameof(relationshipService));
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> UnlinkRelationshipAsync(
        string firstFileId,
        string secondFileId,
        bool preventRegeneration,
        CancellationToken cancellationToken = default)
    {
        GraphQueryService.ValidateId(firstFileId);
        GraphQueryService.ValidateId(secondFileId);
        if (string.Equals(firstFileId, secondFileId, StringComparison.Ordinal))
        {
            throw new ArgumentException("A legacy relationship requires two distinct file identities.");
        }

        var related = await _relationshipService
            .GetRelatedFilesAsync(
                firstFileId,
                maximumCount: MaximumRelationshipLookupCount,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var matches = related
            .Select(item => item.Relationship)
            .Where(item => IsSamePair(item, firstFileId, secondFileId))
            .DistinctBy(item => item.Id, StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new GraphAccessUnavailableException(
                matches.Length == 0
                    ? "legacy-relationship-not-resolvable"
                    : "legacy-relationship-ambiguous");
        }

        var result = await _relationshipService
            .UnlinkAsync(matches[0].Id, preventRegeneration, cancellationToken)
            .ConfigureAwait(false);
        return Map(result, "relationship");
    }

    /// <inheritdoc />
    public async Task<GraphOperationResult> SplitCollectionMemberAsync(
        string collectionId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        GraphQueryService.ValidateId(collectionId);
        GraphQueryService.ValidateId(fileId);
        var result = await _relationshipService
            .SplitCollectionMemberAsync(collectionId, fileId, cancellationToken)
            .ConfigureAwait(false);
        return Map(result, "Smart Collection membership");
    }

    private static bool IsSamePair(FileRelationship relationship, string firstFileId, string secondFileId) =>
        (string.Equals(relationship.FirstFileId, firstFileId, StringComparison.Ordinal) &&
         string.Equals(relationship.SecondFileId, secondFileId, StringComparison.Ordinal)) ||
        (string.Equals(relationship.FirstFileId, secondFileId, StringComparison.Ordinal) &&
         string.Equals(relationship.SecondFileId, firstFileId, StringComparison.Ordinal));

    private static GraphOperationResult Map(RelationshipOperationResult result, string authorityName)
    {
        ArgumentNullException.ThrowIfNull(result);
        var affected = (int)Math.Min(
            int.MaxValue,
            Math.Max(0L, result.AffectedRelationshipCount) + Math.Max(0L, result.AffectedCollectionCount));
        return new GraphOperationResult(
            result.Applied,
            result.Applied
                ? $"The authoritative v1.9 {authorityName} was updated. {result.Message}"
                : result.Message,
            affected);
    }
}
