using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Configuration;
using System.Globalization;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Holds editable application configuration independently from the persisted settings object.
/// </summary>
public sealed class SettingsDraft : ViewModelBase
{
    private bool _fileLoggingEnabled;
    private string? _logDirectoryPath;
    private LogLevel _minimumLogLevel;
    private int _retainedFileCount;
    private bool _showAdvancedFeatures;
    private bool _diagnosticsEnabled;
    private bool _aiDiagnosticsEnabled;
    private bool _ocrAndTextExtractionDiagnosticsEnabled;
    private bool _scanningDiagnosticsEnabled;
    private bool _duplicateDetectionDiagnosticsEnabled;
    private bool _searchAndIndexingDiagnosticsEnabled;
    private bool _rulesAndOrganisationDiagnosticsEnabled;
    private bool _fileOperationDiagnosticsEnabled;
    private bool _performanceDiagnosticsEnabled;
    private bool _showUnredactedDiagnosticContent;
    private double _filesPageDetailsPanelWidthRatio = FeatureSettings.DefaultFilesPageDetailsPanelWidthRatio;
    private bool _aiEnabled;
    private bool _fileRenameSuggestionsEnabled;
    private bool _folderStructureSuggestionsEnabled;
    private string _aiEndpoint = "http://127.0.0.1:11434";
    private string? _selectedAiModel;
    private int _aiRequestTimeoutSeconds = 30;
    private string _aiRequestTimeoutText = "30";
    private bool _preferenceAdaptationEnabled = true;
    private bool _documentTextInterpretationEnabled;
    private bool _catalogEnabled;
    private bool _metadataExtractionEnabled = true;
    private bool _ocrEnabled;
    private bool _ocrOnlyWhenNativeTextUnavailable = true;
    private int _maximumOcrPages = 25;
    private int _maximumContentFileSizeMiB = 50;
    private string _ocrLanguage = "eng";
    private int _maximumOcrDurationSeconds = 120;
    private int _pdfRasterizationDpi = 240;
    private int _maximumRasterDimension = 4096;
    private int _maximumOcrTextCharacters = 65_536;
    private int _maximumTemporaryStorageMiB = 256;
    private string? _tesseractExecutablePath;
    private bool _backgroundContentProcessingEnabled;
    private bool _semanticSearchEnabled;
    private int _maximumSemanticDocuments = 10_000;
    private int _maximumSemanticResults = 200;
    private bool _deepIndexingEnabled = true;
    private IndexingLevel _defaultIndexingLevel = IndexingLevel.Basic;
    private IndexingResourceMode _indexingResourceMode = IndexingResourceMode.Balanced;
    private int _maximumIndexSizeMiB = 1024;
    private int _maximumExtractedTextCharacters = 131_072;
    private int _maximumDeepOcrTextCharacters = 65_536;
    private int _maximumSemanticChunksPerDocument = 8;
    private int _deletedFileRetentionDays = 30;
    private int _failedJobHistoryRetentionDays = 14;
    private int _maximumIndexingRetryCount = 3;
    private int _maximumIndexingConcurrency = 1;
    private bool _processIndexOnlyWhileIdle;
    private bool _processIndexOnlyOnPower;
    private bool _pauseIndexingOnLowBattery;
    private int _pauseBelowBatteryPercentage = 20;
    private bool _deepOcrProcessingEnabled;
    private bool _deepAiProcessingEnabled;
    private bool _deepSummaryProcessingEnabled = true;
    private bool _deepSemanticProcessingEnabled = true;
    private bool _archiveIndexingEnabled;
    private bool _excludeGeneratedFolders = true;
    private bool _binaryAndExecutableMetadataOnly = true;
    private bool _useIndexingTimeWindow;
    private int _indexingWindowStartHour = 22;
    private int _indexingWindowEndHour = 7;
    private bool _relationshipAnalysisEnabled = true;
    private string _relationshipExcludedExtensions = string.Empty;
    private int _maximumRelationshipCandidates = 256;
    private int _maximumRelationshipsPerFile = 64;
    private int _maximumSmartCollectionMembers = 1000;

    /// <summary>Gets or sets whether specialist and troubleshooting interface features are shown.</summary>
    public bool ShowAdvancedFeatures
    {
        get => _showAdvancedFeatures;
        set => SetProperty(ref _showAdvancedFeatures, value);
    }

    /// <summary>Gets or sets the master detailed-diagnostics switch.</summary>
    public bool DiagnosticsEnabled
    {
        get => _diagnosticsEnabled;
        set => SetProperty(ref _diagnosticsEnabled, value);
    }

