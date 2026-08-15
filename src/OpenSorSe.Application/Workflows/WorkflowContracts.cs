#pragma warning disable CS1591

using OpenSorSe.Application.Models;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Plugins;
using OpenSorSe.Core.Platform;
using OpenSorSe.Application.Watching;
using OpenSorSe.Executor;
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
    public const int MaximumOrganizationSelection = ChangePlanSchema.MaximumActions;
    public const int MaximumOrganizationPreviewRows = 100;
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
    public const string TrustedClassificationRecipe = "builtin:recipe:trusted-classification";
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
    WorkflowScanBehavior FullScan)
{
    public IReadOnlyList<PluginContributionReference> PluginContributions { get; init; } = [];
}

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
    IReadOnlyList<RecipePreviewExample> PreviewExamples)
{
    public IReadOnlyList<PluginContributionReference> PluginFieldContributions { get; init; } = [];

    public FileNamePortabilityMode FileNamePortability { get; init; } =
        FileNamePortabilityMode.Portable;
}

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
    DateTimeOffset ResolvedAtUtc)
{
    public IReadOnlyList<ResolvedPluginContributionSnapshot> PluginContributions { get; init; } = [];
}

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
    bool IsAiDerived = false)
{
    public string? PluginId { get; init; }
    public string? PluginVersion { get; init; }
    public string? ContributionId { get; init; }
    public string? Reason { get; init; }
    public string? Evidence { get; init; }
    public double? Confidence { get; init; }
}

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

/// <summary>Describes whether one deterministic organization proposal is ready for Change Plan review.</summary>
public enum OrganizationProposalReadiness
{
    /// <summary>Every required trusted value resolved without a warning or conflict.</summary>
    Reliable,
    /// <summary>An explicit fallback, sanitization, or other non-blocking warning requires attention.</summary>
    NeedsReview,
    /// <summary>The recipe cannot safely produce an operation for this file.</summary>
    CannotPropose,
}

/// <summary>Maps one recipe token to bounded local evidence without retaining extracted content.</summary>
public sealed record OrganizationEvidenceMapping(
    string Token,
    string Value,
    string EvidenceSource,
    bool IsSensitive);

/// <summary>Contains one ephemeral, non-executable organization preview row.</summary>
public sealed record OrganizationProposalRow(
    string FileId,
    string CurrentPath,
    string? ProposedFileName,
    string? ProposedRelativeDestination,
    string? TargetPath,
    OrganizationProposalReadiness Readiness,
    IReadOnlyList<OrganizationEvidenceMapping> Evidence,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> Fallbacks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Conflicts,
    long? SourceLength,
    DateTimeOffset? SourceModifiedAtUtc)
{
    /// <summary>Gets whether this row can become an existing Change Plan action.</summary>
    public bool IsEligible =>
        Readiness != OrganizationProposalReadiness.CannotPropose &&
        !string.IsNullOrWhiteSpace(TargetPath) &&
        !string.Equals(CurrentPath, TargetPath, ChangePlanFactory.PathComparison);
}

/// <summary>Reports literal trusted-token availability over an explicit selection.</summary>
public sealed record OrganizationEvidenceCoverage(
    string Token,
    string DisplayName,
    int AvailableCount,
    int SelectedCount);

/// <summary>
/// Contains one bounded, ephemeral organization preview. It is not executable state and is never persisted.
/// </summary>
public sealed record OrganizationProposalSet(
    string ProposalId,
    SortingRecipe Recipe,
    string OrganizationRoot,
    string SourceId,
    IReadOnlyList<string> SelectedFileIds,
    IReadOnlyList<OrganizationProposalRow> Rows,
    IReadOnlyList<OrganizationEvidenceCoverage> Coverage,
    int ProjectedFileActionCount,
    int ProjectedDirectoryActionCount,
    IReadOnlyList<string> Warnings,
    bool HasSensitivePathEvidence,
    string Fingerprint)
{
    /// <summary>Gets the total existing Change Plan actions represented by the preview.</summary>
    public int ProjectedActionCount => checked(ProjectedFileActionCount + ProjectedDirectoryActionCount);

    /// <summary>Gets whether every selected file has an eligible reviewed proposal within the action bound.</summary>
    public bool CanCreateChangePlan =>
        Rows.Count == SelectedFileIds.Count &&
        Rows.Count > 0 &&
        Rows.All(row => row.IsEligible) &&
        ProjectedActionCount <= ChangePlanSchema.MaximumActions;
}

/// <summary>Requests a deterministic preview over one explicit stable-ID snapshot.</summary>
public sealed record OrganizationPreviewRequest(
    SortingRecipe Recipe,
    IReadOnlyList<string> SelectedFileIds);

