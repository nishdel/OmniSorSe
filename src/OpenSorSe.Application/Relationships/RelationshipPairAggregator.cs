namespace OpenSorSe.Application.Relationships;

/// <summary>Creates deterministic bounded pair-level projections from persisted typed edges.</summary>
public static class RelationshipPairAggregator
{
    /// <summary>Aggregates multiple typed edges so one related file appears only once.</summary>
    public static IReadOnlyList<RelatedFileContext> Aggregate(
        IReadOnlyList<RelatedFile> relatedFiles,
        int maximumCount,
        RelatedFileSort sort = RelatedFileSort.Confidence)
    {
        ArgumentNullException.ThrowIfNull(relatedFiles);
        if (maximumCount is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var contexts = relatedFiles
            .GroupBy(item => item.FileId, StringComparer.Ordinal)
            .Select(CreateContext);
        var ordered = sort switch
        {
            RelatedFileSort.Relationship => contexts
                .OrderBy(item => item.PrimaryRelationship.Type)
                .ThenByDescending(item => item.PrimaryRelationship.Confidence),
            RelatedFileSort.FileName => contexts
                .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.FileId, StringComparer.Ordinal),
            RelatedFileSort.LastValidated => contexts
                .OrderByDescending(item => item.LastValidatedAtUtc),
            _ => contexts
                .OrderByDescending(item => item.Decision is RelationshipDecision.Confirmed or RelationshipDecision.AlwaysRelate)
                .ThenByDescending(item => item.PrimaryRelationship.Confidence)
                .ThenBy(item => item.PrimaryRelationship.Type),
        };
        return ordered
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FileId, StringComparer.Ordinal)
            .Take(maximumCount)
            .ToArray();
    }

    /// <summary>Converts pair-level contexts back to the compatibility RelatedFile projection.</summary>
    public static IReadOnlyList<RelatedFile> ToRelatedFiles(IReadOnlyList<RelatedFileContext> contexts) =>
        contexts.Select(item => new RelatedFile
        {
            FileId = item.FileId,
            FileName = item.FileName,
            FullPath = item.FullPath,
            SourceName = item.SourceName,
            Relationship = item.PrimaryRelationship with { Evidence = item.Evidence },
            ContributingRelationships = item.Relationships,
        }).ToArray();

    private static RelatedFileContext CreateContext(IGrouping<string, RelatedFile> group)
    {
        var first = group.First();
        var relationships = group
            .SelectMany(item => item.ContributingRelationships.Count > 0
                ? item.ContributingRelationships
                : [item.Relationship])
            .DistinctBy(item => item.Id)
            .OrderByDescending(item => item.Decision is RelationshipDecision.Confirmed or RelationshipDecision.AlwaysRelate)
            .ThenByDescending(item => item.IsManual)
            .ThenByDescending(item => item.Confidence)
            .ThenBy(item => item.Type)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        var primary = relationships[0];
        var evidence = relationships
            .SelectMany(item => item.Evidence)
            .DistinctBy(item => (item.Kind, item.EvidenceKey))
            .OrderBy(item => item.Family)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.EvidenceKey, StringComparer.Ordinal)
            .Take(RelationshipLimits.MaximumEvidencePerRelationship)
            .ToArray();
        var decision = relationships
            .Select(item => item.Decision)
            .OrderByDescending(DecisionPriority)
            .FirstOrDefault();
        return new RelatedFileContext(
            first.FileId,
            first.FileName,
            first.FullPath,
            first.SourceName,
            primary,
            relationships,
            relationships.Select(item => item.Type).Distinct().Order().ToArray(),
            evidence,
            decision,
            relationships.Max(item => item.LastValidatedAtUtc),
            primary.Algorithm,
            primary.AlgorithmVersion);
    }

    private static int DecisionPriority(RelationshipDecision decision) => decision switch
    {
        RelationshipDecision.AlwaysRelate => 4,
        RelationshipDecision.Confirmed => 3,
        RelationshipDecision.NeverRelate => 2,
        RelationshipDecision.Rejected => 1,
        _ => 0,
    };
}