    /// <summary>Gets or sets whether detailed AI sessions are collected.</summary>
    public bool AiDiagnosticsEnabled
    {
        get => _aiDiagnosticsEnabled;
        set
        {
            if (SetProperty(ref _aiDiagnosticsEnabled, value))
            {
                OnPropertyChanged(nameof(AiRequestDiagnosticsEnabled));
            }
        }
    }

    /// <summary>Gets or sets whether detailed OCR and text-extraction sessions are collected.</summary>
    public bool OcrAndTextExtractionDiagnosticsEnabled
    {
        get => _ocrAndTextExtractionDiagnosticsEnabled;
        set => SetProperty(ref _ocrAndTextExtractionDiagnosticsEnabled, value);
    }

    /// <summary>Gets or sets whether detailed scan sessions are collected.</summary>
    public bool ScanningDiagnosticsEnabled
    {
        get => _scanningDiagnosticsEnabled;
        set => SetProperty(ref _scanningDiagnosticsEnabled, value);
    }

    /// <summary>Gets or sets the planned duplicate-detection diagnostic preference.</summary>
    public bool DuplicateDetectionDiagnosticsEnabled
    {
        get => _duplicateDetectionDiagnosticsEnabled;
        set => SetProperty(ref _duplicateDetectionDiagnosticsEnabled, value);
    }

    /// <summary>Gets or sets the planned search-and-indexing diagnostic preference.</summary>
    public bool SearchAndIndexingDiagnosticsEnabled
    {
        get => _searchAndIndexingDiagnosticsEnabled;
        set => SetProperty(ref _searchAndIndexingDiagnosticsEnabled, value);
    }

    /// <summary>Gets or sets the planned rules-and-organisation diagnostic preference.</summary>
    public bool RulesAndOrganisationDiagnosticsEnabled
    {
        get => _rulesAndOrganisationDiagnosticsEnabled;
        set => SetProperty(ref _rulesAndOrganisationDiagnosticsEnabled, value);
    }

    /// <summary>Gets or sets the planned file-operation diagnostic preference.</summary>
    public bool FileOperationDiagnosticsEnabled
    {
        get => _fileOperationDiagnosticsEnabled;
        set => SetProperty(ref _fileOperationDiagnosticsEnabled, value);
    }

    /// <summary>Gets or sets the planned performance diagnostic preference.</summary>
    public bool PerformanceDiagnosticsEnabled
    {
        get => _performanceDiagnosticsEnabled;
        set => SetProperty(ref _performanceDiagnosticsEnabled, value);
    }

    /// <summary>Gets or sets whether classified diagnostic content is retained without ordinary redaction.</summary>
    public bool ShowUnredactedDiagnosticContent
    {
        get => _showUnredactedDiagnosticContent;
        set
        {
            if (SetProperty(ref _showUnredactedDiagnosticContent, value))
            {
                OnPropertyChanged(nameof(ShowUnredactedAiDiagnosticContent));
            }
        }
    }

    /// <summary>Gets or sets the persisted Files-page details-panel proportion.</summary>
    public double FilesPageDetailsPanelWidthRatio
    {
        get => _filesPageDetailsPanelWidthRatio;
        set => SetProperty(ref _filesPageDetailsPanelWidthRatio, value);
    }

    /// <summary>
    /// Gets or sets whether local daily file logging is enabled.
    /// </summary>
    public bool FileLoggingEnabled
    {
        get => _fileLoggingEnabled;
        set => SetProperty(ref _fileLoggingEnabled, value);
    }

    /// <summary>
    /// Gets or sets the optional absolute local log directory.
    /// </summary>
    public string? LogDirectoryPath
    {
        get => _logDirectoryPath;
        set => SetProperty(ref _logDirectoryPath, value);
    }

    /// <summary>
    /// Gets or sets the lowest log level retained by configured logging outputs.
    /// </summary>
    public LogLevel MinimumLogLevel
    {
        get => _minimumLogLevel;
        set => SetProperty(ref _minimumLogLevel, value);
    }

    /// <summary>
    /// Gets or sets the number of local daily log files retained.
    /// </summary>
    public int RetainedFileCount
    {
        get => _retainedFileCount;
        set => SetProperty(ref _retainedFileCount, value);
    }

    /// <summary>Gets or sets whether optional local AI suggestions may be requested.</summary>
    public bool AiEnabled
    {
        get => _aiEnabled;
        set => SetProperty(ref _aiEnabled, value);
    }

    /// <summary>Gets or sets whether review-only file-rename suggestions are enabled.</summary>
    public bool FileRenameSuggestionsEnabled
    {
        get => _fileRenameSuggestionsEnabled;
        set => SetProperty(ref _fileRenameSuggestionsEnabled, value);
    }

    /// <summary>Gets or sets whether review-only folder-structure suggestions are enabled.</summary>
    public bool FolderStructureSuggestionsEnabled
    {
        get => _folderStructureSuggestionsEnabled;
        set => SetProperty(ref _folderStructureSuggestionsEnabled, value);
    }

