namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Validates untrusted provider records at Application boundaries without storage coupling.</summary>
internal static class GraphBoundaryValidator
{
    internal static bool IsValid(GraphDecisionSnapshot snapshot) =>
        snapshot is not null && snapshot.IsValid && snapshot.Sequence >= 0 &&
        IsBounded(snapshot.CheckpointId, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(snapshot.CanonicalHash, GraphLimits.MaximumStableIdCharacters, allowEmpty: false);

    internal static bool IsValid(GraphAuthoritySnapshot authority) =>
        authority is not null && authority.PrivacySequence >= 0 && authority.CurrentSourceRevision >= 0 &&
        IsBounded(authority.LegacyDecisionManifestId, GraphLimits.MaximumStableIdCharacters, allowEmpty: !authority.IsAvailable) &&
        IsBounded(authority.CurrentSourceManifestId, GraphLimits.MaximumStableIdCharacters, allowEmpty: !authority.IsAvailable) &&
        IsBounded(authority.ReasonCode, 128, allowEmpty: false) &&
        (!authority.IsAllowed || authority.IsAvailable);

    internal static bool IsValid(GraphProjectionCoverage coverage) =>
        coverage is not null && coverage.ProjectedObservationCount >= 0 && coverage.TotalObservationCount >= 0 &&
        coverage.FailedCount >= 0 && coverage.WaitingCount >= 0 && coverage.ProjectionRevision >= 0 &&
        coverage.ProjectedObservationCount <= coverage.TotalObservationCount &&
        coverage.FailedCount <= coverage.TotalObservationCount - coverage.ProjectedObservationCount &&
        coverage.WaitingCount <= coverage.TotalObservationCount - coverage.ProjectedObservationCount - coverage.FailedCount &&
        coverage.IngestedRevision >= 0 && coverage.AppliedRevision >= 0 &&
        coverage.IngestedDecisionSequence >= 0 && coverage.AppliedDecisionSequence >= 0 &&
        coverage.IngestedPrivacySequence >= 0 && coverage.AppliedPrivacySequence >= 0 &&
        IsBounded(coverage.ManifestId, GraphLimits.MaximumStableIdCharacters, allowEmpty: true) &&
        IsBounded(coverage.IngestedManifestId, GraphLimits.MaximumStableIdCharacters, allowEmpty: true) &&
        IsBounded(coverage.AppliedManifestId, GraphLimits.MaximumStableIdCharacters, allowEmpty: true) &&
        IsBounded(coverage.IngestedDecisionCheckpointId, GraphLimits.MaximumStableIdCharacters, allowEmpty: true) &&
        IsBounded(coverage.AppliedDecisionCheckpointId, GraphLimits.MaximumStableIdCharacters, allowEmpty: true) &&
        IsBounded(coverage.Message, GraphLimits.MaximumDecisionReasonCharacters, allowEmpty: true);

    internal static bool IsValid(GraphNode node) =>
        node is not null && node.Identity is not null && node.Identity.Kind.IsStable &&
        IsBounded(node.Identity.NodeId, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(node.Identity.Scope, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(node.Identity.CanonicalKey, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(node.Identity.NormalizationVersion, 64, allowEmpty: false) &&
        IsBounded(node.Identity.CanonicalInputs, GraphLimits.MaximumCanonicalIdentityCharacters, allowEmpty: false) &&
        IsBounded(node.DisplayLabel, GraphLimits.MaximumLabelCharacters, allowEmpty: false) &&
        IsBounded(node.OwningSourceId, GraphLimits.MaximumStableIdCharacters, allowEmpty: true) &&
        IsBounded(node.SourceManifestId, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(node.ObservationHash, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(node.Algorithm, 64, allowEmpty: false) &&
        IsBounded(node.AlgorithmVersion, 64, allowEmpty: false) &&
        node.CreatedAtUtc != default && node.LastValidatedAtUtc != default &&
        node.LastValidatedAtUtc >= node.CreatedAtUtc &&
        Enum.IsDefined(node.Origin) && Enum.IsDefined(node.Freshness) && Enum.IsDefined(node.Integrity);

    internal static bool IsValid(GraphEdge edge) =>
        edge is not null && edge.Kind.IsStable &&
        IsBounded(edge.Id, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(edge.SourceNodeId, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(edge.TargetNodeId, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        !string.Equals(edge.SourceNodeId, edge.TargetNodeId, StringComparison.Ordinal) &&
        edge.EvidenceIds is not null && edge.EvidenceIds.Count <= GraphLimits.MaximumEvidencePerEdge &&
        edge.EvidenceIds.Distinct(StringComparer.Ordinal).Count() == edge.EvidenceIds.Count &&
        edge.EvidenceIds.All(item => IsBounded(item, GraphLimits.MaximumStableIdCharacters, allowEmpty: false)) &&
        IsBounded(edge.Algorithm, 64, allowEmpty: false) &&
        IsBounded(edge.AlgorithmVersion, 64, allowEmpty: false) &&
        IsBounded(edge.InputFingerprint, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        edge.CreatedAtUtc != default && edge.LastValidatedAtUtc != default &&
        edge.LastValidatedAtUtc >= edge.CreatedAtUtc &&
        Enum.IsDefined(edge.Confidence) && Enum.IsDefined(edge.Origin) &&
        Enum.IsDefined(edge.Freshness) && Enum.IsDefined(edge.Integrity);

    internal static bool IsValid(GraphEvidenceReference evidence) =>
        evidence is not null && evidence.Kind.IsStable &&
        IsBounded(evidence.Id, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(evidence.SourceEvidenceKey, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(evidence.ExplanationTemplateCode, 64, allowEmpty: false) &&
        IsBounded(evidence.Explanation, GraphLimits.MaximumEvidenceTextCharacters, allowEmpty: false) &&
        IsBounded(evidence.SourceManifestId, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(evidence.ObservationHash, GraphLimits.MaximumStableIdCharacters, allowEmpty: false);

    internal static bool IsValid(GraphFact fact) =>
        fact is not null && fact.Kind.IsStable &&
        IsBounded(fact.Id, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(fact.SubjectNodeId, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(fact.CanonicalValue, GraphLimits.MaximumCanonicalIdentityCharacters, allowEmpty: false) &&
        fact.EvidenceIds is not null && fact.EvidenceIds.Count <= GraphLimits.MaximumEvidencePerEdge &&
        fact.EvidenceIds.Distinct(StringComparer.Ordinal).Count() == fact.EvidenceIds.Count &&
        fact.EvidenceIds.All(item => IsBounded(item, GraphLimits.MaximumStableIdCharacters, allowEmpty: false)) &&
        IsBounded(fact.AlgorithmVersion, 64, allowEmpty: false);

    internal static bool IsValid(GraphTimelineEntry entry) =>
        entry is not null && entry.Kind is var kind &&
        (kind == GraphFactKind.CreatedTimestamp || kind == GraphFactKind.ModifiedTimestamp) &&
        IsBounded(entry.FactId, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        IsBounded(entry.SubjectNodeId, GraphLimits.MaximumStableIdCharacters, allowEmpty: false) &&
        entry.EvidenceIds is not null && entry.EvidenceIds.Count <= GraphLimits.MaximumEvidencePerEdge &&
        entry.EvidenceIds.Distinct(StringComparer.Ordinal).Count() == entry.EvidenceIds.Count &&
        entry.EvidenceIds.All(item => IsBounded(item, GraphLimits.MaximumStableIdCharacters, allowEmpty: false)) &&
        entry.OccurredAtUtc != default &&
        IsBounded(entry.AlgorithmVersion, 64, allowEmpty: false);

    internal static bool IsValid(GraphPageCursor? cursor) =>
        cursor is null || IsBounded(cursor.Value, GraphLimits.MaximumStableIdCharacters, allowEmpty: false);

    private static bool IsBounded(string? value, int maximumLength, bool allowEmpty)
    {
        if (value is null)
        {
            return allowEmpty;
        }

        return value.Length <= maximumLength &&
            !ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(value) &&
            (allowEmpty || !string.IsNullOrWhiteSpace(value));
    }
}
