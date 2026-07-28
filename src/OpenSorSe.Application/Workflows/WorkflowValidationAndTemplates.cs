#pragma warning disable CS1591

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpenSorSe.Core.Platform;
using static OpenSorSe.Application.Workflows.WorkflowValidationResults;

namespace OpenSorSe.Application.Workflows;

/// <summary>
/// Parses and evaluates the constrained field/date template language used by Sorting Recipes.
/// </summary>
/// <remarks>
/// Recipe text is data, never executable code. Evaluation performs one bounded
/// substitution pass followed by portable filename sanitization, reserved-name
/// checks, path normalization, root confinement, and collision policy. It does
/// not create directories or move files.
/// </remarks>
public sealed partial class WorkflowTemplateEngine : IWorkflowTemplateEngine
{
    private readonly IPathSemantics _pathSemantics;
    private static readonly IReadOnlySet<string> SupportedFields = new HashSet<string>(
    [
        "originalName",
        "extension",
        "date",
        "createdDate",
        "modifiedDate",
        "vendor",
        "documentType",
        "title",
        "author",
        "invoiceNumber",
        "amount",
        "currency",
        "project",
        "category",
        "captureYear",
        "captureMonth",
        "ruleGenerated",
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> DateFields = new HashSet<string>(
        ["date", "createdDate", "modifiedDate"],
        StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        @"^plugin\.[a-z0-9](?:[a-z0-9.-]{0,126}[a-z0-9])?\.[a-z0-9](?:[a-z0-9.-]{0,126}[a-z0-9])?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PluginFieldRegex();

    public WorkflowTemplateEngine()
        : this(PlatformServices.CurrentPathSemantics)
    {
    }

    public WorkflowTemplateEngine(IPathSemantics pathSemantics)
    {
        _pathSemantics = pathSemantics ?? throw new ArgumentNullException(nameof(pathSemantics));
    }

    public WorkflowValidationResult ValidateRecipeTemplates(SortingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var issues = new List<WorkflowValidationIssue>();
        ValidateTemplate(recipe.NamingTemplate, destination: false, issues);
        ValidateTemplate(recipe.DestinationTemplate, destination: true, issues);
        if (!IsValidDateFormat(recipe.DefaultDateFormat))
        {
            issues.Add(new WorkflowValidationIssue(
                "template.default-format",
                "The default date format is invalid.",
                true));
        }

        if (recipe.RequiredFields is not null &&
            recipe.OptionalFields is not null &&
            recipe.FallbackValues is not null)
        {
            var declaredFields = recipe.RequiredFields
                .Concat(recipe.OptionalFields)
                .Concat(recipe.FallbackValues.Keys)
                .ToArray();
            if (declaredFields.Any(field => !IsSupportedField(field)))
            {
                issues.Add(new WorkflowValidationIssue(
                    "template.declared-field",
                    "Required, optional, and fallback fields must use the supported field whitelist.",
                    true));
            }

        }

        return Result(issues);
    }

    public RecipeEvaluationResult Evaluate(SortingRecipe recipe, RecipeEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(context);
        var validation = ValidateRecipeTemplates(recipe);
        if (!validation.IsValid)
        {
            return Invalid(
                context.OriginalPath,
                validation.Issues.Where(issue => issue.IsBlocking).Select(issue => issue.Message));
        }

        if (string.IsNullOrWhiteSpace(context.OrganizationRoot) ||
            !Path.IsPathFullyQualified(context.OrganizationRoot) ||
            string.IsNullOrWhiteSpace(context.OriginalPath) ||
            !Path.IsPathFullyQualified(context.OriginalPath))
        {
            return Invalid(context.OriginalPath, ["The organization root and original path must be absolute."]);
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(context.OrganizationRoot));
        var values = new Dictionary<string, RecipeFieldValue>(context.Values, StringComparer.OrdinalIgnoreCase);
        values.TryAdd(
            "originalName",
            new RecipeFieldValue(Path.GetFileNameWithoutExtension(context.OriginalPath), "filesystem"));
        values.TryAdd(
            "extension",
            new RecipeFieldValue(Path.GetExtension(context.OriginalPath).TrimStart('.'), "filesystem"));

        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requiredField in recipe.RequiredFields)
        {
            if (!values.TryGetValue(requiredField, out var requiredValue) ||
                string.IsNullOrWhiteSpace(requiredValue.Value))
            {
                missing.Add(requiredField);
            }
        }

        var fallback = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sanitization = new List<string>();
        var aiRequired = false;
        var name = ResolveTemplate(
            recipe.NamingTemplate,
            recipe,
            values,
            missing,
            fallback,
            used,
            sanitization,
            ref aiRequired);
        var relativeDestination = ResolveTemplate(
            recipe.DestinationTemplate,
            recipe,
            values,
            missing,
            fallback,
            used,
            sanitization,
            ref aiRequired);
        var unresolvedRequired = recipe.RequiredFields
            .Where(field => missing.Contains(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unresolvedRequired.Length > 0)
        {
            return new RecipeEvaluationResult(
                false,
                context.OriginalPath,
                null,
                null,
                ToReadOnly(used),
                Array.AsReadOnly(unresolvedRequired),
                Array.AsReadOnly(fallback.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()),
                Array.AsReadOnly(sanitization.Distinct(StringComparer.Ordinal).ToArray()),
                ["Required recipe values are unresolved."],
                [],
                aiRequired);
        }

        var conflicts = new List<string>();
        var warnings = new List<string>();
        var sanitizedName = SanitizeSegment(
            name,
            recipe.Normalization,
            recipe.FileNamePortability,
            sanitization,
            "filename");
        if (recipe.PreserveExtension)
        {
            var extension = Path.GetExtension(context.OriginalPath);
            if (!string.IsNullOrWhiteSpace(extension) &&
                !sanitizedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                sanitizedName += extension;
            }
        }

        if (!IsSafeSegment(sanitizedName, recipe.FileNamePortability))
        {
            conflicts.Add("The proposed filename is empty, reserved, or otherwise unsafe.");
        }

        if (sanitizedName.Length > recipe.MaximumFileNameLength)
        {
            conflicts.Add($"The proposed filename exceeds the configured {recipe.MaximumFileNameLength}-character limit.");
        }

        string? destinationDirectory = null;
        string? destinationPath = null;
        try
        {
            if (CrossPlatformPath.IsRootedOnAnyPlatform(relativeDestination))
            {
                conflicts.Add("The destination template produced a rooted path.");
            }
            else
            {
                var rawSegments = relativeDestination.Split(
                    ['/', '\\'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (rawSegments.Length == 0 ||
                    rawSegments.Any(segment => segment is "." or ".."))
                {
                    conflicts.Add("The destination template produced an empty or traversing path.");
                }
                else
                {
                    var safeSegments = rawSegments
                        .Select(segment => SanitizeSegment(
                            segment,
                            recipe.Normalization,
                            recipe.FileNamePortability,
                            sanitization,
                            "destination segment"))
                        .ToArray();
                    if (safeSegments.Any(segment =>
                            !IsSafeSegment(segment, recipe.FileNamePortability)))
                    {
                        conflicts.Add("The destination template produced an unsafe or reserved path segment.");
                    }
                    else
                    {
                        destinationDirectory = Path.GetFullPath(Path.Combine([root, .. safeSegments]));
                        destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, sanitizedName));
                        if (!_pathSemantics.IsWithinRoot(root, destinationDirectory) ||
                            !_pathSemantics.IsWithinRoot(root, destinationPath))
                        {
                            conflicts.Add("The proposed destination escapes the approved organization root.");
                        }
                        else if (destinationPath.Length > WorkflowLibraryLimits.MaximumPathLength)
                        {
                            conflicts.Add("The proposed destination exceeds the supported path length.");
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            conflicts.Add("The destination template produced an invalid path.");
        }

        if (destinationPath is not null &&
            context.OccupiedDestinationPaths?.Contains(destinationPath) == true)
        {
            if (recipe.CollisionPolicy == WorkflowCollisionPolicy.Block)
            {
                conflicts.Add("The proposed destination is already occupied; overwrite is never allowed.");
            }
            else
            {
                warnings.Add(
                    "The proposed destination is occupied and requires review; execution preflight will still block overwrite.");
            }
        }

        if (aiRequired)
        {
            warnings.Add("One or more values came from explicitly approved AI-derived metadata.");
        }

        if (missing.Count > unresolvedRequired.Length)
        {
            warnings.Add("Optional values were missing and were omitted or replaced with configured fallbacks.");
        }

        return new RecipeEvaluationResult(
            conflicts.Count == 0,
            context.OriginalPath,
            conflicts.Count == 0 ? sanitizedName : null,
            conflicts.Count == 0 ? destinationPath : null,
            ToReadOnly(used),
            Array.AsReadOnly(missing.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()),
            Array.AsReadOnly(fallback.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()),
            Array.AsReadOnly(sanitization.Distinct(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(conflicts.Distinct(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).ToArray()),
            aiRequired);
    }

    private static void ValidateTemplate(
        string template,
        bool destination,
        List<WorkflowValidationIssue> issues)
    {
        var label = destination ? "Destination" : "Filename";
        if (string.IsNullOrWhiteSpace(template) ||
            template.Length > WorkflowLibraryLimits.MaximumTemplateLength ||
            template.Any(char.IsControl))
        {
            issues.Add(new WorkflowValidationIssue(
                "template.invalid",
                $"{label} template is empty or exceeds supported bounds.",
                true));
            return;
        }

        if (destination && CrossPlatformPath.IsRootedOnAnyPlatform(template))
        {
            issues.Add(new WorkflowValidationIssue(
                "template.rooted",
                "Destination templates must be relative to the approved organization root.",
                true));
        }

        if (!destination && (template.Contains('/') || template.Contains('\\')))
        {
            issues.Add(new WorkflowValidationIssue(
                "template.filename-separator",
                "Filename templates cannot contain directory separators.",
                true));
        }

        if (destination && template
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(segment => segment is "." or ".."))
        {
            issues.Add(new WorkflowValidationIssue(
                "template.traversal",
                "Destination templates cannot contain traversal segments.",
                true));
        }

        if (!TryParse(template, out var tokens))
        {
            issues.Add(new WorkflowValidationIssue(
                "template.syntax",
                $"{label} template contains malformed braces or unsupported syntax.",
                true));
            return;
        }

        foreach (var token in tokens.Where(token => token.Field is not null))
        {
            if (!IsSupportedField(token.Field!))
            {
                issues.Add(new WorkflowValidationIssue(
                    "template.field",
                    $"Template field \"{token.Field}\" is not supported.",
                    true));
            }

            if (token.Format is not null &&
                (!DateFields.Contains(token.Field!) ||
                 !IsValidDateFormat(token.Format)))
            {
                issues.Add(new WorkflowValidationIssue(
                    "template.format",
                    $"Template format for \"{token.Field}\" is invalid.",
                    true));
            }
        }
    }

    private string ResolveTemplate(
        string template,
        SortingRecipe recipe,
        IReadOnlyDictionary<string, RecipeFieldValue> suppliedValues,
        HashSet<string> missing,
        HashSet<string> fallbacks,
        Dictionary<string, string> used,
        List<string> sanitization,
        ref bool aiRequired)
    {
        _ = TryParse(template, out var tokens);
        var builder = new StringBuilder(template.Length);
        foreach (var token in tokens)
        {
            if (token.Field is null)
            {
                builder.Append(token.Literal);
                continue;
            }

            if (!suppliedValues.TryGetValue(token.Field, out var fieldValue) ||
                string.IsNullOrWhiteSpace(fieldValue.Value))
            {
                missing.Add(token.Field);
                if (recipe.Normalization.MissingValuePolicy == WorkflowMissingValuePolicy.UseFallback &&
                    recipe.FallbackValues.TryGetValue(token.Field, out var configuredFallback))
                {
                    fieldValue = new RecipeFieldValue(configuredFallback, "recipe fallback");
                    fallbacks.Add(token.Field);
                }
                else if (recipe.OptionalFields.Contains(token.Field, StringComparer.OrdinalIgnoreCase))
                {
                    if (recipe.Normalization.MissingValuePolicy == WorkflowMissingValuePolicy.UseFallback)
                    {
                        fieldValue = new RecipeFieldValue(
                            recipe.Normalization.EmptyValueReplacement,
                            "recipe empty-value replacement");
                        fallbacks.Add(token.Field);
                    }
                    else
                    {
                        fieldValue = new RecipeFieldValue(string.Empty, "optional field omitted");
                    }
                }
                else
                {
                    fieldValue = new RecipeFieldValue(string.Empty, "missing");
                }
            }

            var originalValue = fieldValue.Value;
            var value = FormatValue(token.Field, originalValue, token.Format ?? recipe.DefaultDateFormat);
            value = NormalizeValue(value, recipe.Normalization);
            value = ReplaceUnsafeTokenCharacters(
                value,
                recipe.Normalization.InvalidCharacterPolicy,
                recipe.FileNamePortability);
            if (!string.Equals(originalValue, value, StringComparison.Ordinal))
            {
                sanitization.Add($"The value for \"{token.Field}\" was formatted, normalized, or sanitized.");
            }

            used[token.Field] = value;
            aiRequired |= fieldValue.IsAiDerived;
            builder.Append(value);
        }

        return builder.ToString();
    }

    private static string FormatValue(string field, string value, string format)
    {
        if (!DateFields.Contains(field) || string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var date)
            ? date.ToString(format, CultureInfo.InvariantCulture)
            : value;
    }

    private static string NormalizeValue(string value, RecipeNormalizationOptions options)
    {
        var normalized = options.NormalizeUnicode
            ? value.Normalize(NormalizationForm.FormKC)
            : value;
        if (options.CollapseWhitespace)
        {
            normalized = WhitespaceRegex().Replace(normalized, " ").Trim();
        }

        return options.CasePolicy switch
        {
            WorkflowCasePolicy.Lower => normalized.ToLowerInvariant(),
            WorkflowCasePolicy.Upper => normalized.ToUpperInvariant(),
            WorkflowCasePolicy.Title => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                normalized.ToLowerInvariant()),
            _ => normalized,
        };
    }

    private string ReplaceUnsafeTokenCharacters(
        string value,
        WorkflowInvalidCharacterPolicy policy,
        FileNamePortabilityMode portabilityMode)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var invalid = !_pathSemantics.IsValidFileName(
                $"a{character}b",
                portabilityMode,
                out _);
            if (!invalid)
            {
                builder.Append(character);
            }
            else if (policy == WorkflowInvalidCharacterPolicy.ReplaceWithUnderscore)
            {
                builder.Append('_');
            }
        }

        return builder.ToString();
    }

    private string SanitizeSegment(
        string value,
        RecipeNormalizationOptions options,
        FileNamePortabilityMode portabilityMode,
        List<string> changes,
        string label)
    {
        var normalized = NormalizeValue(value, options);
        var sanitized = ReplaceUnsafeTokenCharacters(
                normalized,
                options.InvalidCharacterPolicy,
                portabilityMode)
            .Trim();
        if (portabilityMode is FileNamePortabilityMode.Portable or
                FileNamePortabilityMode.WindowsCompatible ||
            _pathSemantics.Platform == HostPlatformKind.Windows)
        {
            sanitized = sanitized.TrimEnd('.', ' ');
        }
        if (!string.Equals(value, sanitized, StringComparison.Ordinal))
        {
            changes.Add($"The {label} was normalized or sanitized.");
        }

        return sanitized;
    }

    private bool IsSafeSegment(string value, FileNamePortabilityMode portabilityMode) =>
        _pathSemantics.IsValidFileName(value, portabilityMode, out _);

    private static bool IsValidDateFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format) ||
            format.Length > 64 ||
            format.Any(character => character is '{' or '}' || char.IsControl(character)))
        {
            return false;
        }

        try
        {
            _ = DateTimeOffset.UnixEpoch.ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsSupportedField(string field) =>
        SupportedFields.Contains(field) ||
        field.Length <= 270 && PluginFieldRegex().IsMatch(field);

    private static bool TryParse(string template, out IReadOnlyList<TemplateToken> tokens)
    {
        var result = new List<TemplateToken>();
        var literal = new StringBuilder();
        for (var index = 0; index < template.Length;)
        {
            if (template[index] == '}')
            {
                tokens = [];
                return false;
            }

            if (template[index] != '{')
            {
                literal.Append(template[index++]);
                continue;
            }

            if (literal.Length > 0)
            {
                result.Add(new TemplateToken(literal.ToString(), null, null));
                literal.Clear();
            }

            var close = template.IndexOf('}', index + 1);
            if (close < 0)
            {
                tokens = [];
                return false;
            }

            var expression = template[(index + 1)..close];
            if (expression.Length == 0 || expression.Contains('{'))
            {
                tokens = [];
                return false;
            }

            var separator = expression.IndexOf(':');
            var field = separator < 0 ? expression : expression[..separator];
            var format = separator < 0 ? null : expression[(separator + 1)..];
            if (string.IsNullOrWhiteSpace(field) || format is not null && format.Length == 0)
            {
                tokens = [];
                return false;
            }

            result.Add(new TemplateToken(null, field, format));
            index = close + 1;
        }

        if (literal.Length > 0)
        {
            result.Add(new TemplateToken(literal.ToString(), null, null));
        }

        tokens = Array.AsReadOnly(result.ToArray());
        return true;
    }

    private static RecipeEvaluationResult Invalid(string originalPath, IEnumerable<string> conflicts) =>
        new(
            false,
            originalPath,
            null,
            null,
            new Dictionary<string, string>(),
            [],
            [],
            [],
            Array.AsReadOnly(conflicts.Distinct(StringComparer.Ordinal).ToArray()),
            [],
            false);

    private static IReadOnlyDictionary<string, string> ToReadOnly(
        IReadOnlyDictionary<string, string> values) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase));

    private sealed record TemplateToken(string? Literal, string? Field, string? Format);
}

/// <summary>
/// Validates complete Workflow Profile and Sorting Recipe models before persistence, import, or resolution.
/// </summary>
/// <remarks>
/// Validation is deliberately stricter than UI input checks so malformed or
/// future data cannot bypass capability, dependency, template, path, or bound
/// rules through direct JSON editing.
/// </remarks>
public sealed class WorkflowValidator : IWorkflowValidator
{
    private readonly IWorkflowTemplateEngine _templateEngine;

    public WorkflowValidator(IWorkflowTemplateEngine templateEngine)
    {
        _templateEngine = templateEngine ?? throw new ArgumentNullException(nameof(templateEngine));
    }

    public WorkflowValidationResult ValidateProfile(
        WorkflowProfile profile,
        IReadOnlyList<SortingRecipe> availableRecipes)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(availableRecipes);
        var issues = new List<WorkflowValidationIssue>();
        if (profile.SchemaVersion != WorkflowLibraryLimits.CurrentProfileSchemaVersion)
        {
            Add(issues, "profile.schema", "The workflow profile schema is unsupported.");
        }

        ValidateIdentity(
            profile.Id,
            profile.Name,
            profile.Description,
            profile.Revision,
            profile.CreatedAtUtc,
            profile.ModifiedAtUtc,
            issues);
        if (profile.Origin is null ||
            profile.Files is null ||
            profile.Extraction is null ||
            profile.Analysis is null ||
            profile.Ai is null ||
            profile.SortingRecipeIds is null ||
            profile.ChangePlans is null ||
            profile.Notifications is null ||
            profile.IncrementalScan is null ||
            profile.FullScan is null)
        {
            Add(issues, "profile.required", "The workflow profile is missing required typed settings.");
            return Result(issues);
        }

        if (profile.Files.IncludedFileTypes is null ||
            profile.Files.ExcludedFileTypes is null ||
            profile.Ai.SelectedFileTypes is null ||
            profile.PluginContributions is null)
        {
            Add(issues, "profile.collections", "The workflow profile contains a missing typed collection.");
            return Result(issues);
        }

        if (!Enum.IsDefined(profile.Origin.Kind) ||
            !Enum.IsDefined(profile.Ai.InvocationPolicy) ||
            !Enum.IsDefined(profile.UncertaintyPolicy))
        {
            Add(issues, "profile.capability", "The workflow profile requests an unsupported capability or policy.");
        }

        ValidateExtensions(profile.Files.IncludedFileTypes, issues);
        ValidateExtensions(profile.Files.ExcludedFileTypes, issues);
        ValidateExtensions(profile.Ai.SelectedFileTypes, issues);
        if (profile.Files.MaximumFileSizeBytes <= 0 ||
            profile.Extraction.MaximumPagesPerDocument is < 1 or > 500 ||
            !Bounded(profile.Extraction.OcrLanguage, 32) ||
            profile.SortingRecipeIds.Count > WorkflowLibraryLimits.MaximumRecipes ||
            profile.SortingRecipeIds.Any(id => !Bounded(id, WorkflowLibraryLimits.MaximumIdentifierLength)) ||
            profile.SortingRecipeIds.Distinct(StringComparer.Ordinal).Count() != profile.SortingRecipeIds.Count)
        {
            Add(issues, "profile.bounds", "The workflow profile contains invalid or excessive settings.");
        }

        if (profile.Files.IncludedFileTypes.Intersect(
                profile.Files.ExcludedFileTypes,
                StringComparer.OrdinalIgnoreCase).Any())
        {
            Add(issues, "profile.file-types", "Included and excluded file-type lists cannot overlap.");
        }

        ValidatePluginReferences(
            profile.PluginContributions,
            expectedPoint: null,
            issues);

        if (profile.Origin.SourceId is { } sourceId &&
            !Bounded(sourceId, WorkflowLibraryLimits.MaximumIdentifierLength) ||
            profile.Origin.SourceApplicationVersion is { } sourceVersion &&
            !Bounded(sourceVersion, 64))
        {
            Add(issues, "profile.origin", "The workflow profile origin is invalid or excessive.");
        }

        var available = availableRecipes.Select(recipe => recipe.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in profile.SortingRecipeIds.Where(id => !available.Contains(id)))
        {
            Add(issues, "profile.recipe-missing", $"Sorting recipe \"{missing}\" is unavailable.");
        }

        if (profile.Ai.Enabled && profile.Ai.InvocationPolicy == WorkflowAiInvocationPolicy.Disabled)
        {
            Add(issues, "profile.ai-contradiction", "AI is enabled but the invocation policy is Disabled.");
        }

        if (profile.Ai.Enabled &&
            profile.Ai.InvocationPolicy == WorkflowAiInvocationPolicy.AfterTextExtraction &&
            !profile.Extraction.TextEnabled &&
            !profile.Extraction.OcrEnabled)
        {
            Add(issues, "profile.ai-extraction", "The AI policy requires text or OCR extraction, but both are disabled.");
        }

        if (profile.Ai.Enabled &&
            profile.Ai.InvocationPolicy == WorkflowAiInvocationPolicy.SelectedFileTypesOnly &&
            profile.Ai.SelectedFileTypes.Count == 0)
        {
            Add(issues, "profile.ai-file-types", "The selected-file-types AI policy requires at least one file type.");
        }

        if (!profile.Ai.Enabled && profile.Ai.InvocationPolicy != WorkflowAiInvocationPolicy.Disabled)
        {
            issues.Add(new WorkflowValidationIssue(
                "profile.ai-inefficient",
                "The AI policy is configured but profile AI is disabled.",
                false));
        }

        if (profile.Extraction.OcrEnabled && !profile.Extraction.MetadataEnabled)
        {
            Add(issues, "profile.ocr-metadata", "OCR requires metadata extraction to be enabled.");
        }

        if (profile.Analysis.RuleEvaluationEnabled &&
            profile.SortingRecipeIds.Count == 0)
        {
            issues.Add(new WorkflowValidationIssue(
                "profile.rules-empty",
                "Rule evaluation is enabled but no persistent sorting recipe is selected.",
                false));
        }

        return Result(issues);
    }

    public WorkflowValidationResult ValidateRecipe(SortingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var issues = new List<WorkflowValidationIssue>();
        if (recipe.SchemaVersion != WorkflowLibraryLimits.CurrentRecipeSchemaVersion)
        {
            Add(issues, "recipe.schema", "The sorting recipe schema is unsupported.");
        }

        ValidateIdentity(
            recipe.Id,
            recipe.Name,
            recipe.Description,
            recipe.Revision,
            recipe.CreatedAtUtc,
            recipe.ModifiedAtUtc,
            issues);
        if (recipe.Origin is null ||
            recipe.Applicability is null ||
            recipe.RequiredFields is null ||
            recipe.OptionalFields is null ||
            recipe.FallbackValues is null ||
            recipe.Normalization is null ||
            recipe.Rules is null ||
            recipe.PreviewExamples is null)
        {
            Add(issues, "recipe.required", "The sorting recipe is missing required typed settings.");
            return Result(issues);
        }

        if (recipe.Applicability.IncludedFileTypes is null ||
            recipe.Applicability.Categories is null ||
            recipe.PluginFieldContributions is null ||
            recipe.PreviewExamples.Any(example =>
                example is null ||
                example.Values is null))
        {
            Add(issues, "recipe.collections", "The sorting recipe contains a missing typed collection.");
            return Result(issues);
        }

        if (!Enum.IsDefined(recipe.Origin.Kind) ||
            !Enum.IsDefined(recipe.FileNamePortability) ||
            !Enum.IsDefined(recipe.Normalization.CasePolicy) ||
            !Enum.IsDefined(recipe.Normalization.InvalidCharacterPolicy) ||
            !Enum.IsDefined(recipe.Normalization.MissingValuePolicy) ||
            !Enum.IsDefined(recipe.CollisionPolicy) ||
            !Enum.IsDefined(recipe.UncertaintyPolicy) ||
            recipe.Applicability.Categories.Any(category => !Enum.IsDefined(category)))
        {
            Add(issues, "recipe.capability", "The sorting recipe requests an unsupported capability or policy.");
        }

        ValidateExtensions(recipe.Applicability.IncludedFileTypes, issues);
        if (recipe.Priority is < -10_000 or > 10_000 ||
            recipe.Applicability.MinimumFileSizeBytes is < 0 ||
            recipe.Applicability.MaximumFileSizeBytes is < 0 ||
            recipe.Applicability.MinimumFileSizeBytes is { } minimum &&
            recipe.Applicability.MaximumFileSizeBytes is { } maximum &&
            minimum > maximum ||
            recipe.MaximumFileNameLength is < 1 or > WorkflowLibraryLimits.MaximumFileNameLength ||
            recipe.RequiredFields.Count > WorkflowLibraryLimits.MaximumFields ||
            recipe.OptionalFields.Count > WorkflowLibraryLimits.MaximumFields ||
            recipe.FallbackValues.Count > WorkflowLibraryLimits.MaximumFields ||
            recipe.Rules.Count > WorkflowLibraryLimits.MaximumRulesPerRecipe ||
            recipe.PreviewExamples.Count > WorkflowLibraryLimits.MaximumPreviewExamples ||
            string.IsNullOrWhiteSpace(recipe.DefaultDateFormat) ||
            recipe.DefaultDateFormat.Length > 64)
        {
            Add(issues, "recipe.bounds", "The sorting recipe contains invalid or excessive settings.");
        }

        if (recipe.RequiredFields.Concat(recipe.OptionalFields)
            .Any(field => !ValidFieldName(field)) ||
            recipe.RequiredFields.Intersect(recipe.OptionalFields, StringComparer.OrdinalIgnoreCase).Any() ||
            recipe.FallbackValues.Any(pair =>
                !ValidFieldName(pair.Key) ||
                pair.Value is null ||
                pair.Value.Length > 256 ||
                pair.Value.Any(char.IsControl)) ||
            !Bounded(recipe.Normalization.EmptyValueReplacement, 128))
        {
            Add(issues, "recipe.fields", "Recipe fields and fallbacks must be bounded and unambiguous.");
        }

        ValidatePluginReferences(
            recipe.PluginFieldContributions,
            OpenSorSe.Extensions.Abstractions.ExtensionPointKind.RecipeFieldProvider,
            issues);

        if (recipe.Rules.Any(rule => !IsValidRecipeRule(rule)) ||
            recipe.Rules
                .Where(rule => rule is not null)
                .Select(rule => rule.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != recipe.Rules.Count)
        {
            Add(
                issues,
                "recipe.rules",
                "Recipe rules must be bounded, unique, declarative, and limited to non-destructive supported actions.");
        }

        if (recipe.PreviewExamples.Any(example =>
                !Bounded(example.OriginalPath, WorkflowLibraryLimits.MaximumPathLength) ||
                example.Values.Count > WorkflowLibraryLimits.MaximumFields ||
                example.Values.Any(pair =>
                    !Bounded(pair.Key, 64) ||
                    pair.Value is null ||
                    pair.Value.Length > 256 ||
                    pair.Value.Any(char.IsControl))))
        {
            Add(issues, "recipe.preview", "Recipe preview examples must be bounded and contain valid sample metadata.");
        }

        if (recipe.Origin.SourceId is { } recipeSourceId &&
            !Bounded(recipeSourceId, WorkflowLibraryLimits.MaximumIdentifierLength) ||
            recipe.Origin.SourceApplicationVersion is { } recipeSourceVersion &&
            !Bounded(recipeSourceVersion, 64))
        {
            Add(issues, "recipe.origin", "The sorting recipe origin is invalid or excessive.");
        }

        issues.AddRange(_templateEngine.ValidateRecipeTemplates(recipe).Issues);
        return Result(issues);
    }

    private static bool IsValidRecipeRule(OpenSorSe.Rules.Models.FileRule? rule)
    {
        if (rule is null ||
            !Bounded(rule.Id, WorkflowLibraryLimits.MaximumIdentifierLength) ||
            !Bounded(rule.Name, WorkflowLibraryLimits.MaximumNameLength) ||
            rule.Conditions is null ||
            rule.Conditions.Count == 0 ||
            rule.Action is null ||
            rule.Action.Kind is OpenSorSe.Rules.Models.RuleActionKind.Move or
                OpenSorSe.Rules.Models.RuleActionKind.Copy or
                OpenSorSe.Rules.Models.RuleActionKind.Delete ||
            !Enum.IsDefined(rule.Action.Kind))
        {
            return false;
        }

        var actionValid = rule.Action.Kind switch
        {
            OpenSorSe.Rules.Models.RuleActionKind.NoAction =>
                rule.Action.DestinationPath is null && rule.Action.NameTemplate is null,
            OpenSorSe.Rules.Models.RuleActionKind.Rename =>
                Bounded(rule.Action.NameTemplate, WorkflowLibraryLimits.MaximumTemplateLength) &&
                !rule.Action.NameTemplate!.Contains('/') &&
                !rule.Action.NameTemplate.Contains('\\'),
            _ => false,
        };
        return actionValid && rule.Conditions.All(IsValidRecipeRuleCondition);
    }

    private static bool IsValidRecipeRuleCondition(OpenSorSe.Rules.Models.RuleCondition? condition)
    {
        if (condition is null || !Enum.IsDefined(condition.Kind))
        {
            return false;
        }

        return condition.Kind switch
        {
            OpenSorSe.Rules.Models.RuleConditionKind.FileCategoryEquals =>
                condition.CategoryValue is not null &&
                condition.CategoryValue != OpenSorSe.Scanner.Models.FileCategory.Unknown &&
                Enum.IsDefined(condition.CategoryValue.Value) &&
                condition.StringValue is null &&
                condition.LongValue is null &&
                condition.DuplicateStatusValue is null,
            OpenSorSe.Rules.Models.RuleConditionKind.DuplicateStatusEquals =>
                condition.DuplicateStatusValue is not null &&
                Enum.IsDefined(condition.DuplicateStatusValue.Value) &&
                condition.StringValue is null &&
                condition.LongValue is null &&
                condition.CategoryValue is null,
            OpenSorSe.Rules.Models.RuleConditionKind.ExtensionEquals =>
                Bounded(condition.StringValue, 32) &&
                condition.StringValue!.StartsWith('.') &&
                condition.LongValue is null &&
                condition.CategoryValue is null &&
                condition.DuplicateStatusValue is null,
            OpenSorSe.Rules.Models.RuleConditionKind.ExactFileNameEquals =>
                Bounded(condition.StringValue, WorkflowLibraryLimits.MaximumFileNameLength) &&
                condition.LongValue is null &&
                condition.CategoryValue is null &&
                condition.DuplicateStatusValue is null,
            OpenSorSe.Rules.Models.RuleConditionKind.MinimumSizeInBytes or
                OpenSorSe.Rules.Models.RuleConditionKind.MaximumSizeInBytes =>
                condition.LongValue is >= 0 &&
                condition.StringValue is null &&
                condition.CategoryValue is null &&
                condition.DuplicateStatusValue is null,
            _ => false,
        };
    }

    private static void ValidateIdentity(
        string id,
        string name,
        string? description,
        int revision,
        DateTimeOffset createdAt,
        DateTimeOffset modifiedAt,
        List<WorkflowValidationIssue> issues)
    {
        if (!Bounded(id, WorkflowLibraryLimits.MaximumIdentifierLength) ||
            !Bounded(name, WorkflowLibraryLimits.MaximumNameLength) ||
            description is { Length: > WorkflowLibraryLimits.MaximumDescriptionLength } ||
            description?.Any(char.IsControl) == true ||
            revision < 1 ||
            createdAt.Offset != TimeSpan.Zero ||
            modifiedAt.Offset != TimeSpan.Zero ||
            modifiedAt < createdAt)
        {
            Add(issues, "item.identity", "The item identity, timestamps, revision, or display text is invalid.");
        }
    }

    private static void ValidateExtensions(
        IReadOnlyList<string> extensions,
        List<WorkflowValidationIssue> issues)
    {
        if (extensions is null ||
            extensions.Count > WorkflowLibraryLimits.MaximumExtensions ||
            extensions.Any(extension =>
                string.IsNullOrWhiteSpace(extension) ||
                !extension.StartsWith('.') ||
                extension.Length > 32 ||
                extension.Any(character => char.IsControl(character) || character is '/' or '\\' or '*' or '?')) ||
            extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != extensions.Count)
        {
            Add(issues, "item.extensions", "File-type lists contain invalid, duplicate, or excessive extensions.");
        }
    }

    private static bool Bounded(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum &&
        !value.Any(char.IsControl);

    private static bool ValidFieldName(string? value) =>
        Bounded(value, 64) ||
        value is { Length: <= 270 } &&
        value.StartsWith("plugin.", StringComparison.Ordinal) &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '-');

    private static void ValidatePluginReferences(
        IReadOnlyList<OpenSorSe.Application.Plugins.PluginContributionReference> references,
        OpenSorSe.Extensions.Abstractions.ExtensionPointKind? expectedPoint,
        List<WorkflowValidationIssue> issues)
    {
        if (references.Count > WorkflowLibraryLimits.MaximumFields ||
            references.Any(reference =>
                reference is null ||
                !Bounded(reference.PluginId, 128) ||
                !Bounded(reference.ContributionId, 128) ||
                reference.PluginVersion is null ||
                !OpenSorSe.Application.Plugins.PluginManifestParser.TryVersion(
                    reference.PluginVersion,
                    out _) ||
                !Enum.IsDefined(reference.ExtensionPoint) ||
                expectedPoint is not null && reference.ExtensionPoint != expectedPoint) ||
            references
                .Select(reference => (reference.PluginId, reference.ContributionId, reference.ExtensionPoint))
                .Distinct()
                .Count() != references.Count)
        {
            Add(
                issues,
                "plugin.references",
                "Plugin contribution references must be bounded, unique, versioned when specified, and use the expected extension point.");
        }
    }

    private static void Add(
        List<WorkflowValidationIssue> issues,
        string code,
        string message) =>
        issues.Add(new WorkflowValidationIssue(code, message, true));
}

internal static class WorkflowValidationResults
{
    public static WorkflowValidationResult Result(IEnumerable<WorkflowValidationIssue> issues)
    {
        var values = issues.Distinct().ToArray();
        return new WorkflowValidationResult(
            values.All(issue => !issue.IsBlocking),
            Array.AsReadOnly(values));
    }
}
