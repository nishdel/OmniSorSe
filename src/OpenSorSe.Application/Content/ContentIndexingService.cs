using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Logging;
using OpenSorSe.Scanner.Models;
using OpenSorSe.Application.Tags;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Application.Content;

/// <summary>Indexes bounded metadata and OCR text with cache reuse and per-file failure isolation.</summary>
public sealed class ContentIndexingService : IContentIndexingService
{
    private readonly IConfigurationService _configurationService;
    private readonly IContentStore _contentStore;
    private readonly ILogger _logger;
    private readonly IMetadataExtractionPipeline _metadataPipeline;
    private readonly IOcrService _ocrService;
    private readonly IDiagnosticsEventSink? _diagnostics;

    /// <summary>Initializes the local scan-content indexing stage.</summary>
    public ContentIndexingService(
        IConfigurationService configurationService,
        IMetadataExtractionPipeline metadataPipeline,
        IOcrService ocrService,
        IContentStore contentStore,
        ILoggingService loggingService,
        IDiagnosticsEventSink? diagnostics = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _metadataPipeline = metadataPipeline ?? throw new ArgumentNullException(nameof(metadataPipeline));
        _ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService)))
            .CreateLogger(nameof(ContentIndexingService));
        _diagnostics = DiagnosticsIsolation.Protect(diagnostics);
    }

    /// <inheritdoc />
    public async Task<ContentIndexingSummary> IndexAsync(
        IReadOnlyCollection<FileEntry> files,
        CancellationToken cancellationToken)
    {
        return await IndexAsync(files, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ContentIndexingSummary> IndexAsync(
        IReadOnlyCollection<FileEntry> files,
        ContentIndexingOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        var settings = EffectiveSettings(_configurationService.Current.Content, options);
        if (!settings.MetadataExtractionEnabled && !settings.OcrEnabled)
        {
            return new ContentIndexingSummary(files.Count, 0, 0, 0, 0, files.Count);
        }

        var indexed = 0;
        var cacheHits = 0;
        var failed = 0;
        var ocrCompleted = 0;
        var ocrSkipped = 0;
        var maximumBytes = settings.MaximumFileSizeMiB * 1024L * 1024L;
        var capability = settings.OcrEnabled && files.Count > 0
            ? await _ocrService.GetCapabilityAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var extractionFingerprint = ContentCacheFingerprint.Create(settings, capability);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var diagnosticStarted = Stopwatch.StartNew();
            var sessionId = _diagnostics?.BeginSession(
                DiagnosticCategory.OcrAndTextExtraction,
                "Extract and index document text",
                [
                    new DiagnosticField("Source file", file.FullPath, DiagnosticDataClassification.Path),
                    new DiagnosticField("File type", Path.GetExtension(file.FullPath).ToLowerInvariant()),
                    new DiagnosticField("Metadata extraction enabled", settings.MetadataExtractionEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("OCR enabled", settings.OcrEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ],
                string.IsNullOrWhiteSpace(file.ScanDiagnosticSessionId) ? null : [file.ScanDiagnosticSessionId]);
            if (!string.IsNullOrWhiteSpace(sessionId) &&
                !string.IsNullOrWhiteSpace(file.ScanDiagnosticSessionId))
            {
                _diagnostics?.Relate(file.ScanDiagnosticSessionId, sessionId);
            }
            try
            {
                var source = ReadSourceIdentity(file);
                var existing = await _contentStore.GetAsync(file.FullPath, cancellationToken).ConfigureAwait(false);
                if (existing is not null &&
                    existing.SourceLength == source.Length &&
                    existing.SourceLastWriteTimeUtc == source.LastWriteTimeUtc &&
                    string.Equals(
                        existing.ExtractionFingerprint,
                        extractionFingerprint,
                        StringComparison.Ordinal))
                {
                    cacheHits++;
                    _diagnostics?.Publish(
                        sessionId,
                        "Extraction strategy",
                        DiagnosticStatus.Skipped,
                        DiagnosticSeverity.Information,
                        DiagnosticSection.Overview,
                        "A valid bounded content-cache record was reused.",
                        [
                            new DiagnosticField("Strategy", "Validated content-cache reuse"),
                            new DiagnosticField("Source length bytes", source.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        ]);
                    _diagnostics?.Complete(
                        sessionId,
                        DiagnosticStatus.Skipped,
                        diagnosticStarted.Elapsed,
                        "Extraction was skipped because the compatible cached result is current.");
                    continue;
                }

                var metadata = settings.MetadataExtractionEnabled
                    ? await _metadataPipeline.ExtractAsync(
                        file,
                        maximumBytes,
                        settings.MaximumPagesPerDocument,
                        cancellationToken).ConfigureAwait(false)
                    : new MetadataExtractionResult([], null, false, null, []);
                _diagnostics?.Publish(
                    sessionId,
                    "Native text extraction",
                    DiagnosticStatus.Succeeded,
                    metadata.Warnings.Count == 0 ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                    DiagnosticSection.IntermediateResults,
                    "Native metadata and text extraction completed.",
                    [
                        new DiagnosticField("Extraction strategy", metadata.ExtractionStrategies.Count == 0
                            ? "No format-specific native text extractor"
                            : string.Join(", ", metadata.ExtractionStrategies)),
                        new DiagnosticField("Page count", metadata.PageCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty),
                        new DiagnosticField("Raw native embedded text", metadata.RawNativeText ?? string.Empty, DiagnosticDataClassification.Content),
                        new DiagnosticField("Normalized native text", metadata.NativeText ?? string.Empty, DiagnosticDataClassification.Content),
                        new DiagnosticField("Raw native character count", metadata.RawNativeText?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                        new DiagnosticField("Normalized native character count", metadata.NativeText?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                        new DiagnosticField("Native extraction truncated", metadata.WasTruncated.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new DiagnosticField("Native-text quality decision", metadata.HasReliableNativeText
                            ? "Reliable native text is available for every required page."
                            : "Native text is absent or insufficient for at least one required page."),
                        new DiagnosticField("Metadata field count", metadata.Fields.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ]);
                foreach (var page in metadata.PdfPages.Take(DiagnosticLimits.MaximumPageRecords))
                {
                    _diagnostics?.Publish(
                        sessionId,
                        $"Native page {page.PageNumber}",
                        page.HasReliableNativeText ? DiagnosticStatus.Succeeded : DiagnosticStatus.PartiallySucceeded,
                        page.HasReliableNativeText ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                        DiagnosticSection.IntermediateResults,
                        page.HasReliableNativeText
                            ? "Native page text passed the deterministic quality policy."
                            : "Native page text did not pass the deterministic quality policy; OCR fallback may run.",
                        [
                            new DiagnosticField("Page number", page.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                            new DiagnosticField("Raw native page text", page.RawNativeText ?? string.Empty, DiagnosticDataClassification.Content),
                            new DiagnosticField("Normalized native page text", page.NativeText ?? string.Empty, DiagnosticDataClassification.Content),
                            new DiagnosticField("Reliable native text", page.HasReliableNativeText.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        ]);
                }
                var ocr = await _ocrService.RecognizeAsync(
                    new OcrRequest(
                        file.FullPath,
                        settings.OcrLanguage,
                        maximumBytes,
                        settings.MaximumPagesPerDocument,
                        TimeSpan.FromSeconds(settings.MaximumOcrDurationSeconds),
                        metadata.HasReliableNativeText)
                    {
                        PdfPages = metadata.PdfPages,
                        RasterizationDpi = settings.PdfRasterizationDpi,
                        MaximumRasterDimension = settings.MaximumRasterDimension,
                        MaximumTextCharacters = settings.MaximumOcrTextCharacters,
                        MaximumTemporaryStorageBytes = settings.MaximumTemporaryStorageMiB * 1024L * 1024L,
                        DiagnosticSessionId = sessionId,
                    },
                    cancellationToken).ConfigureAwait(false);
                if (ocr.Status is OcrStatus.Completed or OcrStatus.PartiallyCompleted)
                {
                    ocrCompleted++;
                }
                else if (ocr.Status == OcrStatus.Skipped)
                {
                    ocrSkipped++;
                }

                var indexedAt = DateTimeOffset.UtcNow;
                var generatedTags = ProvenanceTagGenerator.Generate(
                    Path.GetFullPath(file.FullPath),
                    $"{source.Length}:{source.LastWriteTimeUtc.UtcTicks}",
                    metadata.Fields,
                    metadata.NativeText,
                    ocr.DownstreamText ?? ocr.ExtractedText,
                    indexedAt);
                var record = new ContentRecord(
                    Path.GetFullPath(file.FullPath),
                    source.Length,
                    source.LastWriteTimeUtc,
                    indexedAt,
                    metadata.Fields,
                    metadata.NativeText,
                    ocr.DownstreamText ?? ocr.ExtractedText,
                    ocr.Status,
                    ocr.EngineIdentifier,
                    Array.AsReadOnly(metadata.Warnings
                        .Concat(ocr.Warnings)
                        .Append(ocr.Message)
                        .Distinct(StringComparer.Ordinal)
                        .Take(16)
                        .ToArray()))
                {
                    ExtractionFingerprint = extractionFingerprint,
                    DiagnosticSessionId = sessionId,
                    OcrPages = ocr.Pages,
                    Tags = MergeTags(
                        generatedTags,
                        existing?.Tags ?? [],
                        $"{source.Length}:{source.LastWriteTimeUtc.UtcTicks}"),
                };
                await _contentStore.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
                _diagnostics?.Publish(
                    sessionId,
                    "Downstream text",
                    ocr.Status is OcrStatus.Failed or OcrStatus.Unavailable
                        ? DiagnosticStatus.PartiallySucceeded
                        : DiagnosticStatus.Succeeded,
                    ocr.Status is OcrStatus.Failed or OcrStatus.Unavailable
                        ? DiagnosticSeverity.Warning
                        : DiagnosticSeverity.Information,
                    DiagnosticSection.Outputs,
                    "Bounded text and metadata were supplied to local content indexing.",
                    [
                        new DiagnosticField("Native text supplied downstream", metadata.NativeText ?? string.Empty, DiagnosticDataClassification.Content),
                        new DiagnosticField("OCR text supplied downstream", ocr.DownstreamText ?? ocr.ExtractedText ?? string.Empty, DiagnosticDataClassification.Content),
                        new DiagnosticField("Metadata supplied downstream", string.Join(
                            Environment.NewLine,
                            metadata.Fields.Select(field => $"{field.Name}: {field.Value}")),
                            DiagnosticDataClassification.Metadata),
                        new DiagnosticField("Native downstream character count", metadata.NativeText?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                        new DiagnosticField("OCR downstream character count", (ocr.DownstreamText ?? ocr.ExtractedText)?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                    ]);
                var terminalStatus = ocr.Status switch
                {
                    OcrStatus.PartiallyCompleted or OcrStatus.TextNotIndexedDueToBounds =>
                        DiagnosticStatus.PartiallySucceeded,
                    OcrStatus.Failed or OcrStatus.Unavailable when
                        !string.IsNullOrWhiteSpace(metadata.NativeText) || metadata.Fields.Count > 0 =>
                        DiagnosticStatus.PartiallySucceeded,
                    OcrStatus.Failed or OcrStatus.Unavailable => DiagnosticStatus.Failed,
                    _ => DiagnosticStatus.Succeeded,
                };
                if (terminalStatus == DiagnosticStatus.Succeeded &&
                    (metadata.WasTruncated || metadata.Warnings.Count > 0))
                {
                    terminalStatus = DiagnosticStatus.PartiallySucceeded;
                }
                _diagnostics?.Complete(
                    sessionId,
                    terminalStatus,
                    diagnosticStarted.Elapsed,
                    terminalStatus switch
                    {
                        DiagnosticStatus.PartiallySucceeded =>
                            "Content extraction completed with a usable partial result.",
                        DiagnosticStatus.Failed =>
                            "Content extraction completed without usable native or OCR content.",
                        _ => "Content extraction and downstream indexing completed.",
                    },
                    terminalStatus == DiagnosticStatus.Failed
                        ? DiagnosticSeverity.Error
                        : terminalStatus == DiagnosticStatus.PartiallySucceeded
                            ? DiagnosticSeverity.Warning
                            : DiagnosticSeverity.Information);
                indexed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _diagnostics?.Complete(
                    sessionId,
                    DiagnosticStatus.Cancelled,
                    diagnosticStarted.Elapsed,
                    "Content extraction was cancelled.",
                    DiagnosticSeverity.Warning);
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                _diagnostics?.Complete(
                    sessionId,
                    DiagnosticStatus.Failed,
                    diagnosticStarted.Elapsed,
                    "Content extraction failed safely and the scan continued.",
                    DiagnosticSeverity.Error,
                    [new DiagnosticField("Error category", exception.GetType().Name)]);
                _logger.LogWarning(
                    "Local content extraction failed safely for one scanned file. Error category: {ErrorCategory}.",
                    exception.GetType().Name);
            }
        }

        return new ContentIndexingSummary(
            files.Count,
            indexed,
            cacheHits,
            failed,
            ocrCompleted,
            ocrSkipped);
    }

    private static ContentSettings EffectiveSettings(
        ContentSettings global,
        ContentIndexingOptions? options)
    {
        if (options is null)
        {
            return global;
        }

        var maximumProfileMiB = Math.Max(
            1,
            (int)Math.Min(
                1024,
                Math.Ceiling(options.MaximumFileSizeBytes / (1024d * 1024d))));
        return new ContentSettings
        {
            MetadataExtractionEnabled =
                global.MetadataExtractionEnabled &&
                (options.MetadataEnabled || options.TextEnabled),
            OcrEnabled = global.OcrEnabled && options.OcrEnabled,
            OcrOnlyWhenNativeTextUnavailable =
                global.OcrOnlyWhenNativeTextUnavailable &&
                options.OcrOnlyWhenTextUnavailable,
            MaximumPagesPerDocument = Math.Min(
                global.MaximumPagesPerDocument,
                options.MaximumPagesPerDocument),
            MaximumFileSizeMiB = Math.Min(global.MaximumFileSizeMiB, maximumProfileMiB),
            OcrLanguage = options.OcrLanguage,
            MaximumOcrDurationSeconds = global.MaximumOcrDurationSeconds,
            PdfRasterizationDpi = global.PdfRasterizationDpi,
            MaximumRasterDimension = global.MaximumRasterDimension,
            MaximumOcrTextCharacters = global.MaximumOcrTextCharacters,
            MaximumTemporaryStorageMiB = global.MaximumTemporaryStorageMiB,
            TesseractExecutablePath = global.TesseractExecutablePath,
            BackgroundProcessingEnabled = global.BackgroundProcessingEnabled,
        };
    }

    private static (long Length, DateTimeOffset LastWriteTimeUtc) ReadSourceIdentity(FileEntry file)
    {
        if (file.Metadata?.SizeInBytes is { } length &&
            file.Metadata.LastWriteTimeUtc is { } modified)
        {
            return (length, modified.ToUniversalTime());
        }

        var info = new FileInfo(file.FullPath);
        return (info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private static IReadOnlyList<OpenSorSe.Application.Models.TagAssociation> MergeTags(
        IReadOnlyList<OpenSorSe.Application.Models.TagAssociation> generated,
        IReadOnlyList<OpenSorSe.Application.Models.TagAssociation> existing,
        string sourceFingerprint)
    {
        var retained = existing.Where(tag =>
            tag.Source == OpenSorSe.Application.Models.TagSource.UserApproved ||
            tag.AcceptanceState == OpenSorSe.Application.Models.TagAcceptanceState.Accepted && !tag.IsSystem ||
            tag.AcceptanceState == OpenSorSe.Application.Models.TagAcceptanceState.Rejected &&
            string.Equals(tag.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal));
        return Array.AsReadOnly(generated
            .Concat(retained)
            .GroupBy(tag => tag.NormalizedValue, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(tag => tag.Source == OpenSorSe.Application.Models.TagSource.UserApproved)
                .ThenByDescending(tag => tag.AcceptanceState == OpenSorSe.Application.Models.TagAcceptanceState.Accepted)
                .ThenByDescending(tag => tag.AcceptanceState == OpenSorSe.Application.Models.TagAcceptanceState.Rejected)
                .ThenByDescending(tag => tag.Confidence)
                .First())
            .OrderByDescending(tag => tag.AcceptanceState == OpenSorSe.Application.Models.TagAcceptanceState.Accepted)
            .ThenBy(tag => tag.NormalizedValue, StringComparer.Ordinal)
            .Take(32)
            .ToArray());
    }
}

/// <summary>Builds the deterministic extraction-settings identity stored with local content.</summary>
public static class ContentCacheFingerprint
{
    private const int SchemaVersion = 2;

    /// <summary>Creates a stable non-secret fingerprint for settings and detected local OCR components.</summary>
    public static string Create(ContentSettings settings, OcrCapability? capability)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var value = string.Join(
            "|",
            SchemaVersion,
            settings.MetadataExtractionEnabled,
            settings.OcrEnabled,
            settings.OcrOnlyWhenNativeTextUnavailable,
            settings.MaximumPagesPerDocument,
            settings.MaximumFileSizeMiB,
            settings.OcrLanguage,
            settings.MaximumOcrDurationSeconds,
            settings.PdfRasterizationDpi,
            settings.MaximumRasterDimension,
            settings.MaximumOcrTextCharacters,
            settings.MaximumTemporaryStorageMiB,
            capability?.EngineIdentifier ?? "none",
            capability?.EngineVersion ?? "none",
            capability?.RasterizerIdentifier ?? "none",
            capability?.RasterizerVersion ?? "none");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
