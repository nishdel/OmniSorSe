using System.Security.Cryptography;
using System.Text;
using OpenSorSe.Application.AI;
using OpenSorSe.Core.Platform;

#pragma warning disable CS1591

namespace OpenSorSe.Application.Workflows;

public sealed partial class ReviewedOrganizationService
{
    private const string OrganizationPreferenceProvider = "OmniSorSe";
    private const string OrganizationPreferenceModel = "deterministic-organization";

    private async Task<Dictionary<string, string>> LoadPreferencesAsync(string sourceId, CancellationToken cancellationToken)
    {
        var preferences = new Dictionary<string, string>(_paths.Comparer);
        if (_decisions is null)
        {
            return preferences;
        }

        var scope = PreferenceScope(sourceId);
        var decisions = await _decisions.LoadAsync(cancellationToken).ConfigureAwait(false);
        var eligible = decisions.Where(decision =>
            decision.OrganizationScope == scope && decision.Provider == OrganizationPreferenceProvider &&
            decision.Model == OrganizationPreferenceModel && decision.Kind == AiSuggestionDecisionKind.DestinationFolder &&
            decision.Outcome == AiSuggestionDecisionOutcome.Edited &&
            IsSafePreferenceFolder(decision.SuggestedValue) && IsSafePreferenceFolder(decision.FinalValue)).ToArray();
        foreach (var group in eligible.GroupBy(decision => NormalizeFolder(decision.SuggestedValue), _paths.Comparer))
        {
            var preferred = group.GroupBy(decision => NormalizeFolder(decision.FinalValue!), _paths.Comparer)
                .OrderByDescending(candidate => candidate.Count())
                .ThenByDescending(candidate => candidate.Max(decision => decision.RecordedAtUtc))
                .ThenBy(candidate => candidate.Key, _paths.Comparer)
                .First().Key;
            if (!_paths.Comparer.Equals(group.Key, preferred))
            {
                preferences[group.Key] = preferred;
            }
        }

        return preferences;
    }

    /// <inheritdoc />
    public async Task RememberPreferencesAsync(OrganizationProposalSet proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (_decisions is null)
        {
            throw new InvalidOperationException("Local organization preference persistence is unavailable.");
        }

        var fresh = await PreviewAsync(new OrganizationPreviewRequest(proposal.Recipe, proposal.SelectedFileIds)
        {
            Strategy = proposal.Strategy,
            Edits = proposal.Edits,
            UseLearnedPreferences = proposal.UseLearnedPreferences,
        }, cancellationToken).ConfigureAwait(false);
        if (!fresh.CanCreateChangePlan || !string.Equals(fresh.Fingerprint, proposal.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The organization preview is stale. Preview again before remembering folder preferences.");
        }

        var editedIds = proposal.Edits.Where(edit => !edit.IsRejected && edit.RelativeTargetPath is not null)
            .Select(edit => edit.FileId).ToHashSet(StringComparer.Ordinal);
        var mappings = fresh.Rows.Where(row => row.IsEligible && editedIds.Contains(row.FileId) &&
            IsSafePreferenceFolder(row.RecommendedRelativeDestination))
            .Select(row => (From: NormalizeFolder(row.RecommendedRelativeDestination!),
                To: RelativeFolder(fresh.OrganizationRoot, row.TargetPath!)))
            .Where(mapping => IsSafePreferenceFolder(mapping.To) && !_paths.Comparer.Equals(mapping.From, mapping.To))
            .DistinctBy(mapping => $"{mapping.From}\n{mapping.To}", _paths.Comparer).ToArray();
        if (mappings.Length > 64)
        {
            throw new InvalidOperationException("Remember at most 64 distinct folder substitutions at a time; reduce the selected files.");
        }

        foreach (var mapping in mappings)
        {
            await _decisions.AppendAsync(new AiSuggestionDecision(
                AiSuggestionDecisionKind.DestinationFolder, AiSuggestionDecisionOutcome.Edited, null,
                mapping.From, mapping.To, OrganizationPreferenceProvider, OrganizationPreferenceModel, DateTimeOffset.UtcNow)
            {
                OrganizationScope = PreferenceScope(fresh.SourceId),
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool IsSafePreferenceFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && folder.Length <= AiDecisionHistoryLimits.MaximumValueLength &&
        (folder == "." || !CrossPlatformPath.IsRootedOnAnyPlatform(folder) &&
            folder.Split(['/', '\\']).All(segment => segment is not "." and not ".." &&
                _paths.IsValidFileName(segment, FileNamePortabilityMode.Portable, out _)));

    private static string NormalizeFolder(string folder) => folder.Replace('\\', Path.DirectorySeparatorChar)
        .Replace('/', Path.DirectorySeparatorChar).Normalize(NormalizationForm.FormC);

    private static string PreferenceScope(string sourceId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId)));
}
