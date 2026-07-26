using System.Globalization;
using System.Text;

namespace OpenSorSe.Application.Semantic;

/// <summary>Creates deterministic 256-dimensional local feature-hashing vectors.</summary>
public sealed class FeatureHashingEmbeddingProvider : IEmbeddingProvider
{
    /// <inheritdoc />
    public int Dimensions => 256;

    /// <inheritdoc />
    public IReadOnlyList<float> Embed(string text)
    {
        var vector = new float[Dimensions];
        foreach (var token in SemanticTokenizer.Tokenize(text, 512))
        {
            var hash = StableHash(token);
            var index = (int)(hash % (uint)Dimensions);
            var sign = (hash & 0x80000000) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        var magnitude = Math.Sqrt(vector.Sum(value => value * value));
        if (magnitude > 0)
        {
            for (var index = 0; index < vector.Length; index++)
            {
                vector[index] = (float)(vector[index] / magnitude);
            }
        }

        return Array.AsReadOnly(vector);
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var character in value.Normalize(System.Text.NormalizationForm.FormKC))
        {
            hash ^= char.ToLower(character, CultureInfo.InvariantCulture);
            hash *= prime;
        }

        return hash;
    }
}

internal static class SemanticTokenizer
{
    private static readonly string[] EnglishSuffixes = ["ing", "ed", "es", "s"];
    private static readonly string[] GermanSuffixes = ["ern", "en", "er", "es", "e", "n", "s"];

    public static IReadOnlyList<string> Tokenize(string? text, int maximumTokens)
    {
        if (string.IsNullOrWhiteSpace(text) || maximumTokens < 1)
        {
            return [];
        }

        var folded = FoldDiacritics(text);
        var tokens = new List<string>(Math.Min(maximumTokens, 64));
        var current = new StringBuilder();
        foreach (var character in folded)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (current.Length < 64)
                {
                    current.Append(char.ToLowerInvariant(character));
                }
            }
            else
            {
                Flush(current, tokens, maximumTokens);
            }

            if (tokens.Count >= maximumTokens)
            {
                break;
            }
        }

        Flush(current, tokens, maximumTokens);
        foreach (var date in ExtractIsoDates(folded))
        {
            if (tokens.Count >= maximumTokens)
            {
                break;
            }

            AddDistinct(tokens, date);
        }

        return Array.AsReadOnly(tokens.Take(maximumTokens).ToArray());
    }

    private static void Flush(StringBuilder current, ICollection<string> tokens, int maximumTokens)
    {
        if (current.Length is <= 1 or > 64 || tokens.Count >= maximumTokens)
        {
            current.Clear();
            return;
        }

        var token = current.ToString();
        current.Clear();
        AddDistinct(tokens, token);
        if (token.Length < 6 || tokens.Count >= maximumTokens)
        {
            return;
        }

        foreach (var suffix in EnglishSuffixes.Concat(GermanSuffixes).Distinct(StringComparer.Ordinal))
        {
            if (token.EndsWith(suffix, StringComparison.Ordinal) &&
                token.Length - suffix.Length >= 4)
            {
                AddDistinct(tokens, token[..^suffix.Length]);
                break;
            }
        }
    }

    private static string FoldDiacritics(string value)
    {
        var output = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                output.Append(character);
            }
        }

        return output.ToString().Normalize(NormalizationForm.FormC);
    }

    private static IEnumerable<string> ExtractIsoDates(string value)
    {
        for (var index = 0; index <= value.Length - 10; index++)
        {
            var candidate = value.AsSpan(index, 10);
            if (candidate[4] == '-' && candidate[7] == '-' &&
                DateOnly.TryParseExact(
                    candidate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                yield return candidate.ToString();
                index += 9;
            }
        }
    }

    private static void AddDistinct(ICollection<string> values, string value)
    {
        if (!values.Contains(value))
        {
            values.Add(value);
        }
    }
}
