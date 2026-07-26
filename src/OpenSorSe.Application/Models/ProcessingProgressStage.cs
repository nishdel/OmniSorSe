namespace OpenSorSe.Application.Models;

/// <summary>Identifies the current sequential v0.1 pipeline stage.</summary>
public enum ProcessingProgressStage
{
    /// <summary>File discovery is running.</summary>
    Scanning,

    /// <summary>Metadata enrichment is running.</summary>
    ReadingMetadata,

    /// <summary>Bounded local metadata, native-text, and optional OCR extraction is running.</summary>
    ExtractingContent,

    /// <summary>SHA-256 hashing is running.</summary>
    Hashing,

    /// <summary>Metadata classification is running.</summary>
    Classifying,

    /// <summary>Exact-hash duplicate detection is running.</summary>
    DetectingDuplicates,

    /// <summary>Rule evaluation is running.</summary>
    EvaluatingRules,

    /// <summary>Action planning is running.</summary>
    PlanningActions,

    /// <summary>Intra-plan conflict resolution is running.</summary>
    ResolvingConflicts,

    /// <summary>The pipeline completed.</summary>
    Completed,

    /// <summary>The pipeline was cancelled.</summary>
    Cancelled,
}