/// <summary>Describes one closed, non-executable product-facing organization token.</summary>
public sealed record OrganizationRecipeToken(
    string Token,
    string DisplayName,
    string Description,
    bool SupportsDateFormat = false,
    bool IsSensitive = false);

/// <summary>Owns the modern closed token picker; legacy fields remain parser-compatible but are not promoted.</summary>
public static class OrganizationRecipeTokenCatalog
{
    public static IReadOnlyList<OrganizationRecipeToken> Tokens { get; } = Array.AsReadOnly(
    new OrganizationRecipeToken[]
    {
        new("{originalName}", "Original name", "The current filename without its extension."),
        new("{theme}", "Theme", "One accepted or uniquely usable Strong deterministic Theme.", IsSensitive: true),
        new("{documentType}", "Document Type", "One accepted or uniquely usable Strong deterministic Document Type.", IsSensitive: true),
        new("{filesystemCreatedDate:yyyy-MM-dd}", "Filesystem created date", "The filesystem-created timestamp; this is not a document or capture date.", SupportsDateFormat: true),
        new("{filesystemModifiedDate:yyyy-MM-dd}", "Filesystem modified date", "The filesystem-modified timestamp; this is not a document date.", SupportsDateFormat: true),
        new("{category}", "File category", "The normalized coarse file category."),
    });
}

public sealed class WorkflowProfileUnavailableException : InvalidOperationException
{
    public WorkflowProfileUnavailableException(string message)
        : base(message)
    {
    }
}

/// <summary>Persists the bounded versioned user workflow library atomically.</summary>
public interface IWorkflowLibraryStore
{
    Task<WorkflowLibraryLoadResult> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(
        IReadOnlyList<WorkflowProfile> profiles,
        IReadOnlyList<SortingRecipe> recipes,
        CancellationToken cancellationToken);
}

/// <summary>Validates complete profiles and recipes independently of presentation input checks.</summary>
public interface IWorkflowValidator
{
    WorkflowValidationResult ValidateProfile(
        WorkflowProfile profile,
        IReadOnlyList<SortingRecipe> availableRecipes);

    WorkflowValidationResult ValidateRecipe(SortingRecipe recipe);
}

/// <summary>Finds known assignments and snapshots that protect workflow items from unsafe deletion.</summary>
public interface IWorkflowUsageInspector
{
    Task<WorkflowUsageInfo> InspectAsync(string itemId, CancellationToken cancellationToken);
}

/// <summary>Owns built-in and user Workflow Profile/Sorting Recipe lifecycle.</summary>
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

/// <summary>Validates and evaluates the constrained non-executable recipe template language.</summary>
public interface IWorkflowTemplateEngine
{
    WorkflowValidationResult ValidateRecipeTemplates(SortingRecipe recipe);
    RecipeEvaluationResult Evaluate(SortingRecipe recipe, RecipeEvaluationContext context);
}

/// <summary>Creates immutable fail-closed effective configurations for manual and watched scans.</summary>
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

/// <summary>Transfers bounded versioned workflow JSON through host validation and conflict policy.</summary>
public interface IWorkflowImportExportService
{
    Task<string> ExportProfileAsync(string profileId, CancellationToken cancellationToken);
    Task<string> ExportRecipeAsync(string recipeId, CancellationToken cancellationToken);
    Task<WorkflowImportResult> ImportAsync(
        string json,
        WorkflowImportConflictPolicy conflictPolicy,
        CancellationToken cancellationToken);
}

/// <summary>Converts evaluated recipe proposals into pending provenance-rich Change Plans.</summary>
public interface IWorkflowRecipePlanService
{
    Task<WorkflowRecipePlanResult> CreatePlanAsync(
        ResolvedWorkflowConfiguration configuration,
        string organizationRoot,
        string sourceScanId,
        IReadOnlyList<ResultFile> files,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves trusted indexed evidence into ephemeral recipe previews and converts only a fresh approved preview
/// into the existing Change Plan safety boundary.
/// </summary>
public interface IReviewedOrganizationService
{
    Task<OrganizationProposalSet> PreviewAsync(
        OrganizationPreviewRequest request,
        CancellationToken cancellationToken);

    Task<ChangePlan> CreateChangePlanAsync(
        OrganizationProposalSet proposal,
        string sourceContextId,
        CancellationToken cancellationToken);
}

/// <summary>Provides only the bounded durable evidence needed by reviewed organization.</summary>
public interface IReviewedOrganizationEvidenceSource
{
    Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
        IReadOnlyList<string> fileIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken);
}
