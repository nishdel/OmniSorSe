#pragma warning disable CS1591

using OpenSorSe.Application.Models;
using OpenSorSe.Application.Watching;
using OpenSorSe.Executor.Models;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Workflows;

public static class WorkflowLibraryLimits
{
    public const int CurrentLibrarySchemaVersion = 2;
    public const int CurrentProfileSchemaVersion = 1;
    public const int CurrentRecipeSchemaVersion = 1;
    public const int CurrentExportSchemaVersion = 1;
    public const int MaximumProfiles = 128;
    public const int MaximumRecipes = 256;
    public const int MaximumRulesPerRecipe = 128;
    public const int MaximumExtensions = 128;
    public const int MaximumFields = 64;
    public const int MaximumPreviewExamples = 16;
    public const int MaximumNameLength = 128;
    public const int MaximumDescriptionLength = 2_048;
    public const int MaximumIdentifierLength = 256;
    public const int MaximumTemplateLength = 1_024;
    public const int MaximumFileNameLength = 240;
    public const int MaximumPathLength = 32_767;
    public const int MaximumDiagnostics = 200;
    public const long MaximumLibraryBytes = 8L * 1024 * 1024;
    public const long MaximumImportBytes = 2L * 1024 * 1024;
}

public static class BuiltInWorkflowIds
{
    public const string GeneralDocuments = "builtin:general-documents";
    public const string InvoicesAndReceipts = "builtin:invoices-and-receipts";
    public const string Photos = "builtin:photos";
    public const string DownloadsCleanup = "builtin:downloads-cleanup";
    public const string MinimalLocalProcessing = "builtin:minimal-local-processing";
    public const string GeneralDocumentRecipe = "builtin:recipe:general-documents";
    public const string InvoiceRecipe = "builtin:recipe:invoices";
    public const string PhotoRecipe = "builtin:recipe:photos";
    public const string DownloadsRecipe = "builtin:recipe:downloads";
}

public enum WorkflowOriginKind
{
    BuiltIn,
    UserCreated,
    Duplicated,
    Imported,
}

public enum WorkflowAiInvocationPolicy
{
    Disabled,
    MissingDeterministicClassificationOnly,
    AfterTextExtraction,
    SelectedFileTypesOnly,
    ExplicitRetryOnly,
}

public enum WorkflowUncertaintyPolicy
{
    Skip,
    IncludeAsWarning,
    RequireAiOrSkip,
}

public enum WorkflowCasePolicy
{
    Preserve,
    Lower,
    Upper,
    Title,
}

public enum WorkflowInvalidCharacterPolicy
{
    ReplaceWithUnderscore,
    Remove,
}

public enum WorkflowMissingValuePolicy
{
    SkipItem,
    UseFallback,
}

public enum WorkflowCollisionPolicy
{
    Block,
    RequireReview,
}

public enum WorkflowImportConflictPolicy
{
    ImportAsCopy,
    ReplaceUserCreated,
    Skip,
    Cancel,
}

public enum WorkflowExportContentType
{
    WorkflowProfile,
    SortingRecipe,
}

public enum WorkflowDiagnosticKind
{
    Load,
    Recovery,
    Migration,
    Validation,
    Resolution,
    TemplateEvaluation,
    Import,
    Export,
    Assignment,
}

public sealed record WorkflowProfileOrigin(
    WorkflowOriginKind Kind,
    string? SourceId = null,
    string? SourceApplicationVersion = null);

public sealed record WorkflowFileSelectionOptions(
    IReadOnlyList<string> IncludedFileTypes,
    IReadOnlyList<string> ExcludedFileTypes,
    long MaximumFileSizeBytes,
    bool IncludeHiddenFiles = false);

public sealed record WorkflowExtractionOptions(
    bool MetadataEnabled,
    bool TextEnabled,
    bool OcrEnabled,
    bool OcrOnlyWhenTextUnavailable,
    string OcrLanguage,
    int MaximumPagesPerDocument);

