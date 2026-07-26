namespace OpenSorSe.Application.AI;

/// <summary>
/// Produces small, deterministic, inspectable preference signals from local user-review decisions.
/// </summary>
public static class AiPreferenceAggregator
{
    /// <summary>
    /// Aggregates supported accepted and rejected values without attempting to train or fine-tune a model.
    /// </summary>
    /// <param name="decisions">The local decision history.</param>
    /// <returns>Bounded preference signals suitable for ranking and concise provider context.</returns>
    public static AiPreferenceSummary Build(IReadOnlyList<AiSuggestionDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        var valid = decisions.Where(IsValid).ToArray();
        return new AiPreferenceSummary(
            TopValues(valid, AiSuggestionDecisionKind.Tags, accepted: true),
            TopFolderValues(valid),
            TopValues(valid, AiSuggestionDecisionKind.Category, accepted: true),
            TopValues(valid, null, accepted: false));
    }

    private static IReadOnlyList<string> TopFolderValues(IEnumerable<AiSuggestionDecision> decisions)
    {
        var values = decisions
            .Where(decision => decision.Kind is AiSuggestionDecisionKind.DestinationFolder or AiSuggestionDecisionKind.FolderStructure)
            .Where(decision => decision.Outcome is AiSuggestionDecisionOutcome.Accepted or AiSuggestionDecisionOutcome.Edited)
            .Select(decision => decision.FinalValue ?? decision.SuggestedValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(group => group.Key)
            .ToArray();
        return Array.AsReadOnly(values);
    }

    private static IReadOnlyList<string> TopValues(
        IEnumerable<AiSuggestionDecision> decisions,
        AiSuggestionDecisionKind? kind,
        bool accepted)
    {
        var values = decisions
            .Where(decision => kind is null || decision.Kind == kind)
            .Where(decision => accepted
                ? decision.Outcome is AiSuggestionDecisionOutcome.Accepted or AiSuggestionDecisionOutcome.Edited
                : decision.Outcome == AiSuggestionDecisionOutcome.Rejected)
            .Select(decision => accepted ? decision.FinalValue ?? decision.SuggestedValue : decision.SuggestedValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(group => group.Key)
            .ToArray();
        return Array.AsReadOnly(values);
    }

    private static bool IsValid(AiSuggestionDecision decision) =>
        !string.IsNullOrWhiteSpace(decision.SuggestedValue) &&
        !string.IsNullOrWhiteSpace(decision.Provider) &&
        !string.IsNullOrWhiteSpace(decision.Model);
}
