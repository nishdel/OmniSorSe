using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Configuration;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Provides plain-language presentation for an initial indexing scheduling policy.</summary>
public sealed record InitialScanDepthOption(
    InitialScanDepth Value,
    string Label,
    string Description)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>Owns the bounded user-facing initial indexing choices.</summary>
public static class InitialScanDepthOptions
{
    /// <summary>Gets all choices in recommended display order.</summary>
    public static IReadOnlyList<InitialScanDepthOption> All { get; } =
    [
        new(
            InitialScanDepth.BaseFirst,
            "Fast — searchable first",
            "Makes names, paths, metadata, and inexpensive document text searchable across the selection first. Enabled OCR, media, summaries, and related analysis continue durably afterward."),
        new(
            InitialScanDepth.DeepInitialAnalysis,
            "Deep initial analysis",
            "Completes enabled deeper analysis earlier for each file. Initial Search coverage across a large folder may take longer."),
    ];

    /// <summary>Returns the stable presentation option for a persisted value.</summary>
    public static InitialScanDepthOption For(InitialScanDepth value) =>
        All.First(option => option.Value == value);
}

/// <summary>
/// Represents an immutable user request to scan normalized folder roots.
/// </summary>
/// <param name="FolderPaths">The validated folder roots in user selection order.</param>
public sealed record ScanRequest(IReadOnlyList<string> FolderPaths)
{
    /// <summary>Gets the persistent workflow profile selected for this scan.</summary>
    public string ProfileId { get; init; } = BuiltInWorkflowIds.GeneralDocuments;

    /// <summary>Gets optional one-time constraints that do not mutate the saved profile.</summary>
    public WorkflowProfileOverride? OneTimeOverride { get; init; }

    /// <summary>Gets the user-selected scheduling policy for this initial scan.</summary>
    public InitialScanDepth InitialScanDepth { get; init; } = InitialScanDepth.BaseFirst;
}