public sealed record WorkflowAnalysisOptions(
    bool DuplicateAnalysisEnabled,
    bool ClassificationEnabled,
    bool RuleEvaluationEnabled);

public sealed record WorkflowAiOptions(
    bool Enabled,
    WorkflowAiInvocationPolicy InvocationPolicy,
    IReadOnlyList<string> SelectedFileTypes);

public sealed record WorkflowChangePlanOptions(
    bool GenerateChangePlans,
    bool PermitRenameProposals,
    bool PermitMoveProposals,
    bool PermitDirectoryProposals,
    bool IncludeUncertainItemsAsWarnings);

public sealed record WorkflowNotificationOptions(
    bool NotifyWhenComplete,
    bool NotifyWhenPlanReady,
    bool NotifyOnErrors);

public sealed record WorkflowScanBehavior(
    bool Enabled,
    bool ReanalyseChangedContentOnly,
    bool ReconcileMissingItems,
    bool PreserveUnchangedAnalysis);

public sealed record WorkflowProfile(
    int SchemaVersion,
    string Id,
    int Revision,
    string Name,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedAtUtc,
    bool IsEnabled,
    bool IsArchived,
    bool IsBuiltIn,
    WorkflowProfileOrigin Origin,
    WorkflowFileSelectionOptions Files,
    WorkflowExtractionOptions Extraction,
    WorkflowAnalysisOptions Analysis,
    WorkflowAiOptions Ai,
    IReadOnlyList<string> SortingRecipeIds,
    WorkflowUncertaintyPolicy UncertaintyPolicy,
    WorkflowChangePlanOptions ChangePlans,
    WorkflowNotificationOptions Notifications,
    WorkflowScanBehavior IncrementalScan,
    WorkflowScanBehavior FullScan);

public sealed record RecipeApplicability(
    IReadOnlyList<string> IncludedFileTypes,
    IReadOnlyList<FileCategory> Categories,
    long? MinimumFileSizeBytes = null,
    long? MaximumFileSizeBytes = null);

public sealed record RecipeNormalizationOptions(
    WorkflowCasePolicy CasePolicy,
    WorkflowInvalidCharacterPolicy InvalidCharacterPolicy,
    WorkflowMissingValuePolicy MissingValuePolicy,
    string EmptyValueReplacement = "Unknown",
    bool CollapseWhitespace = true,
    bool NormalizeUnicode = true);

public sealed record RecipePreviewExample(
    string OriginalPath,
    IReadOnlyDictionary<string, string> Values);

public sealed record SortingRecipe(
    int SchemaVersion,
    string Id,
    int Revision,
    string Name,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedAtUtc,
    bool IsEnabled,
    bool IsArchived,
    bool IsBuiltIn,
    WorkflowProfileOrigin Origin,
    int Priority,
    RecipeApplicability Applicability,
    string NamingTemplate,
    string DestinationTemplate,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> OptionalFields,
    IReadOnlyDictionary<string, string> FallbackValues,
    RecipeNormalizationOptions Normalization,
    string DefaultDateFormat,
    int MaximumFileNameLength,
    WorkflowCollisionPolicy CollisionPolicy,
    WorkflowUncertaintyPolicy UncertaintyPolicy,
    bool PreserveExtension,
    IReadOnlyList<FileRule> Rules,
    IReadOnlyList<RecipePreviewExample> PreviewExamples);

public sealed record WorkflowProfileOverride(
    long? MaximumFileSizeBytes = null,
    bool? OcrEnabled = null,
    bool? DuplicateAnalysisEnabled = null,
    bool? AiEnabled = null,
    bool? GenerateChangePlans = null);

public sealed record WorkflowConfigurationSnapshot(
    string ProfileId,
    string ProfileName,
    int ProfileRevision,
    DateTimeOffset ProfileModifiedAtUtc,
    IReadOnlyList<WorkflowRecipeSnapshot> Recipes,
    WorkflowFileSelectionOptions Files,
    WorkflowExtractionOptions Extraction,
    WorkflowAnalysisOptions Analysis,
    WorkflowAiOptions Ai,
    WorkflowUncertaintyPolicy UncertaintyPolicy,
    WorkflowChangePlanOptions ChangePlans,
    WorkflowNotificationOptions Notifications,
    WorkflowScanBehavior ScanBehavior,
    string ResolutionSource,
    DateTimeOffset ResolvedAtUtc);

