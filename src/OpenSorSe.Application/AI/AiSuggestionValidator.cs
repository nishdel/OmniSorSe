using System.Globalization;
using System.Text;
using OpenSorSe.Application.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.AI;

/// <summary>
/// Centralizes normalization and safety validation for all untrusted provider suggestion values.
/// </summary>
public static class AiSuggestionValidator
{
    /// <summary>Maximum model-supplied filename-stem length.</summary>
    public const int MaximumFileStemLength = 120;

    /// <summary>Maximum model-supplied folder-component length.</summary>
    public const int MaximumFolderComponentLength = 64;

    private static readonly char[] PortableInvalidNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };
    private static readonly HashSet<string> ReservedSystemFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "Program Files", "Program Files (x86)", "ProgramData", "System Volume Information",
        "System32", "Windows", "dev", "etc", "proc", "root", "sys", "usr", "var",
    };

    /// <summary>
    /// Validates and normalizes a proposed file name while preserving the existing file extension.
    /// </summary>
    /// <param name="proposedFileName">The untrusted model output.</param>
    /// <param name="currentExtension">The extension that must be preserved.</param>
    /// <param name="siblingFileNames">Known sibling names used for non-mutating conflict detection.</param>
    /// <param name="normalizedFileName">The safe proposed name when validation succeeds.</param>
    /// <param name="error">A user-safe validation explanation when validation fails.</param>
    /// <returns><see langword="true"/> when the name is safe to present for review.</returns>
    public static bool TryNormalizeFileName(
        string? proposedFileName,
        string currentExtension,
        IReadOnlyList<string> siblingFileNames,
        out string normalizedFileName,
        out string error)
    {
        normalizedFileName = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(proposedFileName))
        {
            error = "A suggested file name is required.";
            return false;
        }

        var candidate = proposedFileName.Trim();
        if (candidate.Length > 255 || candidate is "." or ".." ||
            Path.IsPathRooted(candidate) || candidate.Contains("..", StringComparison.Ordinal) ||
            candidate.IndexOfAny(PortableInvalidNameCharacters) >= 0 || candidate.Any(char.IsControl) ||
            candidate.EndsWith(' ') || candidate.EndsWith('.'))
        {
            error = "The suggested file name is not a safe file name.";
            return false;
        }

        var extension = Path.GetExtension(candidate);
        if (!string.Equals(extension, currentExtension, StringComparison.Ordinal))
        {
            error = "The suggested file name must preserve the original extension.";
            return false;
        }

        var baseName = Path.GetFileNameWithoutExtension(candidate);
        if (string.IsNullOrWhiteSpace(baseName) || ReservedFileNames.Contains(baseName))
        {
            error = "The suggested file name uses a reserved file name.";
            return false;
        }

        if (siblingFileNames.Any(name => string.Equals(name?.Trim(), candidate, StringComparison.OrdinalIgnoreCase)))
        {
            error = "The suggested file name conflicts with a known file in the same folder.";
            return false;
        }

        normalizedFileName = candidate;
        return true;
    }

    /// <summary>Validates one model-supplied stem and appends the application-owned extension.</summary>
    public static bool TryNormalizeFileStem(
        string? proposedStem,
        string currentExtension,
        IReadOnlyList<string> siblingFileNames,
        out string normalizedFileName,
        out string error)
    {
        normalizedFileName = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(proposedStem))
        {
            error = "Expected `suggestedStem` to be a non-empty string. No suggestion was used.";
            return false;
        }

        var stem = proposedStem.Trim();
        if (stem.Length > MaximumFileStemLength ||
            stem is "." or ".." ||
            stem.StartsWith('-') ||
            stem.EndsWith('-') ||
            stem.Contains("--", StringComparison.Ordinal) ||
            stem.EndsWith(currentExtension, StringComparison.OrdinalIgnoreCase) ||
            stem.Any(character => !char.IsLetterOrDigit(character) && character != '-'))
        {
            error = "The AI rename response did not provide one safe extension-free hyphen-separated stem. No suggestion was used.";
            return false;
        }

        if (ReservedFileNames.Contains(stem))
        {
            error = "The AI rename response used a reserved filename stem. No suggestion was used.";
            return false;
        }

        var candidate = stem + currentExtension;
        if (candidate.Length > 255 ||
            siblingFileNames.Any(name =>
                string.Equals(name?.Trim(), candidate, StringComparison.OrdinalIgnoreCase)))
        {
            error = candidate.Length > 255
                ? "The AI rename response exceeded the supported final filename length. No suggestion was used."
                : "The AI rename response conflicts with a known file in the same folder. No suggestion was used.";
            return false;
        }

        normalizedFileName = candidate;
        return true;
    }

    /// <summary>Checks that every suggested stem token is grounded in supplied deterministic evidence.</summary>
    public static bool IsFileStemGrounded(
        string proposedStem,
        string currentFileName,
        string classificationDisplay)
    {
        var evidence = Tokens(Path.GetFileNameWithoutExtension(currentFileName))
            .Concat(Tokens(classificationDisplay))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Tokens(proposedStem).All(evidence.Contains);
    }

    /// <summary>Checks that every model-produced interpretation value is grounded in supplied filename or text evidence.</summary>
    public static bool AreDocumentInterpretationValuesGrounded(
        AiDocumentTextRequest request,
        string? documentType,
        string? title,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> dates,
        string? issuer,
        string? suggestedFolder)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(dates);
        var evidence = Tokens(Path.GetFileNameWithoutExtension(request.DisplayFileName ?? string.Empty))
            .Concat(Tokens(request.NativeText ?? string.Empty))
            .Concat(Tokens(request.OcrText ?? string.Empty))
            .Concat((request.Pages ?? [])
                .Where(page => page is not null && !string.IsNullOrWhiteSpace(page.Text))
                .SelectMany(page => Tokens(page.Text!)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new[] { documentType, title, issuer, suggestedFolder }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Concat(tags)
            .Concat(dates);
        foreach (var candidate in candidates)
        {
            var tokens = Tokens(candidate).ToArray();
            if (tokens.Length == 0 || tokens.Any(token => !evidence.Contains(token)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates one logical folder-name component independently from host operating-system rules.
    /// </summary>
    /// <param name="value">The untrusted folder name.</param>
    /// <param name="normalizedFolderName">The trimmed safe component.</param>
    /// <param name="error">A user-safe validation explanation.</param>
    /// <returns><see langword="true"/> when the value is one portable safe component.</returns>
    public static bool TryNormalizeFolderName(string? value, out string normalizedFolderName, out string error)
    {
        normalizedFolderName = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "A suggested folder name is required.";
            return false;
        }

        var candidate = value.Trim();
        if (candidate.Length > MaximumFolderComponentLength || candidate is "." or ".." || Path.IsPathRooted(candidate) ||
            candidate.Contains("..", StringComparison.Ordinal) || candidate.IndexOfAny(PortableInvalidNameCharacters) >= 0 ||
            candidate.Any(char.IsControl) || candidate.EndsWith(' ') || candidate.EndsWith('.'))
        {
            error = "The suggested folder name is not a safe portable folder name.";
            return false;
        }

        if (ReservedFileNames.Contains(candidate) || ReservedSystemFolderNames.Contains(candidate))
        {
            error = "The suggested folder name uses a reserved or system-directory name.";
            return false;
        }

        normalizedFolderName = candidate;
        return true;
    }

    /// <summary>
    /// Normalizes and de-duplicates tag values without trusting provider casing, punctuation, or whitespace.
    /// </summary>
    /// <param name="values">The untrusted tag values.</param>
    /// <param name="tags">Safe application-owned tag values.</param>
    /// <param name="error">A user-safe validation explanation when validation fails.</param>
    /// <returns><see langword="true"/> when all tag values are valid.</returns>
    public static bool TryNormalizeTags(IReadOnlyList<string>? values, out IReadOnlyList<SuggestedTag> tags, out string error)
    {
        tags = Array.Empty<SuggestedTag>();
        error = string.Empty;
        if (values is null || values.Count > 12)
        {
            error = "The suggested tags are invalid.";
            return false;
        }

        var normalized = new List<SuggestedTag>();
        foreach (var value in values)
        {
            if (!TryNormalizeTag(value, out var tag))
            {
                error = "A suggested tag is invalid.";
                return false;
            }

            if (!normalized.Any(existing => string.Equals(existing.NormalizedValue, tag.NormalizedValue, StringComparison.Ordinal)))
            {
                normalized.Add(tag);
            }
        }

        tags = Array.AsReadOnly(normalized.ToArray());
        return true;
    }

    /// <summary>
    /// Validates a category name against the application's existing deterministic category model.
    /// </summary>
    /// <param name="value">The optional category supplied by the provider.</param>
    /// <param name="category">The parsed category when supplied.</param>
    /// <param name="error">A user-safe validation explanation when validation fails.</param>
    /// <returns><see langword="true"/> when the category is absent or supported.</returns>
    public static bool TryParseCategory(string? value, out FileCategory? category, out string error)
    {
        category = null;
        error = string.Empty;
        if (value is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse<FileCategory>(value.Trim(), true, out var parsed) || !Enum.IsDefined(parsed))
        {
            error = "The suggested category is unsupported.";
            return false;
        }

        category = parsed;
        return true;
    }

    /// <summary>
    /// Validates a relative destination folder and removes duplicate path segments.
    /// </summary>
    /// <param name="value">The optional untrusted provider output.</param>
    /// <param name="normalizedFolder">The safe, relative destination path when supplied.</param>
    /// <param name="error">A user-safe validation explanation when validation fails.</param>
    /// <returns><see langword="true"/> when the destination is absent or safe.</returns>
    public static bool TryNormalizeDestinationFolder(string? value, out string? normalizedFolder, out string error)
    {
        normalizedFolder = null;
        error = string.Empty;
        if (value is null)
        {
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 512 || Path.IsPathRooted(trimmed) || trimmed.Contains(':'))
        {
            error = "The suggested destination must be a relative folder path.";
            return false;
        }

        var segments = trimmed.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Length > 12)
        {
            error = "The suggested destination is invalid.";
            return false;
        }

        var safeSegments = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (!TryNormalizeFolderName(segment, out var safeSegment, out _))
            {
                error = "The suggested destination contains an unsafe folder name.";
                return false;
            }

            safeSegments.Add(safeSegment);
        }

        normalizedFolder = string.Join('/', safeSegments);
        return true;
    }

    /// <summary>
    /// Creates deterministic tag associations for the accepted tags in the current in-memory result session.
    /// </summary>
    /// <param name="fileId">The opaque result-file identifier.</param>
    /// <param name="tags">The already normalized accepted tags.</param>
    /// <param name="explanation">The optional provider explanation.</param>
    /// <param name="createdAtUtc">The application-owned timestamp.</param>
    /// <returns>De-duplicated, accepted tag associations.</returns>
    public static IReadOnlyList<TagAssociation> CreateAcceptedTagAssociations(
        string fileId,
        IReadOnlyList<SuggestedTag> tags,
        string? explanation,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(tags);
        return Array.AsReadOnly(tags
            .Select(tag => new TagAssociation(
                $"tag:{fileId}:{tag.NormalizedValue}",
                fileId,
                tag.DisplayName,
                tag.NormalizedValue,
                "Suggested",
                TagSource.UserApproved,
                TagAcceptanceState.Accepted,
                explanation,
                createdAtUtc))
            .ToArray());
    }

    private static bool TryNormalizeTag(string? value, out SuggestedTag tag)
    {
        tag = default!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var display = value.Trim();
        if (display.Length > 64 || display.Any(char.IsControl))
        {
            return false;
        }

        var builder = new StringBuilder(display.Length);
        var previousWasSeparator = false;
        foreach (var character in display.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

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

        var normalized = builder.ToString().Trim('-');
        if (normalized.Length is 0 or > 64)
        {
            return false;
        }

        tag = new SuggestedTag(display, normalized);
        return true;
    }

    private static IEnumerable<string> Tokens(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            if (builder.Length > 0)
            {
                yield return NormalizeNumericToken(builder.ToString());
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return NormalizeNumericToken(builder.ToString());
        }
    }

    private static string NormalizeNumericToken(string value) =>
        value.All(char.IsDigit) &&
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : value;
}
