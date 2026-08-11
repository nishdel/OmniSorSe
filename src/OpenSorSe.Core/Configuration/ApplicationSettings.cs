using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Core.Configuration;

/// <summary>
/// Represents the validated settings available to the running application.
/// </summary>
public sealed class ApplicationSettings
{
    /// <summary>
    /// Gets or initializes application-wide feature-presentation settings.
    /// </summary>
    public FeatureSettings Features { get; init; } = new();

    /// <summary>
    /// Gets or initializes the logging settings.
    /// </summary>
    public LoggingSettings Logging { get; init; } = new();

    /// <summary>Gets or initializes the process-session advanced-diagnostics settings.</summary>
    public DiagnosticsSettings Diagnostics { get; init; } = new();

    /// <summary>
    /// Gets or initializes the optional local AI-provider settings.
    /// </summary>
    public AiSettings Ai { get; init; } = new();

    /// <summary>
    /// Gets or initializes settings for the opt-in local results catalog.
    /// </summary>
    public CatalogSettings Catalog { get; init; } = new();

    /// <summary>Gets or initializes bounded local content-extraction settings.</summary>
    public ContentSettings Content { get; init; } = new();

    /// <summary>Gets or initializes local Search settings.</summary>
    public SemanticSearchSettings SemanticSearch { get; init; } = new();

    /// <summary>Gets or initializes durable background-indexing settings.</summary>
    public DeepIndexingSettings DeepIndexing { get; init; } = new();

    /// <summary>
    /// Creates a settings snapshot with only the two shell-wide feature switches changed.
    /// All detailed provider, capability, catalog, and logging values are preserved.
    /// </summary>
    public ApplicationSettings WithShellFeatureSwitches(bool aiEnabled, bool showAdvancedFeatures) => new()
    {
        Features = new FeatureSettings
        {
            ShowAdvancedFeatures = showAdvancedFeatures,
            FilesPageDetailsPanelWidthRatio = Features.FilesPageDetailsPanelWidthRatio,
        },
        Logging = Logging,
        Diagnostics = Diagnostics,
        Ai = new AiSettings
        {
            Enabled = aiEnabled,
            FileRenameSuggestionsEnabled = Ai.FileRenameSuggestionsEnabled,
            FolderStructureSuggestionsEnabled = Ai.FolderStructureSuggestionsEnabled,
            SearchAssistanceEnabled = Ai.SearchAssistanceEnabled,
            RequestDiagnosticsEnabled = Ai.RequestDiagnosticsEnabled,
            ShowUnredactedDiagnosticContent = Ai.ShowUnredactedDiagnosticContent,
            Endpoint = Ai.Endpoint,
            SelectedModel = Ai.SelectedModel,
            RequestTimeoutSeconds = Ai.RequestTimeoutSeconds,
            PreferenceAdaptationEnabled = Ai.PreferenceAdaptationEnabled,
            DocumentTextInterpretationEnabled = Ai.DocumentTextInterpretationEnabled,
        },
        Catalog = Catalog,
        Content = Content,
        SemanticSearch = SemanticSearch,
        DeepIndexing = DeepIndexing,
    };

    /// <summary>
    /// Creates a settings snapshot with only the Files-page details-panel proportion changed.
    /// </summary>
    /// <param name="ratio">The validated proportion of available Files-page width assigned to details.</param>
    /// <returns>A settings snapshot that preserves every unrelated value.</returns>
    public ApplicationSettings WithFilesPageDetailsPanelWidthRatio(double ratio) => new()
    {
        Features = new FeatureSettings
        {
            ShowAdvancedFeatures = Features.ShowAdvancedFeatures,
            FilesPageDetailsPanelWidthRatio = ratio,
        },
        Logging = Logging,
        Diagnostics = Diagnostics,
        Ai = Ai,
        Catalog = Catalog,
        Content = Content,
        SemanticSearch = SemanticSearch,
        DeepIndexing = DeepIndexing,
    };

