namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Identifies a stable focus destination after an asynchronous graph action.</summary>
public enum KnowledgeGraphFocusTarget
{
    /// <summary>The Knowledge Graph page heading.</summary>
    PageHeading,
    /// <summary>The explicit enable control.</summary>
    EnableControl,
    /// <summary>The bounded node list.</summary>
    NodeList,
    /// <summary>The bounded direct-neighbor list.</summary>
    NeighborList,
    /// <summary>The privacy section heading.</summary>
    PrivacyHeading,
    /// <summary>The repair section heading.</summary>
    RepairHeading,
    /// <summary>The control that initiated the current confirmation.</summary>
    InitiatingControl,
    /// <summary>The confirmation control for a reviewed graph correction.</summary>
    DecisionConfirmation,
}

/// <summary>Requests deterministic View-owned focus without exposing Avalonia controls to a ViewModel.</summary>
/// <param name="Sequence">Monotonically increasing request identity.</param>
/// <param name="Target">Stable logical focus destination.</param>
/// <param name="ItemId">Optional stable item identity associated with the request.</param>
public sealed record KnowledgeGraphFocusRequest(
    long Sequence,
    KnowledgeGraphFocusTarget Target,
    string? ItemId = null);

/// <summary>Presents one bounded provider-neutral graph-node result.</summary>
/// <param name="Id">Stable graph-node identity.</param>
/// <param name="Title">Bounded user-visible title.</param>
/// <param name="KindText">Plain-language node kind.</param>
/// <param name="SecondaryText">Bounded supporting context.</param>
/// <param name="FreshnessText">Explicit freshness label.</param>
/// <param name="IntegrityText">Explicit integrity label.</param>
/// <param name="IsManual">Whether the node is controlled by a graph-native user decision.</param>
/// <param name="CanMerge">Whether the selected kind is eligible for a reviewed merge.</param>
/// <param name="CanSplit">Whether the selected kind is eligible for a reviewed split.</param>
/// <param name="SourceId">Optional stable owning-source identity.</param>
/// <param name="PrivacyStableId">Provider-neutral canonical identity used by scoped graph-only privacy actions.</param>
public sealed record KnowledgeGraphNodeRow(
    string Id,
    string Title,
    string KindText,
    string SecondaryText,
    string FreshnessText,
    string IntegrityText,
    bool IsManual,
    bool CanMerge,
    bool CanSplit,
    string? SourceId,
    string? PrivacyStableId = null)
{
    /// <summary>Gets a text-only representation that never relies on color.</summary>
    public string AccessibleText =>
        $"{Title}. {KindText}. {SecondaryText}. Freshness: {FreshnessText}. Integrity: {IntegrityText}.";
}

/// <summary>Presents one direct, bounded related-node result and its actual edge evidence.</summary>
/// <param name="EdgeId">Stable edge identity.</param>
/// <param name="NodeId">Stable related-node identity.</param>
/// <param name="Title">Bounded related-node title.</param>
/// <param name="RelationshipText">Plain-language relationship type.</param>
/// <param name="ConfidenceText">Deterministic confidence level, never a fabricated percentage.</param>
/// <param name="OriginText">Originating algorithm or Manual label.</param>
/// <param name="EvidenceSummary">Bounded explanation derived from retained evidence.</param>
/// <param name="FreshnessText">Explicit freshness label.</param>
/// <param name="IntegrityText">Explicit integrity label.</param>
/// <param name="IsManual">Whether this edge was explicitly created by the user.</param>
/// <param name="IsLegacyOwned">Whether the existing v1.9 relationship index owns this edge.</param>
/// <param name="CanUnlink">Whether a safe authoritative unlink operation exists for this edge.</param>
public sealed record KnowledgeGraphNeighborRow(
    string EdgeId,
    string NodeId,
    string Title,
    string RelationshipText,
    string ConfidenceText,
    string OriginText,
    string EvidenceSummary,
    string FreshnessText,
    string IntegrityText,
    bool IsManual,
    bool IsLegacyOwned = false,
    bool CanUnlink = true)
{
    /// <summary>Gets an accessible explanation backed by the displayed edge.</summary>
    public string AccessibleText =>
        $"{Title}. Relationship: {RelationshipText}. Confidence: {ConfidenceText}. " +
        $"Origin: {OriginText}. Authority: {(IsLegacyOwned ? "existing v1.9 relationship index" : "Knowledge Graph projection")}. " +
        $"Evidence: {EvidenceSummary}. Freshness: {FreshnessText}. Integrity: {IntegrityText}.";
}

/// <summary>Presents one bounded evidence record used by an actual graph edge.</summary>
/// <param name="Id">Stable evidence identity.</param>
/// <param name="KindText">Plain-language evidence kind.</param>
/// <param name="Explanation">Bounded retained explanation.</param>
/// <param name="OriginText">Originating algorithm and version.</param>
/// <param name="ObservedText">Display-safe observation time or state.</param>
public sealed record KnowledgeGraphEvidenceRow(
    string Id,
    string KindText,
    string Explanation,
    string OriginText,
    string ObservedText)
{
    /// <summary>Gets a screen-reader-friendly evidence description.</summary>
    public string AccessibleText =>
        $"{KindText}. {Explanation}. Origin: {OriginText}. Observed: {ObservedText}.";
}

/// <summary>Presents one bounded evidence-backed mechanical graph fact.</summary>
/// <param name="Id">Stable fact identity.</param>
/// <param name="KindText">Plain-language fact kind.</param>
/// <param name="ValueText">Validated bounded display value.</param>
/// <param name="EvidenceText">Content-free evidence-reference count.</param>
/// <param name="AlgorithmText">Originating deterministic algorithm version.</param>
public sealed record KnowledgeGraphFactRow(
    string Id,
    string KindText,
    string ValueText,
    string EvidenceText,
    string AlgorithmText)
{
    /// <summary>Gets a screen-reader-friendly fact description.</summary>
    public string AccessibleText => $"{KindText}: {ValueText}. {EvidenceText}. Algorithm: {AlgorithmText}.";
}

/// <summary>Presents one bounded timestamped fact without inferring an event.</summary>
/// <param name="Id">Stable event identity.</param>
/// <param name="WhenText">Display-safe timestamp.</param>
/// <param name="Title">Fact title.</param>
/// <param name="Detail">Bounded supporting detail.</param>
public sealed record KnowledgeGraphTimelineRow(
    string Id,
    string WhenText,
    string Title,
    string Detail)
{
    /// <summary>Gets a screen-reader-friendly timeline description.</summary>
    public string AccessibleText => $"{WhenText}. {Title}. {Detail}.";
}
