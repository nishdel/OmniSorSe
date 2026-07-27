namespace OpenSorSe.Rules.Models;

/// <summary>
/// Identifies a deterministic metadata condition supported by the rule engine.
/// </summary>
public enum RuleConditionKind
{
    /// <summary>Matches a deterministic file category.</summary>
    FileCategoryEquals,
    /// <summary>Matches an exact-content duplicate status.</summary>
    DuplicateStatusEquals,
    /// <summary>Matches a metadata extension.</summary>
    ExtensionEquals,
    /// <summary>Matches an exact metadata filename.</summary>
    ExactFileNameEquals,
    /// <summary>Matches a minimum metadata size.</summary>
    MinimumSizeInBytes,
    /// <summary>Matches a maximum metadata size.</summary>
    MaximumSizeInBytes,
}