    /// <summary>
    /// Validates settings before they are made available to the application.
    /// </summary>
    /// <exception cref="ConfigurationValidationException">
    /// Thrown when a required settings group is missing.
    /// </exception>
    public void Validate()
    {
        if (Features is null)
        {
            throw new ConfigurationValidationException("Feature settings are required.");
        }

        Features.Validate();

        if (Logging is null)
        {
            throw new ConfigurationValidationException("Logging settings are required.");
        }

        Logging.Validate();

        if (Diagnostics is null)
        {
            throw new ConfigurationValidationException("Advanced diagnostics settings are required.");
        }

        Diagnostics.Validate();

        if (Ai is null)
        {
            throw new ConfigurationValidationException("AI settings are required.");
        }

        Ai.Validate();

        if (Catalog is null)
        {
            throw new ConfigurationValidationException("Catalog settings are required.");
        }

        Catalog.Validate();

        if (Content is null)
        {
            throw new ConfigurationValidationException("Content extraction settings are required.");
        }

        Content.Validate();

        if (SemanticSearch is null)
        {
            throw new ConfigurationValidationException("Search settings are required.");
        }

        SemanticSearch.Validate();

        if (DeepIndexing is null)
        {
            throw new ConfigurationValidationException("Background indexing settings are required.");
        }

        DeepIndexing.Validate();
    }
}

/// <summary>Defines the master and per-category controls for process-session advanced diagnostics.</summary>
public sealed class DiagnosticsSettings
{
    /// <summary>Gets or initializes whether any detailed diagnostic session may be collected.</summary>
    public bool EnableDiagnostics { get; init; }

    /// <summary>Gets or initializes whether detailed AI sessions may be collected.</summary>
    public bool AiDiagnostics { get; init; }

    /// <summary>Gets or initializes whether detailed OCR and extraction sessions may be collected.</summary>
    public bool OcrAndTextExtractionDiagnostics { get; init; }

    /// <summary>Gets or initializes whether detailed scanning sessions may be collected.</summary>
    public bool ScanningDiagnostics { get; init; }

    /// <summary>Gets or initializes the planned duplicate-detection category preference.</summary>
    public bool DuplicateDetectionDiagnostics { get; init; }

    /// <summary>Gets or initializes the planned search and indexing category preference.</summary>
    public bool SearchAndIndexingDiagnostics { get; init; }

    /// <summary>Gets or initializes the planned rules and organisation category preference.</summary>
    public bool RulesAndOrganisationDiagnostics { get; init; }

    /// <summary>Gets or initializes the planned file-operation category preference.</summary>
    public bool FileOperationDiagnostics { get; init; }

    /// <summary>Gets or initializes the planned performance category preference.</summary>
    public bool PerformanceDiagnostics { get; init; }

    /// <summary>Gets or initializes whether classified content may be retained without ordinary redaction.</summary>
    public bool ShowUnredactedDiagnosticContent { get; init; }

    /// <summary>Gets whether both the master switch and one category switch are enabled.</summary>
    /// <param name="category">The category to evaluate.</param>
    /// <returns>True only when detailed collection is authorized for the category.</returns>
    public bool IsCategoryEnabled(DiagnosticCategory category) =>
        EnableDiagnostics &&
        DiagnosticCategoryRegistry.Get(category).IsInstrumented &&
        category switch
        {
            DiagnosticCategory.Ai => AiDiagnostics,
            DiagnosticCategory.OcrAndTextExtraction => OcrAndTextExtractionDiagnostics,
            DiagnosticCategory.Scanning => ScanningDiagnostics,
            DiagnosticCategory.DuplicateDetection => DuplicateDetectionDiagnostics,
            DiagnosticCategory.SearchAndIndexing => SearchAndIndexingDiagnostics,
            DiagnosticCategory.RulesAndOrganisation => RulesAndOrganisationDiagnostics,
            DiagnosticCategory.FileOperations => FileOperationDiagnostics,
            DiagnosticCategory.Performance => PerformanceDiagnostics,
            _ => false,
        };

    /// <summary>Validates future-compatible settings.</summary>
    public void Validate()
    {
    }
}

/// <summary>Defines bounded local deterministic Search behavior.</summary>
public sealed class SemanticSearchSettings
{
    /// <summary>Gets or initializes whether local semantic indexing and navigation are enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets or initializes the maximum indexed-document count.</summary>
    public int MaximumDocumentCount { get; init; } = 10_000;

    /// <summary>Gets or initializes the maximum displayed result count.</summary>
    public int MaximumResultCount { get; init; } = 200;

    /// <summary>Validates local semantic-search bounds.</summary>
    public void Validate()
    {
        if (MaximumDocumentCount is < 1 or > 100_000 ||
            MaximumResultCount is < 1 or > 1_000)
        {
            throw new ConfigurationValidationException("Search settings are invalid.");
        }
    }
}

/// <summary>Identifies the amount of local analysis requested for an indexing source.</summary>
public enum IndexingLevel
{
    /// <summary>Indexes file and folder names, paths, timestamps, sizes, and stable fingerprints.</summary>
    Basic,