    /// <summary>Gets or sets whether bounded raw AI request diagnostics are retained for this session.</summary>
    public bool AiRequestDiagnosticsEnabled
    {
        get => AiDiagnosticsEnabled;
        set
        {
            if (value)
            {
                DiagnosticsEnabled = true;
            }

            AiDiagnosticsEnabled = value;
        }
    }

    /// <summary>Gets or sets whether the live diagnostics window may retain exact prompt and response text.</summary>
    public bool ShowUnredactedAiDiagnosticContent
    {
        get => ShowUnredactedDiagnosticContent;
        set => ShowUnredactedDiagnosticContent = value;
    }

    /// <summary>Gets or sets the user-configured Ollama-compatible endpoint.</summary>
    public string AiEndpoint
    {
        get => _aiEndpoint;
        set => SetProperty(ref _aiEndpoint, value);
    }

    /// <summary>Gets or sets the model selected from the provider's installed-model list.</summary>
    public string? SelectedAiModel
    {
        get => _selectedAiModel;
        set => SetProperty(ref _selectedAiModel, value);
    }

    /// <summary>Gets or sets the bounded timeout for one optional local AI request.</summary>
    public int AiRequestTimeoutSeconds
    {
        get => _aiRequestTimeoutSeconds;
        set
        {
            if (SetProperty(ref _aiRequestTimeoutSeconds, value))
            {
                AiRequestTimeoutText = value.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Gets or sets timeout entry text so invalid input can be explained predictably.</summary>
    public string AiRequestTimeoutText
    {
        get => _aiRequestTimeoutText;
        set => SetProperty(ref _aiRequestTimeoutText, value ?? string.Empty);
    }

    /// <summary>Gets or sets whether local decision history may influence concise suggestion context.</summary>
    public bool PreferenceAdaptationEnabled
    {
        get => _preferenceAdaptationEnabled;
        set => SetProperty(ref _preferenceAdaptationEnabled, value);
    }

    /// <summary>Gets or sets whether explicit AI requests may include bounded locally extracted text.</summary>
    public bool DocumentTextInterpretationEnabled
    {
        get => _documentTextInterpretationEnabled;
        set => SetProperty(ref _documentTextInterpretationEnabled, value);
    }

    /// <summary>Gets or sets whether OpenSorSe may retain bounded completed scan metadata in its own local application-data catalog.</summary>
    public bool CatalogEnabled
    {
        get => _catalogEnabled;
        set => SetProperty(ref _catalogEnabled, value);
    }

    /// <summary>Gets or sets whether bounded local metadata and native text are extracted.</summary>
    public bool MetadataExtractionEnabled
    {
        get => _metadataExtractionEnabled;
        set => SetProperty(ref _metadataExtractionEnabled, value);
    }

    /// <summary>Gets or sets whether local OCR Beta is enabled.</summary>
    public bool OcrEnabled
    {
        get => _ocrEnabled;
        set => SetProperty(ref _ocrEnabled, value);
    }

    /// <summary>Gets or sets whether reliable native text prevents unnecessary OCR.</summary>
    public bool OcrOnlyWhenNativeTextUnavailable
    {
        get => _ocrOnlyWhenNativeTextUnavailable;
        set => SetProperty(ref _ocrOnlyWhenNativeTextUnavailable, value);
    }

    /// <summary>Gets or sets the maximum pages considered per OCR document.</summary>
    public int MaximumOcrPages
    {
        get => _maximumOcrPages;
        set => SetProperty(ref _maximumOcrPages, value);
    }

    /// <summary>Gets or sets the maximum local content input size in MiB.</summary>
    public int MaximumContentFileSizeMiB
    {
        get => _maximumContentFileSizeMiB;
        set => SetProperty(ref _maximumContentFileSizeMiB, value);
    }

    /// <summary>Gets or sets the local OCR language identifier.</summary>
    public string OcrLanguage
    {
        get => _ocrLanguage;
        set => SetProperty(ref _ocrLanguage, value ?? string.Empty);
    }

    /// <summary>Gets or sets the maximum OCR duration per file.</summary>
    public int MaximumOcrDurationSeconds
    {
        get => _maximumOcrDurationSeconds;
        set => SetProperty(ref _maximumOcrDurationSeconds, value);
    }

    /// <summary>Gets or sets the PDF rasterization resolution.</summary>
    public int PdfRasterizationDpi
    {
        get => _pdfRasterizationDpi;
        set => SetProperty(ref _pdfRasterizationDpi, value);
    }

    /// <summary>Gets or sets the maximum rendered page edge.</summary>
    public int MaximumRasterDimension
    {
        get => _maximumRasterDimension;
        set => SetProperty(ref _maximumRasterDimension, value);
    }

    /// <summary>Gets or sets the maximum retained OCR text per document.</summary>
    public int MaximumOcrTextCharacters
    {
        get => _maximumOcrTextCharacters;
        set => SetProperty(ref _maximumOcrTextCharacters, value);
    }

    /// <summary>Gets or sets the temporary PDF page-image budget in MiB.</summary>
    public int MaximumTemporaryStorageMiB
    {
        get => _maximumTemporaryStorageMiB;
        set => SetProperty(ref _maximumTemporaryStorageMiB, value);
    }

    /// <summary>Gets or sets an optional absolute Tesseract executable path.</summary>
    public string? TesseractExecutablePath
    {
        get => _tesseractExecutablePath;
        set => SetProperty(ref _tesseractExecutablePath, value);
    }

    /// <summary>Gets or sets whether bounded content work may continue in the background.</summary>
    public bool BackgroundContentProcessingEnabled
    {
        get => _backgroundContentProcessingEnabled;
        set => SetProperty(ref _backgroundContentProcessingEnabled, value);
    }

    /// <summary>Gets or sets whether local Search is enabled.</summary>
    public bool SemanticSearchEnabled
    {
        get => _semanticSearchEnabled;
        set => SetProperty(ref _semanticSearchEnabled, value);
    }

    /// <summary>Gets or sets the maximum locally indexed documents.</summary>
    public int MaximumSemanticDocuments
    {
        get => _maximumSemanticDocuments;
        set => SetProperty(ref _maximumSemanticDocuments, value);
    }

    /// <summary>Gets or sets the maximum semantic results displayed.</summary>
    public int MaximumSemanticResults
    {
        get => _maximumSemanticResults;
        set => SetProperty(ref _maximumSemanticResults, value);
    }

    /// <summary>Gets or sets whether durable background indexing is enabled.</summary>
    public bool DeepIndexingEnabled
    {
        get => _deepIndexingEnabled;
        set => SetProperty(ref _deepIndexingEnabled, value);
    }

    /// <summary>Gets or sets the default indexing level for new sources.</summary>
    public IndexingLevel DefaultIndexingLevel
    {
        get => _defaultIndexingLevel;
        set => SetProperty(ref _defaultIndexingLevel, value);
    }

    /// <summary>Gets or sets the background resource mode.</summary>
    public IndexingResourceMode IndexingResourceMode
    {
        get => _indexingResourceMode;
        set => SetProperty(ref _indexingResourceMode, value);
    }

    /// <summary>Gets or sets the maximum index size in MiB.</summary>
    public int MaximumIndexSizeMiB
    {
        get => _maximumIndexSizeMiB;
        set => SetProperty(ref _maximumIndexSizeMiB, value);
    }

    /// <summary>Gets or sets whether evidence-backed relationship analysis may run.</summary>
    public bool RelationshipAnalysisEnabled
    {
        get => _relationshipAnalysisEnabled;
        set => SetProperty(ref _relationshipAnalysisEnabled, value);
    }

    /// <summary>Gets or sets comma-separated file extensions excluded only from relationship analysis.</summary>
    public string RelationshipExcludedExtensions
    {
        get => _relationshipExcludedExtensions;
        set => SetProperty(ref _relationshipExcludedExtensions, value ?? string.Empty);
    }

    /// <summary>Gets or sets the bounded candidate count for one incremental relationship pass.</summary>
    public int MaximumRelationshipCandidates
    {
        get => _maximumRelationshipCandidates;
        set => SetProperty(ref _maximumRelationshipCandidates, value);
    }

    /// <summary>Gets or sets the bounded direct relationships retained per file.</summary>
    public int MaximumRelationshipsPerFile
    {
        get => _maximumRelationshipsPerFile;
        set => SetProperty(ref _maximumRelationshipsPerFile, value);
    }

    /// <summary>Gets or sets the bounded member count retained per Smart Collection.</summary>
    public int MaximumSmartCollectionMembers
    {
        get => _maximumSmartCollectionMembers;
        set => SetProperty(ref _maximumSmartCollectionMembers, value);
    }

    /// <summary>Gets or sets maximum retained extracted text characters per file.</summary>
    public int MaximumExtractedTextCharacters
    {
        get => _maximumExtractedTextCharacters;
        set => SetProperty(ref _maximumExtractedTextCharacters, value);
    }

    /// <summary>Gets or sets maximum retained OCR characters in the durable index.</summary>
    public int MaximumDeepOcrTextCharacters
    {
        get => _maximumDeepOcrTextCharacters;
        set => SetProperty(ref _maximumDeepOcrTextCharacters, value);
    }

    /// <summary>Gets or sets maximum selected chunks retained per Deep document.</summary>
    public int MaximumSemanticChunksPerDocument
    {
        get => _maximumSemanticChunksPerDocument;
        set => SetProperty(ref _maximumSemanticChunksPerDocument, value);
    }

    /// <summary>Gets or sets deleted-file record retention in days.</summary>
    public int DeletedFileRetentionDays
    {
        get => _deletedFileRetentionDays;
        set => SetProperty(ref _deletedFileRetentionDays, value);
    }

    /// <summary>Gets or sets failed-job history retention in days.</summary>
    public int FailedJobHistoryRetentionDays
    {
        get => _failedJobHistoryRetentionDays;
        set => SetProperty(ref _failedJobHistoryRetentionDays, value);
    }

    /// <summary>Gets or sets maximum retry attempts per durable stage.</summary>
    public int MaximumIndexingRetryCount
    {
        get => _maximumIndexingRetryCount;
        set => SetProperty(ref _maximumIndexingRetryCount, value);
    }

    /// <summary>Gets or sets maximum simultaneous indexing stages.</summary>
    public int MaximumIndexingConcurrency
    {
        get => _maximumIndexingConcurrency;
        set => SetProperty(ref _maximumIndexingConcurrency, value);
    }

    /// <summary>Gets or sets whether indexing prefers user-idle periods where supported.</summary>
    public bool ProcessIndexOnlyWhileIdle
    {
        get => _processIndexOnlyWhileIdle;
        set => SetProperty(ref _processIndexOnlyWhileIdle, value);
    }

    /// <summary>Gets or sets whether indexing prefers external power where supported.</summary>
    public bool ProcessIndexOnlyOnPower
    {
        get => _processIndexOnlyOnPower;
        set => SetProperty(ref _processIndexOnlyOnPower, value);
    }

    /// <summary>Gets or sets whether a supported battery monitor may pause indexing.</summary>
    public bool PauseIndexingOnLowBattery
    {
        get => _pauseIndexingOnLowBattery;
        set => SetProperty(ref _pauseIndexingOnLowBattery, value);
    }

    /// <summary>Gets or sets the configured low-battery threshold.</summary>
    public int PauseBelowBatteryPercentage
    {
        get => _pauseBelowBatteryPercentage;
        set => SetProperty(ref _pauseBelowBatteryPercentage, value);
    }

    /// <summary>Gets or sets whether applicable Deep OCR stages may run.</summary>
    public bool DeepOcrProcessingEnabled
    {
        get => _deepOcrProcessingEnabled;
        set => SetProperty(ref _deepOcrProcessingEnabled, value);
    }

    /// <summary>Gets or sets whether optional local-AI indexing enrichment may run.</summary>
    public bool DeepAiProcessingEnabled
    {
        get => _deepAiProcessingEnabled;
        set => SetProperty(ref _deepAiProcessingEnabled, value);
    }

    /// <summary>Gets or sets whether generated summaries and keywords may be retained.</summary>
    public bool DeepSummaryProcessingEnabled
    {
        get => _deepSummaryProcessingEnabled;
        set => SetProperty(ref _deepSummaryProcessingEnabled, value);
    }

    /// <summary>Gets or sets whether related-concept data and selected chunks may be retained.</summary>
    public bool DeepSemanticProcessingEnabled
    {
        get => _deepSemanticProcessingEnabled;
        set => SetProperty(ref _deepSemanticProcessingEnabled, value);
    }

    /// <summary>Gets or sets whether archive contents may be indexed.</summary>
    public bool ArchiveIndexingEnabled
    {
        get => _archiveIndexingEnabled;
        set => SetProperty(ref _archiveIndexingEnabled, value);
    }

    /// <summary>Gets or sets whether known generated folders are excluded.</summary>
    public bool ExcludeGeneratedFolders
    {
        get => _excludeGeneratedFolders;
        set => SetProperty(ref _excludeGeneratedFolders, value);
    }

    /// <summary>Gets or sets whether binaries and executables remain metadata-only.</summary>
    public bool BinaryAndExecutableMetadataOnly
    {
        get => _binaryAndExecutableMetadataOnly;
        set => SetProperty(ref _binaryAndExecutableMetadataOnly, value);
    }

    /// <summary>Gets or sets whether an optional processing time window is active.</summary>
    public bool UseIndexingTimeWindow
    {
        get => _useIndexingTimeWindow;
        set => SetProperty(ref _useIndexingTimeWindow, value);
    }

    /// <summary>Gets or sets the local processing-window start hour.</summary>
    public int IndexingWindowStartHour
    {
        get => _indexingWindowStartHour;
        set => SetProperty(ref _indexingWindowStartHour, value);
    }

    /// <summary>Gets or sets the local processing-window end hour.</summary>
    public int IndexingWindowEndHour
    {
        get => _indexingWindowEndHour;
        set => SetProperty(ref _indexingWindowEndHour, value);
    }

    /// <summary>
    /// Creates a draft copied from validated application settings.
    /// </summary>
    /// <param name="settings">The settings to copy.</param>
    /// <returns>An independently editable settings draft.</returns>
    public static SettingsDraft FromSettings(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SettingsDraft
        {
            FileLoggingEnabled = settings.Logging.FileLoggingEnabled,
            LogDirectoryPath = settings.Logging.LogDirectoryPath,
            MinimumLogLevel = settings.Logging.MinimumLevel,
            RetainedFileCount = settings.Logging.RetainedFileCount,
            ShowAdvancedFeatures = settings.Features.ShowAdvancedFeatures,
            DiagnosticsEnabled = settings.Diagnostics.EnableDiagnostics || settings.Ai.RequestDiagnosticsEnabled,
            AiDiagnosticsEnabled = settings.Diagnostics.AiDiagnostics || settings.Ai.RequestDiagnosticsEnabled,
            OcrAndTextExtractionDiagnosticsEnabled = settings.Diagnostics.OcrAndTextExtractionDiagnostics,
            ScanningDiagnosticsEnabled = settings.Diagnostics.ScanningDiagnostics,
            DuplicateDetectionDiagnosticsEnabled = settings.Diagnostics.DuplicateDetectionDiagnostics,
            SearchAndIndexingDiagnosticsEnabled = settings.Diagnostics.SearchAndIndexingDiagnostics,
            RulesAndOrganisationDiagnosticsEnabled = settings.Diagnostics.RulesAndOrganisationDiagnostics,
            FileOperationDiagnosticsEnabled = settings.Diagnostics.FileOperationDiagnostics,
            PerformanceDiagnosticsEnabled = settings.Diagnostics.PerformanceDiagnostics,
            ShowUnredactedDiagnosticContent =
                settings.Diagnostics.ShowUnredactedDiagnosticContent ||
                settings.Ai.ShowUnredactedDiagnosticContent,
            FilesPageDetailsPanelWidthRatio = settings.Features.FilesPageDetailsPanelWidthRatio,
            AiEnabled = settings.Ai.Enabled,
            FileRenameSuggestionsEnabled = settings.Ai.FileRenameSuggestionsEnabled,
            FolderStructureSuggestionsEnabled = settings.Ai.FolderStructureSuggestionsEnabled,
            AiEndpoint = settings.Ai.Endpoint,
            SelectedAiModel = settings.Ai.SelectedModel,
            AiRequestTimeoutSeconds = settings.Ai.RequestTimeoutSeconds,
            AiRequestTimeoutText = settings.Ai.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            PreferenceAdaptationEnabled = settings.Ai.PreferenceAdaptationEnabled,
            DocumentTextInterpretationEnabled = settings.Ai.DocumentTextInterpretationEnabled,
            CatalogEnabled = settings.Catalog.Enabled,
            MetadataExtractionEnabled = settings.Content.MetadataExtractionEnabled,
            OcrEnabled = settings.Content.OcrEnabled,
            OcrOnlyWhenNativeTextUnavailable = settings.Content.OcrOnlyWhenNativeTextUnavailable,
            MaximumOcrPages = settings.Content.MaximumPagesPerDocument,
            MaximumContentFileSizeMiB = settings.Content.MaximumFileSizeMiB,
            OcrLanguage = settings.Content.OcrLanguage,
            MaximumOcrDurationSeconds = settings.Content.MaximumOcrDurationSeconds,
            PdfRasterizationDpi = settings.Content.PdfRasterizationDpi,
            MaximumRasterDimension = settings.Content.MaximumRasterDimension,
            MaximumOcrTextCharacters = settings.Content.MaximumOcrTextCharacters,
            MaximumTemporaryStorageMiB = settings.Content.MaximumTemporaryStorageMiB,
            TesseractExecutablePath = settings.Content.TesseractExecutablePath,
            BackgroundContentProcessingEnabled = settings.Content.BackgroundProcessingEnabled,
            SemanticSearchEnabled = settings.SemanticSearch.Enabled,
            MaximumSemanticDocuments = settings.SemanticSearch.MaximumDocumentCount,
            MaximumSemanticResults = settings.SemanticSearch.MaximumResultCount,
            DeepIndexingEnabled = settings.DeepIndexing.Enabled,
            DefaultIndexingLevel = settings.DeepIndexing.DefaultLevel,
            IndexingResourceMode = settings.DeepIndexing.ResourceMode,
            MaximumIndexSizeMiB = settings.DeepIndexing.MaximumIndexSizeMiB,
            MaximumExtractedTextCharacters = settings.DeepIndexing.MaximumExtractedTextCharacters,
            MaximumDeepOcrTextCharacters = settings.DeepIndexing.MaximumOcrTextCharacters,
            MaximumSemanticChunksPerDocument = settings.DeepIndexing.MaximumSemanticChunksPerDocument,
            DeletedFileRetentionDays = settings.DeepIndexing.DeletedFileRetentionDays,
            FailedJobHistoryRetentionDays = settings.DeepIndexing.FailedJobHistoryRetentionDays,
            MaximumIndexingRetryCount = settings.DeepIndexing.MaximumRetryCount,
            MaximumIndexingConcurrency = settings.DeepIndexing.MaximumConcurrency,
            ProcessIndexOnlyWhileIdle = settings.DeepIndexing.ProcessOnlyWhileIdle,
            ProcessIndexOnlyOnPower = settings.DeepIndexing.ProcessOnlyWhileConnectedToPower,
            PauseIndexingOnLowBattery = settings.DeepIndexing.PauseBelowBatteryPercentage.HasValue,
            PauseBelowBatteryPercentage = settings.DeepIndexing.PauseBelowBatteryPercentage ?? 20,
            DeepOcrProcessingEnabled = settings.DeepIndexing.OcrProcessingEnabled,
            DeepAiProcessingEnabled = settings.DeepIndexing.AiProcessingEnabled,
            DeepSummaryProcessingEnabled = settings.DeepIndexing.SummaryProcessingEnabled,
            DeepSemanticProcessingEnabled = settings.DeepIndexing.SemanticProcessingEnabled,
            ArchiveIndexingEnabled = settings.DeepIndexing.ArchiveIndexingEnabled,
            ExcludeGeneratedFolders = settings.DeepIndexing.ExcludeGeneratedFolders,
            BinaryAndExecutableMetadataOnly = settings.DeepIndexing.BinaryAndExecutableMetadataOnly,
            UseIndexingTimeWindow = settings.DeepIndexing.ProcessingWindowStartHour.HasValue,
            IndexingWindowStartHour = settings.DeepIndexing.ProcessingWindowStartHour ?? 22,
            IndexingWindowEndHour = settings.DeepIndexing.ProcessingWindowEndHour ?? 7,
            RelationshipAnalysisEnabled = settings.DeepIndexing.RelationshipAnalysisEnabled,
            RelationshipExcludedExtensions = string.Join(", ", settings.DeepIndexing.RelationshipExcludedExtensions),
            MaximumRelationshipCandidates = settings.DeepIndexing.MaximumRelationshipCandidates,
            MaximumRelationshipsPerFile = settings.DeepIndexing.MaximumRelationshipsPerFile,
            MaximumSmartCollectionMembers = settings.DeepIndexing.MaximumSmartCollectionMembers,
        };
    }

    /// <summary>
    /// Creates validated application settings from this draft.
    /// </summary>
    /// <returns>The settings ready for persistence.</returns>
    public ApplicationSettings ToSettings()
    {
        if (!int.TryParse(AiRequestTimeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out var timeoutSeconds))
        {
            throw new ConfigurationValidationException($"AI request timeout must be a whole number from {AiSettings.MinimumRequestTimeoutSeconds} through {AiSettings.MaximumRequestTimeoutSeconds} seconds.");
        }

        return new ApplicationSettings
        {
            Features = new FeatureSettings
            {
                ShowAdvancedFeatures = ShowAdvancedFeatures,
                FilesPageDetailsPanelWidthRatio = FilesPageDetailsPanelWidthRatio,
            },
            Logging = new LoggingSettings
            {
                FileLoggingEnabled = FileLoggingEnabled,
                LogDirectoryPath = string.IsNullOrWhiteSpace(LogDirectoryPath) ? null : LogDirectoryPath.Trim(),
                MinimumLevel = MinimumLogLevel,
                RetainedFileCount = RetainedFileCount,
            },
            Diagnostics = new DiagnosticsSettings
            {
                EnableDiagnostics = DiagnosticsEnabled,
                AiDiagnostics = AiDiagnosticsEnabled,
                OcrAndTextExtractionDiagnostics = OcrAndTextExtractionDiagnosticsEnabled,
                ScanningDiagnostics = ScanningDiagnosticsEnabled,
                DuplicateDetectionDiagnostics = DuplicateDetectionDiagnosticsEnabled,
                SearchAndIndexingDiagnostics = SearchAndIndexingDiagnosticsEnabled,
                RulesAndOrganisationDiagnostics = RulesAndOrganisationDiagnosticsEnabled,
                FileOperationDiagnostics = FileOperationDiagnosticsEnabled,
                PerformanceDiagnostics = PerformanceDiagnosticsEnabled,
                ShowUnredactedDiagnosticContent = ShowUnredactedDiagnosticContent,
            },
            Ai = new AiSettings
            {
                Enabled = AiEnabled,
                FileRenameSuggestionsEnabled = FileRenameSuggestionsEnabled,
                FolderStructureSuggestionsEnabled = FolderStructureSuggestionsEnabled,
                RequestDiagnosticsEnabled = DiagnosticsEnabled && AiDiagnosticsEnabled,
                ShowUnredactedDiagnosticContent = ShowUnredactedDiagnosticContent,
                Endpoint = AiEndpoint?.Trim() ?? string.Empty,
                SelectedModel = string.IsNullOrWhiteSpace(SelectedAiModel) ? null : SelectedAiModel.Trim(),
                RequestTimeoutSeconds = timeoutSeconds,
                PreferenceAdaptationEnabled = PreferenceAdaptationEnabled,
                DocumentTextInterpretationEnabled = DocumentTextInterpretationEnabled,
            },
            Catalog = new CatalogSettings
            {
                Enabled = CatalogEnabled,
            },
            Content = new ContentSettings
            {
                MetadataExtractionEnabled = MetadataExtractionEnabled,
                OcrEnabled = OcrEnabled,
                OcrOnlyWhenNativeTextUnavailable = OcrOnlyWhenNativeTextUnavailable,
                MaximumPagesPerDocument = MaximumOcrPages,
                MaximumFileSizeMiB = MaximumContentFileSizeMiB,
                OcrLanguage = OcrLanguage.Trim(),
                MaximumOcrDurationSeconds = MaximumOcrDurationSeconds,
                PdfRasterizationDpi = PdfRasterizationDpi,
                MaximumRasterDimension = MaximumRasterDimension,
                MaximumOcrTextCharacters = MaximumOcrTextCharacters,
                MaximumTemporaryStorageMiB = MaximumTemporaryStorageMiB,
                TesseractExecutablePath = string.IsNullOrWhiteSpace(TesseractExecutablePath)
                ? null
                : TesseractExecutablePath.Trim(),
                BackgroundProcessingEnabled = BackgroundContentProcessingEnabled,
            },
            SemanticSearch = new SemanticSearchSettings
            {
                Enabled = SemanticSearchEnabled,
                MaximumDocumentCount = MaximumSemanticDocuments,
                MaximumResultCount = MaximumSemanticResults,
            },
            DeepIndexing = new DeepIndexingSettings
            {
                Enabled = DeepIndexingEnabled,
                DefaultLevel = DefaultIndexingLevel,
                ResourceMode = IndexingResourceMode,
                MaximumIndexSizeMiB = MaximumIndexSizeMiB,
                MaximumExtractedTextCharacters = MaximumExtractedTextCharacters,
                MaximumOcrTextCharacters = MaximumDeepOcrTextCharacters,
                MaximumSemanticChunksPerDocument = MaximumSemanticChunksPerDocument,
                DeletedFileRetentionDays = DeletedFileRetentionDays,
                FailedJobHistoryRetentionDays = FailedJobHistoryRetentionDays,
                MaximumRetryCount = MaximumIndexingRetryCount,
                MaximumConcurrency = MaximumIndexingConcurrency,
                ProcessOnlyWhileIdle = ProcessIndexOnlyWhileIdle,
                ProcessOnlyWhileConnectedToPower = ProcessIndexOnlyOnPower,
                PauseBelowBatteryPercentage = PauseIndexingOnLowBattery ? PauseBelowBatteryPercentage : null,
                OcrProcessingEnabled = DeepOcrProcessingEnabled,
                AiProcessingEnabled = DeepAiProcessingEnabled,
                SummaryProcessingEnabled = DeepSummaryProcessingEnabled,
                SemanticProcessingEnabled = DeepSemanticProcessingEnabled,
                ArchiveIndexingEnabled = ArchiveIndexingEnabled,
                ExcludeGeneratedFolders = ExcludeGeneratedFolders,
                BinaryAndExecutableMetadataOnly = BinaryAndExecutableMetadataOnly,
                ProcessingWindowStartHour = UseIndexingTimeWindow ? IndexingWindowStartHour : null,
                ProcessingWindowEndHour = UseIndexingTimeWindow ? IndexingWindowEndHour : null,
                RelationshipAnalysisEnabled = RelationshipAnalysisEnabled,
                RelationshipExcludedExtensions = RelationshipExcludedExtensions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(extension => "." + extension.TrimStart('.').ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                MaximumRelationshipCandidates = MaximumRelationshipCandidates,
                MaximumRelationshipsPerFile = MaximumRelationshipsPerFile,
                MaximumSmartCollectionMembers = MaximumSmartCollectionMembers,
            },
        };
    }
}
