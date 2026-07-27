using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;
using OpenSorSe.Application.Workflows;

namespace OpenSorSe.Application.Models;

/// <summary>Contains the outputs reached by one sequential processing pipeline run.</summary>
/// <param name="Status">The terminal pipeline status.</param>
/// <param name="Scan">The mandatory scanner result.</param>
/// <param name="Metadata">The optional metadata result.</param>
/// <param name="Hashing">The optional hashing result.</param>
/// <param name="Classification">The optional classification result.</param>
/// <param name="Duplicates">The optional duplicate result.</param>
/// <param name="Rules">The optional rule-evaluation result.</param>
/// <param name="Plan">The optional action-plan result.</param>
/// <param name="Conflicts">The optional conflict-resolution result.</param>
public sealed record ProcessingResult(ProcessingStatus Status, ScanResult Scan, FileMetadataResult? Metadata, FileHashResult? Hashing, FileClassificationResult? Classification, DuplicateDetectionResult? Duplicates, RuleEvaluationResult? Rules, ActionPlanResult? Plan, ConflictResolutionResult? Conflicts)
{
    /// <summary>Gets the immutable effective workflow snapshot used by the run.</summary>
    public WorkflowConfigurationSnapshot? Workflow { get; init; }
}