    /// <summary>Adds bounded native text, keywords, and one document-level search representation.</summary>
    Standard,

    /// <summary>Adds applicable OCR, richer derived data, bounded chunks, and relationship analysis.</summary>
    Deep,
}

/// <summary>Identifies the resource profile used by durable background indexing.</summary>
public enum IndexingResourceMode
{
    /// <summary>Minimizes background resource use and responsiveness impact.</summary>
    Eco,

    /// <summary>Balances indexing throughput with interactive responsiveness.</summary>
    Balanced,

    /// <summary>Uses the configured concurrency limit for maximum throughput.</summary>
    Fast,
}

/// <summary>Defines conservative, bounded settings for durable background indexing.</summary>
public sealed class DeepIndexingSettings
{
    /// <summary>Gets or initializes whether durable background indexing is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets or initializes the default analysis level for newly registered sources.</summary>
    public IndexingLevel DefaultLevel { get; init; } = IndexingLevel.Basic;

    /// <summary>Gets or initializes the background resource profile.</summary>
    public IndexingResourceMode ResourceMode { get; init; } = IndexingResourceMode.Balanced;

    /// <summary>Gets or initializes the maximum durable index size in mebibytes.</summary>
    public int MaximumIndexSizeMiB { get; init; } = 1024;

    /// <summary>Gets or initializes the maximum extracted-text characters retained for one file.</summary>
    public int MaximumExtractedTextCharacters { get; init; } = 131_072;

    /// <summary>Gets or initializes the maximum OCR characters retained for one file.</summary>
    public int MaximumOcrTextCharacters { get; init; } = 65_536;

    /// <summary>Gets or initializes the maximum selected semantic chunks retained for one document.</summary>
    public int MaximumSemanticChunksPerDocument { get; init; } = 8;

    /// <summary>Gets or initializes deleted-file record retention in days.</summary>
    public int DeletedFileRetentionDays { get; init; } = 30;

    /// <summary>Gets or initializes failed-job history retention in days.</summary>
    public int FailedJobHistoryRetentionDays { get; init; } = 14;

    /// <summary>Gets or initializes the maximum attempts for a retryable stage.</summary>
    public int MaximumRetryCount { get; init; } = 3;

    /// <summary>Gets or initializes the maximum number of simultaneous stage workers.</summary>
    public int MaximumConcurrency { get; init; } = 1;

    /// <summary>Gets or initializes whether processing is restricted to user-idle periods when supported.</summary>
    public bool ProcessOnlyWhileIdle { get; init; }

    /// <summary>Gets or initializes whether processing is restricted to external power when supported.</summary>
    public bool ProcessOnlyWhileConnectedToPower { get; init; }

    /// <summary>Gets or initializes the optional battery percentage below which processing pauses.</summary>
    public int? PauseBelowBatteryPercentage { get; init; }

    /// <summary>Gets or initializes whether OCR stages may run.</summary>
    public bool OcrProcessingEnabled { get; init; }

    /// <summary>Gets or initializes whether optional local-AI enrichment stages may run.</summary>
    public bool AiProcessingEnabled { get; init; }

    /// <summary>Gets or initializes whether generated summaries and keywords may be retained.</summary>
    public bool SummaryProcessingEnabled { get; init; } = true;

    /// <summary>Gets or initializes whether related-concept data and selected chunks may be retained.</summary>
    public bool SemanticProcessingEnabled { get; init; } = true;

    /// <summary>Gets or initializes whether evidence-backed file relationship analysis may run.</summary>
    public bool RelationshipAnalysisEnabled { get; init; } = true;

    /// <summary>Gets or initializes extensions excluded from relationship analysis while retaining ordinary Search indexing.</summary>
    public IReadOnlyList<string> RelationshipExcludedExtensions { get; init; } = [];

    /// <summary>Gets or initializes the maximum indexed candidates examined for one incremental relationship analysis.</summary>
    public int MaximumRelationshipCandidates { get; init; } = 256;

    /// <summary>Gets or initializes the maximum automatic relationships retained for one file.</summary>
    public int MaximumRelationshipsPerFile { get; init; } = 64;

    /// <summary>Gets or initializes the maximum members retained in one automatic Smart Collection.</summary>
    public int MaximumSmartCollectionMembers { get; init; } = 1_000;

    /// <summary>Gets or initializes whether archive contents may be indexed.</summary>
    public bool ArchiveIndexingEnabled { get; init; }

