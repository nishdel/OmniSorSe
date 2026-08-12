using System.Globalization;
using System.Text;
using OpenSorSe.Application.Models;

namespace OpenSorSe.Application.Tags;

/// <summary>
/// Validates and constructs deterministic application-owned tags from explicit user input.
/// </summary>
public static class UserTagFactory
{
    /// <summary>
    /// Normalizes a bounded set of user tag values and constructs accepted associations.
    /// </summary>
    /// <param name="fileId">The opaque result-file identifier.</param>
    /// <param name="values">The raw user-entered tag values.</param>
    /// <param name="createdAtUtc">The application-owned UTC creation timestamp.</param>
    /// <param name="tags">The normalized, de-duplicated associations on success.</param>
    /// <param name="error">A user-safe validation message on failure.</param>
    /// <returns><see langword="true"/> when the complete input is valid.</returns>
    public static bool TryCreate(
        string fileId,
        IReadOnlyList<string>? values,
        DateTimeOffset createdAtUtc,
        out IReadOnlyList<TagAssociation> tags,
        out string error)
    {
        tags = Array.Empty<TagAssociation>();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(fileId))
        {
            error = "Select a result file before adding tags.";
            return false;
        }

        if (createdAtUtc.Offset != TimeSpan.Zero)
        {
            error = "The tag timestamp must be in UTC.";
            return false;
        }

        if (values is null || values.Count == 0)
        {
            error = "Enter at least one tag.";
            return false;
        }

        if (values.Count > UserTagLimits.MaximumAcceptedTagsPerFile)
        {
            error = $"Add no more than {UserTagLimits.MaximumAcceptedTagsPerFile} tags at a time.";
            return false;
        }

        var normalized = new List<NormalizedTag>(values.Count);
        foreach (var value in values)
        {
            if (!TryNormalize(value, out var tag))
            {
                error = $"Each tag must contain letters or numbers and be no longer than {UserTagLimits.MaximumTagLength} characters.";
                return false;
            }

            if (!normalized.Any(existing => string.Equals(existing.Identity, tag.Identity, StringComparison.Ordinal)))
            {
                normalized.Add(tag);
            }
        }

        tags = Array.AsReadOnly(normalized.Select(tag => new TagAssociation(
            $"tag:{fileId}:{tag.Identity}",
            fileId,
            tag.Display,
            tag.Identity,
            "User",
            TagSource.UserApproved,
            TagAcceptanceState.Accepted,
            "Created explicitly in OmniSorSe.",
            createdAtUtc)).ToArray());
        return true;
    }

    private static bool TryNormalize(string? value, out NormalizedTag tag)
    {
        tag = default!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var display = value.Trim();
        if (display.Length > UserTagLimits.MaximumTagLength || display.Any(char.IsControl))
        {
            return false;
        }

        var builder = new StringBuilder(display.Length);
        var previousWasSeparator = false;
        foreach (var character in display.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var identity = builder.ToString().Trim('-');
        if (identity.Length is 0 or > UserTagLimits.MaximumTagLength)
        {
            return false;
        }

        tag = new NormalizedTag(display, identity);
        return true;
    }

    private sealed record NormalizedTag(string Display, string Identity);
}
