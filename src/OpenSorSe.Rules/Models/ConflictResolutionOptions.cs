namespace OpenSorSe.Rules.Models;

/// <summary>
/// Configures deterministic conflict-resolution behavior.
/// </summary>
/// <param name="Strategy">The strategy used to resolve conflicts.</param>
public sealed record ConflictResolutionOptions(ConflictResolutionStrategy Strategy)
{
    /// <summary>
    /// Gets the conservative default keep-first strategy.
    /// </summary>
    public static ConflictResolutionOptions Default { get; } = new(ConflictResolutionStrategy.KeepFirst);
}