public sealed record WorkflowRecipeSnapshot(
    string RecipeId,
    string RecipeName,
    int RecipeRevision,
    DateTimeOffset RecipeModifiedAtUtc,
    int Priority);

public sealed record ResolvedWorkflowConfiguration(
    WorkflowProfile Profile,
    IReadOnlyList<SortingRecipe> Recipes,
    WorkflowFileSelectionOptions Files,
    WorkflowExtractionOptions Extraction,
    WorkflowAnalysisOptions Analysis,
    WorkflowAiOptions Ai,
    WorkflowUncertaintyPolicy UncertaintyPolicy,
    WorkflowChangePlanOptions ChangePlans,
    WorkflowNotificationOptions Notifications,
    WorkflowScanBehavior ScanBehavior,
    WorkflowConfigurationSnapshot Snapshot);

public sealed record WorkflowResolutionResult(
    bool IsAvailable,
    ResolvedWorkflowConfiguration? Configuration,
    string Message,
    IReadOnlyList<string> Warnings)
{
    public static WorkflowResolutionResult Unavailable(string message, params string[] warnings) =>
        new(false, null, message, Array.AsReadOnly(warnings));
}

public sealed record WorkflowUsageInfo(
    string ItemId,
    IReadOnlyList<string> WatchedFolderIds,
    IReadOnlyList<string> ProfileIds,
    int RecentScanCount)
{
    public bool IsReferenced => WatchedFolderIds.Count > 0 || ProfileIds.Count > 0;
}

public sealed record WorkflowLibraryLoadResult(
    IReadOnlyList<WorkflowProfile> Profiles,
    IReadOnlyList<SortingRecipe> Recipes,
    string? RecoveryMessage,
    string? PreservedCorruptCopyPath,
    bool Migrated);

public sealed record WorkflowValidationIssue(
    string Code,
    string Message,
    bool IsBlocking);

public sealed record WorkflowValidationResult(
    bool IsValid,
    IReadOnlyList<WorkflowValidationIssue> Issues);

public sealed record RecipeFieldValue(
    string Value,
    string EvidenceSource,
    bool IsAiDerived = false);

public sealed record RecipeEvaluationContext(
    string OrganizationRoot,
    string OriginalPath,
    IReadOnlyDictionary<string, RecipeFieldValue> Values,
    IReadOnlySet<string>? OccupiedDestinationPaths = null);

public sealed record RecipeEvaluationResult(
    bool IsValid,
    string OriginalPath,
    string? ProposedFileName,
    string? ProposedDestinationPath,
    IReadOnlyDictionary<string, string> ValuesUsed,
    IReadOnlyList<string> MissingValues,
    IReadOnlyList<string> FallbackValues,
    IReadOnlyList<string> SanitizationChanges,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> Warnings,
    bool RequiresAiDerivedValues);

public sealed record WorkflowImportResult(
    bool Imported,
    string Message,
    string? ImportedId,
    IReadOnlyList<string> Warnings);

public sealed record WorkflowDiagnostic(
    DateTimeOffset TimestampUtc,
    WorkflowDiagnosticKind Kind,
    string Summary,
    string? ItemId = null);

public sealed record WorkflowRecipePlanResult(
    ChangePlan? Plan,
    IReadOnlyList<RecipeEvaluationResult> Evaluations,
    IReadOnlyList<string> Warnings);

public sealed class WorkflowProfileUnavailableException : InvalidOperationException
{
    public WorkflowProfileUnavailableException(string message)
        : base(message)
    {
    }
}

