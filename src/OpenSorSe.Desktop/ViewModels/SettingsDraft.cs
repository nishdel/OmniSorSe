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

    /// <summary>Gets or sets whether local Semantic Search Beta is enabled.</summary>
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
        };
    }
}
