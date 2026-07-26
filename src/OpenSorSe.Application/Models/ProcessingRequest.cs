using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;
using OpenSorSe.Application.Workflows;

namespace OpenSorSe.Application.Models;

/// <summary>Defines all explicit inputs required for one deterministic v0.1 processing run.</summary>
/// <param name="ScanRequest">The root directories and scanner options.</param>
/// <param name="Rules">The ordered rule set, which may be empty.</param>
public sealed record ProcessingRequest(ScanRequest ScanRequest, IReadOnlyList<FileRule> Rules)
{
    /// <summary>Gets the optional immutable effective v1.3 workflow used for this run.</summary>
    public ResolvedWorkflowConfiguration? WorkflowConfiguration { get; init; }
}
