using System.Security.Cryptography;
using System.Text;

namespace OpenSorSe.Application.KnowledgeGraph;

/// <summary>Builds stable mechanical identities and refuses speculative real-world merges.</summary>
public sealed class ConservativeGraphIdentityResolver : IGraphIdentityResolver
{
    private const string ResolverName = "stable-mechanical-identity";
    private const string ResolverVersion = "1.0.0";
    private static readonly HashSet<string> SuggestionOnlyKinds = new(StringComparer.Ordinal)
    {
        "project", "organization", "purchase", "trip", "person", "place", "event", "topic",
    };

    /// <inheritdoc />
    public GraphIdentityResolution Resolve(GraphIdentityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.Kind.IsStable)
        {
            return SuggestionOnlyKinds.Contains(input.Kind.Value ?? string.Empty)
                ? Result(GraphIdentityResolutionStatus.SuggestionRequired, null, string.Empty, "confirmation-required")
                : Result(GraphIdentityResolutionStatus.Rejected, null, string.Empty, "unsupported-node-kind");
        }

        if (!TryNormalizeBounded(input.Scope, allowEmpty: false, out var scope) ||
            !TryNormalizeBounded(input.NormalizationVersion, allowEmpty: false, out var normalizationVersion))
        {
            return Result(GraphIdentityResolutionStatus.RepairRequired, null, string.Empty, "invalid-identity-scope");
        }

        string canonicalKey;
        string? existingId = null;
        switch (input.Kind.Value)
        {
            case "file":
            case "source":
            case "collection":
            case "manual-entity":
                if (!TryNormalizeBounded(input.ExistingStableId, allowEmpty: false, out existingId))
                {
                    return Result(GraphIdentityResolutionStatus.RepairRequired, null, string.Empty, "missing-stable-id");
                }

                if (!string.Equals(existingId, input.ExistingStableId, StringComparison.Ordinal))
                {
                    return Result(GraphIdentityResolutionStatus.RepairRequired, null, string.Empty, "noncanonical-stable-id");
                }

                canonicalKey = existingId;
                break;
            case "folder":
                if (!TryNormalizeRelativePath(input.CanonicalKey, input.PathComparison, out canonicalKey))
                {
                    return Result(GraphIdentityResolutionStatus.RepairRequired, null, string.Empty, "invalid-relative-folder");
                }

                break;
            case "document-set":
                if (!TryNormalizeHash(input.CanonicalKey, out canonicalKey) ||
                    !TryNormalizeBounded(input.HashAlgorithmVersion, allowEmpty: false, out _))
                {
                    return Result(GraphIdentityResolutionStatus.RepairRequired, null, string.Empty, "invalid-content-hash");
                }

                break;
            default:
                return Result(GraphIdentityResolutionStatus.Rejected, null, string.Empty, "unsupported-node-kind");
        }

        var canonicalInputs = BuildCanonicalInputs(
            input.Kind.Value,
            scope,
            normalizationVersion,
            canonicalKey,
            input.Kind == GraphNodeKind.DocumentSet ? input.HashAlgorithmVersion! : string.Empty,
            input.Kind == GraphNodeKind.Folder ? input.PathComparison.ToString() : string.Empty);
        if (input.Kind == GraphNodeKind.ManualEntity && !canonicalKey.StartsWith("manual:", StringComparison.Ordinal))
        {
            return Result(GraphIdentityResolutionStatus.RepairRequired, null, canonicalInputs, "invalid-manual-id");
        }

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalInputs))).ToLowerInvariant();
        var nodeId = input.Kind == GraphNodeKind.ManualEntity
            ? canonicalKey
            : string.Concat("kg:", input.Kind.Value, ":", digest);
        return Result(GraphIdentityResolutionStatus.Resolved, nodeId, canonicalInputs, "resolved");
    }

    private static GraphIdentityResolution Result(
        GraphIdentityResolutionStatus status,
        string? nodeId,
        string canonicalInputs,
        string reasonCode) => new(status, nodeId, canonicalInputs, ResolverName, ResolverVersion, reasonCode);

    private static string BuildCanonicalInputs(params string[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            builder.Append(value.Length).Append(':').Append(value).Append('|');
        }

        return builder.ToString();
    }

    private static bool TryNormalizeHash(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!TryNormalizeBounded(value, allowEmpty: false, out var candidate) || candidate.Length is < 16 or > 256)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        normalized = candidate.ToLowerInvariant();
        return true;
    }

    private static bool TryNormalizeRelativePath(
        string? value,
        GraphPathComparison comparison,
        out string normalized)
    {
        normalized = string.Empty;
        if (!TryNormalizeBounded(value, allowEmpty: true, out var candidate))
        {
            return false;
        }

        var rooted = candidate.StartsWith("/", StringComparison.Ordinal) ||
            candidate.StartsWith("\\", StringComparison.Ordinal) ||
            (candidate.Length >= 2 && candidate[1] == ':');
        candidate = candidate.Replace('\\', '/').Trim('/');
        if (rooted ||
            candidate.Contains("//", StringComparison.Ordinal) ||
            candidate.Split('/').Any(part => part is "." or ".."))
        {
            return false;
        }

        normalized = comparison == GraphPathComparison.CaseInsensitive
            ? candidate.ToUpperInvariant()
            : candidate;
        return true;
    }

    internal static bool TryNormalizeBounded(string? value, bool allowEmpty, out string normalized)
    {
        normalized = string.Empty;
        if (value is null || value.Length > GraphLimits.MaximumStableIdCharacters || ContainsInvalidUnicodeOrControl(value))
        {
            return false;
        }

        normalized = value.Normalize(NormalizationForm.FormC).Trim();
        return allowEmpty || normalized.Length > 0;
    }

    internal static bool ContainsInvalidUnicodeOrControl(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\0' || char.IsControl(character))
            {
                return true;
            }

            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return true;
                }

                index++;
            }
            else if (char.IsLowSurrogate(character))
            {
                return true;
            }
        }

        return false;
    }
}