    /// <summary>Gets or initializes whether known generated folders are excluded.</summary>
    public bool ExcludeGeneratedFolders { get; init; } = true;

    /// <summary>Gets or initializes whether binaries and executables default to metadata-only indexing.</summary>
    public bool BinaryAndExecutableMetadataOnly { get; init; } = true;

    /// <summary>Gets or initializes an optional inclusive processing-window start hour in local time.</summary>
    public int? ProcessingWindowStartHour { get; init; }

    /// <summary>Gets or initializes an optional exclusive processing-window end hour in local time.</summary>
    public int? ProcessingWindowEndHour { get; init; }

    /// <summary>Validates resource, retention, and storage bounds.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(DefaultLevel) ||
            !Enum.IsDefined(ResourceMode) ||
            MaximumIndexSizeMiB is < 16 or > 1_048_576 ||
            MaximumExtractedTextCharacters is < 4096 or > 4_194_304 ||
            MaximumOcrTextCharacters is < 4096 or > 1_048_576 ||
            MaximumSemanticChunksPerDocument is < 1 or > 256 ||
            DeletedFileRetentionDays is < 0 or > 3650 ||
            FailedJobHistoryRetentionDays is < 1 or > 3650 ||
            MaximumRetryCount is < 0 or > 20 ||
            MaximumConcurrency is < 1 or > 32 ||
            MaximumRelationshipCandidates is < 16 or > 512 ||
            MaximumRelationshipsPerFile is < 1 or > 128 ||
            MaximumSmartCollectionMembers is < 2 or > 2000 ||
            RelationshipExcludedExtensions is null ||
            RelationshipExcludedExtensions.Count > 128 ||
            RelationshipExcludedExtensions.Any(extension =>
                string.IsNullOrWhiteSpace(extension) ||
                extension.Length > 32 ||
                extension.Trim().TrimStart('.').Length == 0 ||
                extension.Trim().TrimStart('.').Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')) ||
            PauseBelowBatteryPercentage is < 1 or > 100 ||
            ProcessingWindowStartHour is < 0 or > 23 ||
            ProcessingWindowEndHour is < 0 or > 23 ||
            ProcessingWindowStartHour.HasValue != ProcessingWindowEndHour.HasValue ||
            ProcessingWindowStartHour.HasValue &&
            ProcessingWindowStartHour == ProcessingWindowEndHour)
        {
            throw new ConfigurationValidationException("Background indexing settings are invalid.");
        }
    }
}

/// <summary>Defines bounded local metadata extraction and OCR Beta behavior.</summary>
public sealed class ContentSettings
{
    /// <summary>Gets or initializes whether defensive local metadata and native-text extraction runs after scanning.</summary>
    public bool MetadataExtractionEnabled { get; init; } = true;

    /// <summary>Gets or initializes whether local OCR Beta may run.</summary>
    public bool OcrEnabled { get; init; }

    /// <summary>Gets or initializes whether OCR is skipped when reliable native text is available.</summary>
    public bool OcrOnlyWhenNativeTextUnavailable { get; init; } = true;

    /// <summary>Gets or initializes the maximum supported document pages.</summary>
    public int MaximumPagesPerDocument { get; init; } = 25;

    /// <summary>Gets or initializes the maximum input size in mebibytes.</summary>
    public int MaximumFileSizeMiB { get; init; } = 50;

    /// <summary>Gets or initializes the local OCR language identifier.</summary>
    public string OcrLanguage { get; init; } = "eng";

    /// <summary>Gets or initializes the maximum OCR duration per file.</summary>
    public int MaximumOcrDurationSeconds { get; init; } = 120;

    /// <summary>Gets or initializes PDF rasterization resolution used for local OCR.</summary>
    public int PdfRasterizationDpi { get; init; } = 240;

    /// <summary>Gets or initializes the maximum width or height of a rendered OCR page.</summary>
    public int MaximumRasterDimension { get; init; } = 4096;

    /// <summary>Gets or initializes the maximum combined OCR text retained per document.</summary>
    public int MaximumOcrTextCharacters { get; init; } = 65_536;

    /// <summary>Gets or initializes the temporary page-image budget per document in mebibytes.</summary>
    public int MaximumTemporaryStorageMiB { get; init; } = 256;

    /// <summary>Gets or initializes an optional absolute Tesseract executable path; null uses PATH.</summary>
    public string? TesseractExecutablePath { get; init; }

