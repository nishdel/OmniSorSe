#pragma warning disable CS1591

using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Workflows;

public static class BuiltInWorkflowLibrary
{
    private static readonly DateTimeOffset BuiltInTimestamp = DateTimeOffset.UnixEpoch;

    public static IReadOnlyList<WorkflowProfile> Profiles { get; } = Array.AsReadOnly(
    [
        Profile(
            BuiltInWorkflowIds.GeneralDocuments,
            "General Documents",
            "Balanced local processing for common office documents.",
            [".pdf", ".txt", ".md", ".docx", ".xlsx", ".pptx", ".odt", ".ods"],
            text: true,
            ocr: false,
            duplicates: true,
            classification: true,
            rules: true,
            ai: true,
            WorkflowAiInvocationPolicy.MissingDeterministicClassificationOnly,
            []),
        Profile(
            BuiltInWorkflowIds.InvoicesAndReceipts,
            "Invoices and Receipts",
            "Document and image processing with optional OCR and conservative invoice-oriented suggestions.",
            [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"],
            text: true,
            ocr: true,
            duplicates: true,
            classification: true,
            rules: true,
            ai: true,
            WorkflowAiInvocationPolicy.AfterTextExtraction,
            [BuiltInWorkflowIds.InvoiceRecipe]),
        Profile(
            BuiltInWorkflowIds.Photos,
            "Photos",
            "Metadata-first photo analysis with no AI or OCR by default. Organization examples label filesystem dates explicitly.",
            [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".heic", ".webp"],
            text: false,
            ocr: false,
            duplicates: true,
            classification: true,
            rules: true,
            ai: false,
            WorkflowAiInvocationPolicy.Disabled,
            [BuiltInWorkflowIds.PhotoRecipe]),
        Profile(
            BuiltInWorkflowIds.DownloadsCleanup,
            "Downloads Cleanup",
            "Conservative classification and review-only organization suggestions for completed downloads.",
            [".pdf", ".txt", ".docx", ".xlsx", ".pptx", ".zip", ".7z", ".rar", ".png", ".jpg", ".jpeg"],
            text: true,
            ocr: false,
            duplicates: true,
            classification: true,
            rules: true,
            ai: false,
            WorkflowAiInvocationPolicy.Disabled,
            [BuiltInWorkflowIds.DownloadsRecipe]),
        Profile(
            BuiltInWorkflowIds.MinimalLocalProcessing,
            "Minimal Local Processing",
            "Metadata-only, low-cost scanning for large folders or constrained systems.",
            [],
            text: false,
            ocr: false,
            duplicates: false,
            classification: false,
            rules: false,
            ai: false,
            WorkflowAiInvocationPolicy.Disabled,
            []),
    ]);

    public static IReadOnlyList<SortingRecipe> Recipes { get; } = Array.AsReadOnly(
    [
        Recipe(
            BuiltInWorkflowIds.GeneralDocumentRecipe,
            "General document filing",
            "Preserves the source name and groups classified documents below Documents.",
            ["originalName", "category"],
            "{originalName}",
            "Documents/{category}",
            [".pdf", ".txt", ".md", ".docx", ".xlsx", ".pptx"]),
        Recipe(
            BuiltInWorkflowIds.InvoiceRecipe,
            "Filesystem date and original name",
            "Uses the explicit filesystem modified date and preserves the original name. It does not claim a document or invoice date.",
            ["filesystemModifiedDate", "originalName"],
            "{filesystemModifiedDate:yyyy-MM-dd}_{originalName}",
            "Invoices/{filesystemModifiedDate:yyyy}",
            [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"],
            optionalFields: []),
        Recipe(
            BuiltInWorkflowIds.PhotoRecipe,
            "Filesystem-created photos",
            "Groups photos by the explicit filesystem created timestamp while preserving the original name. It does not claim EXIF capture time.",
            ["filesystemCreatedDate", "originalName"],
            "{filesystemCreatedDate:yyyy-MM-dd}_{originalName}",
            "Photos/{filesystemCreatedDate:yyyy}/{filesystemCreatedDate:MM}",
            [".jpg", ".jpeg", ".png", ".tif", ".tiff", ".heic", ".webp"]),
        Recipe(
            BuiltInWorkflowIds.DownloadsRecipe,
            "Downloads by category",
            "Groups completed downloads by deterministic category without deleting or overwriting.",
            ["originalName", "category"],
            "{originalName}",
            "Downloads/{category}",
            [".pdf", ".txt", ".docx", ".xlsx", ".pptx", ".zip", ".7z", ".rar", ".png", ".jpg", ".jpeg"]),
        Recipe(
            BuiltInWorkflowIds.TrustedClassificationRecipe,
            "Theme and document type",
            "Uses only accepted or uniquely usable Strong deterministic Theme and Document Type evidence during reviewed organization.",
            ["originalName", "theme", "documentType"],
            "{originalName}",
            "{theme}/{documentType}",
            [".pdf", ".txt", ".md", ".docx", ".xlsx", ".pptx", ".csv", ".tsv", ".png", ".jpg", ".jpeg"]),
    ]);

    private static WorkflowProfile Profile(
        string id,
        string name,
        string description,
        IReadOnlyList<string> extensions,
        bool text,
        bool ocr,
        bool duplicates,
        bool classification,
        bool rules,
        bool ai,
        WorkflowAiInvocationPolicy aiPolicy,
        IReadOnlyList<string> recipeIds) =>
        new(
            WorkflowLibraryLimits.CurrentProfileSchemaVersion,
            id,
            1,
            name,
            description,
            BuiltInTimestamp,
            BuiltInTimestamp,
            true,
            false,
            true,
            new WorkflowProfileOrigin(WorkflowOriginKind.BuiltIn, SourceApplicationVersion: "2.9.0"),
            new WorkflowFileSelectionOptions(
                Array.AsReadOnly(extensions.ToArray()),
                Array.AsReadOnly(new[] { ".tmp", ".part", ".crdownload", ".download" }),
                1024L * 1024 * 1024),
            new WorkflowExtractionOptions(true, text, ocr, true, "eng", 25),
            new WorkflowAnalysisOptions(duplicates, classification, rules),
            new WorkflowAiOptions(ai, aiPolicy, Array.AsReadOnly(extensions.ToArray())),
            Array.AsReadOnly(recipeIds.ToArray()),
            ai ? WorkflowUncertaintyPolicy.IncludeAsWarning : WorkflowUncertaintyPolicy.Skip,
            new WorkflowChangePlanOptions(true, true, true, true, true),
            new WorkflowNotificationOptions(true, true, true),
            new WorkflowScanBehavior(true, true, true, true),
            new WorkflowScanBehavior(true, false, true, true));

    private static SortingRecipe Recipe(
        string id,
        string name,
        string description,
        IReadOnlyList<string> requiredFields,
        string namingTemplate,
        string destinationTemplate,
        IReadOnlyList<string> extensions,
        IReadOnlyList<string>? optionalFields = null) =>
        new(
            WorkflowLibraryLimits.CurrentRecipeSchemaVersion,
            id,
            1,
            name,
            description,
            BuiltInTimestamp,
            BuiltInTimestamp,
            true,
            false,
            true,
            new WorkflowProfileOrigin(WorkflowOriginKind.BuiltIn, SourceApplicationVersion: "2.9.0"),
            100,
            new RecipeApplicability(
                Array.AsReadOnly(extensions.ToArray()),
                Array.AsReadOnly(Array.Empty<FileCategory>())),
            namingTemplate,
            destinationTemplate,
            Array.AsReadOnly(requiredFields.ToArray()),
            Array.AsReadOnly((optionalFields ?? []).ToArray()),
            Fallbacks(optionalFields),
            new RecipeNormalizationOptions(
                WorkflowCasePolicy.Preserve,
                WorkflowInvalidCharacterPolicy.ReplaceWithUnderscore,
                WorkflowMissingValuePolicy.UseFallback),
            "yyyy-MM-dd",
            WorkflowLibraryLimits.MaximumFileNameLength,
            WorkflowCollisionPolicy.Block,
            WorkflowUncertaintyPolicy.IncludeAsWarning,
            true,
            Array.AsReadOnly(Array.Empty<OpenSorSe.Rules.Models.FileRule>()),
            Array.AsReadOnly(Array.Empty<RecipePreviewExample>()));

    private static IReadOnlyDictionary<string, string> Fallbacks(
        IReadOnlyList<string>? optionalFields)
    {
        var fields = optionalFields?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fields.Contains("vendor"))
        {
            values["vendor"] = "UnknownVendor";
        }

        if (fields.Contains("amount"))
        {
            values["amount"] = "UnknownAmount";
        }

        if (fields.Contains("currency"))
        {
            values["currency"] = string.Empty;
        }

        return values;
    }
}
