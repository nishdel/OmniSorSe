using System.Security.Cryptography;
using System.Text;

namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Fails closed on malformed optional suggestions and never promotes them to facts.</summary>
public sealed class StrictGraphSuggestionValidator : IGraphSuggestionValidator
{
    /// <inheritdoc />
    public IReadOnlyList<GraphValidatedSuggestion> Validate(
        IReadOnlyList<GraphSuggestionCandidate> candidates,
        GraphSuggestionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        options ??= new GraphSuggestionOptions();
        if (!options.Enabled)
        {
            return [];
        }

        if (options.MaximumSuggestions is < 1 or > GraphLimits.MaximumSuggestions ||
            options.MaximumSourceKeysPerSuggestion is < 1 or > GraphLimits.MaximumSuggestionSourceKeys ||
            options.MaximumEvidenceKeysPerSuggestion is < 1 or > GraphLimits.MaximumEvidencePerEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Suggestion options exceed the documented safety ceiling.");
        }

        var accepted = new List<GraphValidatedSuggestion>(Math.Min(candidates.Count, options.MaximumSuggestions));
        foreach (var candidate in candidates.Take(options.MaximumSuggestions + 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (accepted.Count >= options.MaximumSuggestions || !TryValidate(candidate, options, out var validated))
            {
                continue;
            }

            accepted.Add(validated!);
        }

        return accepted
            .GroupBy(item => item.SuggestionId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.SuggestionId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryValidate(
        GraphSuggestionCandidate? candidate,
        GraphSuggestionOptions options,
        out GraphValidatedSuggestion? validated)
    {
        validated = null;
        if (candidate is null || !Enum.IsDefined(candidate.Kind) ||
            !TryBounded(candidate.Scope, GraphLimits.MaximumStableIdCharacters, out var scope) ||
            !TryBounded(candidate.Label, GraphLimits.MaximumLabelCharacters, out var label) ||
            !TryBounded(candidate.ProviderVersion, 64, out var providerVersion) ||
            candidate.SourceStableKeys is null || candidate.EvidenceStableKeys is null ||
            candidate.SourceStableKeys.Count is < 1 ||
            candidate.SourceStableKeys.Count > options.MaximumSourceKeysPerSuggestion ||
            candidate.EvidenceStableKeys.Count is < 1 ||
            candidate.EvidenceStableKeys.Count > options.MaximumEvidenceKeysPerSuggestion)
        {
            return false;
        }

        var sourceKeys = NormalizeKeys(candidate.SourceStableKeys);
        var evidenceKeys = NormalizeKeys(candidate.EvidenceStableKeys);
        if (sourceKeys is null || evidenceKeys is null ||
            sourceKeys.Count != candidate.SourceStableKeys.Count ||
            evidenceKeys.Count != candidate.EvidenceStableKeys.Count)
        {
            return false;
        }

        var canonical = string.Join('|',
            candidate.Kind.ToString(),
            scope,
            label,
            providerVersion,
            string.Join(',', sourceKeys),
            string.Join(',', evidenceKeys));
        var suggestionId = string.Concat(
            "kg:suggestion:",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant());
        validated = new GraphValidatedSuggestion(
            suggestionId,
            candidate.Kind,
            scope,
            label,
            sourceKeys,
            evidenceKeys,
            providerVersion,
            RequiresConfirmation: true);
        return true;
    }

    private static IReadOnlyList<string>? NormalizeKeys(IReadOnlyList<string> keys)
    {
        var result = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            if (!TryBounded(key, GraphLimits.MaximumStableIdCharacters, out var normalized))
            {
                return null;
            }

            result.Add(normalized);
        }

        return result.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool TryBounded(string? value, int maximum, out string normalized)
    {
        normalized = string.Empty;
        if (value is null || value.Length > maximum || string.IsNullOrWhiteSpace(value) ||
            ConservativeGraphIdentityResolver.ContainsInvalidUnicodeOrControl(value))
        {
            return false;
        }

        normalized = value.Normalize(NormalizationForm.FormC).Trim();
        return normalized.Length > 0;
    }
}