    /// <summary>Gets or initializes whether content extraction may continue outside the immediate scan stage.</summary>
    public bool BackgroundProcessingEnabled { get; init; }

    /// <summary>Validates bounded content settings.</summary>
    public void Validate()
    {
        if (MaximumPagesPerDocument is < 1 or > 500 ||
            MaximumFileSizeMiB is < 1 or > 1024 ||
            MaximumOcrDurationSeconds is < 5 or > 600 ||
            PdfRasterizationDpi is < 150 or > 300 ||
            MaximumRasterDimension is < 1024 or > 8192 ||
            MaximumOcrTextCharacters is < 4096 or > 262_144 ||
            MaximumTemporaryStorageMiB is < 16 or > 2048 ||
            string.IsNullOrWhiteSpace(OcrLanguage) ||
            OcrLanguage.Length > 32 ||
            OcrLanguage.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '+') ||
            OcrLanguage.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(language => language is not ("eng" or "deu")) ||
            OcrLanguage.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length == 0 ||
            TesseractExecutablePath is not null &&
            (string.IsNullOrWhiteSpace(TesseractExecutablePath) ||
             !Path.IsPathRooted(TesseractExecutablePath) ||
             TesseractExecutablePath.Length > 1024))
        {
            throw new ConfigurationValidationException("Content extraction settings are invalid.");
        }
    }
}

/// <summary>
/// Defines application-wide interface-complexity choices independently from AI enablement.
/// </summary>
public sealed class FeatureSettings
{
    /// <summary>Gets the default proportion of Files-page width assigned to selected-file details.</summary>
    public const double DefaultFilesPageDetailsPanelWidthRatio = 0.32;

    /// <summary>Gets the smallest supported Files-page details-panel proportion.</summary>
    public const double MinimumFilesPageDetailsPanelWidthRatio = 0.20;

    /// <summary>Gets the largest supported Files-page details-panel proportion.</summary>
    public const double MaximumFilesPageDetailsPanelWidthRatio = 0.50;

    /// <summary>Gets or initializes whether specialist and troubleshooting features are shown.</summary>
    public bool ShowAdvancedFeatures { get; init; }

    /// <summary>
    /// Gets or initializes the proportion of available Files-page width assigned to selected-file details.
    /// </summary>
    public double FilesPageDetailsPanelWidthRatio { get; init; } = DefaultFilesPageDetailsPanelWidthRatio;

    /// <summary>Validates bounded interface-presentation settings.</summary>
    public void Validate()
    {
        if (!double.IsFinite(FilesPageDetailsPanelWidthRatio) ||
            FilesPageDetailsPanelWidthRatio is < MinimumFilesPageDetailsPanelWidthRatio or > MaximumFilesPageDetailsPanelWidthRatio)
        {
            throw new ConfigurationValidationException(
                $"Files-page details width must be between {MinimumFilesPageDetailsPanelWidthRatio:P0} and {MaximumFilesPageDetailsPanelWidthRatio:P0}.");
        }
    }
}

/// <summary>
/// Identifies an independently enabled AI capability.
/// </summary>
public enum AiCapability
{
    /// <summary>Review-only suggestions for one known file name.</summary>
    FileRenameSuggestions,
    /// <summary>Review-only logical folder hierarchies for known file metadata.</summary>
    FolderStructureSuggestions,
    /// <summary>Review-only interpretation of bounded locally extracted document text.</summary>
    DocumentTextInterpretation,
    /// <summary>Optional reranking of a bounded set of deterministic local Search results.</summary>
    SearchAssistance,
}

/// <summary>
/// Defines user control over the bounded, application-owned local results catalog.
/// </summary>
public sealed class CatalogSettings
{
    /// <summary>
    /// Gets or initializes whether completed display-safe scan snapshots may be stored in OpenSorSe application data.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Validates catalog settings reserved for future compatible expansion.
    /// </summary>
    public void Validate()
    {
    }
}

/// <summary>
/// Defines the optional, user-controlled local Ollama integration settings.
/// </summary>
public sealed class AiSettings
{
    /// <summary>Gets the maximum supported model identifier length.</summary>
    public const int MaximumModelIdentifierLength = 256;

    /// <summary>Gets the minimum supported finite request timeout.</summary>
    public const int MinimumRequestTimeoutSeconds = 5;

    /// <summary>Gets the maximum supported finite request timeout.</summary>
    public const int MaximumRequestTimeoutSeconds = 300;