public interface IWorkflowLibraryStore
{
    Task<WorkflowLibraryLoadResult> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(
        IReadOnlyList<WorkflowProfile> profiles,
        IReadOnlyList<SortingRecipe> recipes,
        CancellationToken cancellationToken);
}

public interface IWorkflowValidator
{
    WorkflowValidationResult ValidateProfile(
        WorkflowProfile profile,
        IReadOnlyList<SortingRecipe> availableRecipes);

    WorkflowValidationResult ValidateRecipe(SortingRecipe recipe);
}

public interface IWorkflowUsageInspector
{
    Task<WorkflowUsageInfo> InspectAsync(string itemId, CancellationToken cancellationToken);
}

public interface IWorkflowLibraryService
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkflowProfile>> ListProfilesAsync(bool includeArchived, CancellationToken cancellationToken);
    Task<IReadOnlyList<SortingRecipe>> ListRecipesAsync(bool includeArchived, CancellationToken cancellationToken);
    Task<WorkflowProfile?> GetProfileAsync(string id, CancellationToken cancellationToken);
    Task<SortingRecipe?> GetRecipeAsync(string id, CancellationToken cancellationToken);
    Task<WorkflowProfile> CreateProfileAsync(WorkflowProfile profile, CancellationToken cancellationToken);
    Task<WorkflowProfile> DuplicateProfileAsync(string id, string newName, CancellationToken cancellationToken);
    Task<WorkflowProfile> UpdateProfileAsync(WorkflowProfile profile, CancellationToken cancellationToken);
    Task<WorkflowProfile> SetProfileArchivedAsync(string id, bool archived, CancellationToken cancellationToken);
    Task<WorkflowProfile> SetProfileEnabledAsync(string id, bool enabled, CancellationToken cancellationToken);
    Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken);
    Task<SortingRecipe> CreateRecipeAsync(SortingRecipe recipe, CancellationToken cancellationToken);
    Task<SortingRecipe> DuplicateRecipeAsync(string id, string newName, CancellationToken cancellationToken);
    Task<SortingRecipe> UpdateRecipeAsync(SortingRecipe recipe, CancellationToken cancellationToken);
    Task<SortingRecipe> SetRecipeArchivedAsync(string id, bool archived, CancellationToken cancellationToken);
    Task<bool> DeleteRecipeAsync(string id, CancellationToken cancellationToken);
    Task<WorkflowUsageInfo> GetUsageAsync(string itemId, CancellationToken cancellationToken);
    void RecordDiagnostic(WorkflowDiagnosticKind kind, string summary, string? itemId = null);
    IReadOnlyList<WorkflowDiagnostic> GetDiagnostics();
    string? RecoveryMessage { get; }
    string? PreservedCorruptCopyPath { get; }
}

public interface IWorkflowTemplateEngine
{
    WorkflowValidationResult ValidateRecipeTemplates(SortingRecipe recipe);
    RecipeEvaluationResult Evaluate(SortingRecipe recipe, RecipeEvaluationContext context);
}

public interface IWorkflowConfigurationResolver
{
    Task<WorkflowResolutionResult> ResolveForWatchedFolderAsync(
        WatchedFolderConfiguration configuration,
        CancellationToken cancellationToken);

    Task<WorkflowResolutionResult> ResolveForManualScanAsync(
        string profileId,
        WorkflowProfileOverride? oneTimeOverride,
        CancellationToken cancellationToken);
}

public interface IWorkflowImportExportService
{
    Task<string> ExportProfileAsync(string profileId, CancellationToken cancellationToken);
    Task<string> ExportRecipeAsync(string recipeId, CancellationToken cancellationToken);
    Task<WorkflowImportResult> ImportAsync(
        string json,
        WorkflowImportConflictPolicy conflictPolicy,
        CancellationToken cancellationToken);
}

public interface IWorkflowRecipePlanService
{
    Task<WorkflowRecipePlanResult> CreatePlanAsync(
        ResolvedWorkflowConfiguration configuration,
        string organizationRoot,
        string sourceScanId,
        IReadOnlyList<ResultFile> files,
        CancellationToken cancellationToken);
}