    /// <summary>
    /// Gets or initializes whether AI suggestion requests are permitted.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Gets or initializes whether review-only file-rename suggestions are enabled.</summary>
    public bool FileRenameSuggestionsEnabled { get; init; }

    /// <summary>Gets or initializes whether review-only folder-structure suggestions are enabled.</summary>
    public bool FolderStructureSuggestionsEnabled { get; init; }

    /// <summary>
    /// Gets or initializes whether an explicit Search request may send its query and a bounded
    /// candidate summary to the configured Ollama-compatible endpoint for supplemental reranking.
    /// </summary>
    public bool SearchAssistanceEnabled { get; init; }

    /// <summary>Gets or initializes opt-in, session-only raw AI request diagnostics.</summary>
    public bool RequestDiagnosticsEnabled { get; init; }

    /// <summary>Gets or initializes whether opt-in diagnostics retain exact prompt and response display content.</summary>
    public bool ShowUnredactedDiagnosticContent { get; init; }

    /// <summary>
    /// Gets or initializes the Ollama-compatible endpoint. The default is the local Ollama endpoint.
    /// </summary>
    public string Endpoint { get; init; } = "http://127.0.0.1:11434";

    /// <summary>
    /// Gets or initializes the locally discovered model selected for suggestion requests.
    /// </summary>
    public string? SelectedModel { get; init; }

    /// <summary>
    /// Gets or initializes the bounded duration permitted for one AI request.
    /// </summary>
    public int RequestTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Gets or initializes whether locally recorded approved patterns may be supplied as concise request context.
    /// </summary>
    public bool PreferenceAdaptationEnabled { get; init; } = true;

    /// <summary>Gets or initializes whether explicit requests may send bounded extracted text to the configured provider.</summary>
    public bool DocumentTextInterpretationEnabled { get; init; }

    /// <summary>
    /// Gets whether the master switch and the specified capability switch are both enabled.
    /// </summary>
    /// <param name="capability">The capability to evaluate.</param>
    /// <returns><see langword="true"/> only when the capability may be considered for use.</returns>
    public bool IsCapabilityEnabled(AiCapability capability) => Enabled && capability switch
    {
        AiCapability.FileRenameSuggestions => FileRenameSuggestionsEnabled,
        AiCapability.FolderStructureSuggestions => FolderStructureSuggestionsEnabled,
        AiCapability.DocumentTextInterpretation => DocumentTextInterpretationEnabled,
        AiCapability.SearchAssistance => SearchAssistanceEnabled,
        _ => false,
    };

    /// <summary>
    /// Validates the supported local AI configuration values.
    /// </summary>
    /// <exception cref="ConfigurationValidationException">Thrown when the settings are unsafe or unsupported.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Endpoint) ||
            !Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(endpoint.Host) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            RequestTimeoutSeconds is < MinimumRequestTimeoutSeconds or > MaximumRequestTimeoutSeconds ||
            SelectedModel is { } model &&
            (model.Length > MaximumModelIdentifierLength || model.Any(char.IsControl)))
        {
            throw new ConfigurationValidationException($"AI settings are invalid. Request timeout must be between {MinimumRequestTimeoutSeconds} and {MaximumRequestTimeoutSeconds} seconds.");
        }
    }
}

/// <summary>
/// Defines logging-related application settings.
/// </summary>
public sealed class LoggingSettings
{
    /// <summary>
    /// Gets or initializes the lowest severity that is written to configured log outputs.
    /// </summary>
    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    /// <summary>
    /// Gets or initializes whether local daily text logging is enabled.
    /// </summary>
    public bool FileLoggingEnabled { get; init; } = true;

    /// <summary>
    /// Gets or initializes the optional absolute directory for local daily log files.
    /// </summary>
    public string? LogDirectoryPath { get; init; }

    /// <summary>
    /// Gets or initializes the number of daily log files retained locally.
    /// </summary>
    public int RetainedFileCount { get; init; } = 7;

    /// <summary>
    /// Validates logging-specific settings.
    /// </summary>
    /// <exception cref="ConfigurationValidationException">Thrown when a logging setting is invalid.</exception>
    public void Validate()
    {
        if (!Enum.IsDefined(MinimumLevel) || RetainedFileCount < 1 ||
            LogDirectoryPath is not null &&
            (string.IsNullOrWhiteSpace(LogDirectoryPath) ||
             !Path.IsPathRooted(LogDirectoryPath)))
        {
            throw new ConfigurationValidationException("Logging settings are invalid.");
        }
    }
}
