using System.Diagnostics;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Application.Content;

/// <summary>Enforces local OCR settings and input bounds before invoking a concrete engine.</summary>
public sealed class OcrService : IOcrService
{
    private readonly IConfigurationService _configurationService;
    private readonly IOcrEngine _engine;
    private readonly IDiagnosticsEventSink? _diagnostics;

    /// <summary>Initializes the bounded local OCR service.</summary>
    public OcrService(
        IConfigurationService configurationService,
        IOcrEngine engine,
        IDiagnosticsEventSink? diagnostics = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _diagnostics = DiagnosticsIsolation.Protect(diagnostics);
    }

    /// <inheritdoc />
    public Task<OcrCapability> GetCapabilityAsync(CancellationToken cancellationToken) =>
        _engine.DetectCapabilityAsync(cancellationToken);

    /// <inheritdoc />
    public Task<OcrCapability> RefreshCapabilityAsync(CancellationToken cancellationToken)
    {
        _engine.ResetCapability();
        return _engine.DetectCapabilityAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OcrResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = Stopwatch.StartNew();
        var sessionId = request.DiagnosticSessionId ?? _diagnostics?.BeginSession(
            DiagnosticCategory.OcrAndTextExtraction,
            "OCR and text extraction",
            [
                new DiagnosticField("Source file", request.FullPath, DiagnosticDataClassification.Path),
                new DiagnosticField("File type", Path.GetExtension(request.FullPath).ToLowerInvariant()),
                new DiagnosticField("Requested language", request.Language),
                new DiagnosticField("Requested page limit", request.MaximumPages.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticField("Requested render DPI", request.RasterizationDpi.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ]);
        var ownsSession = request.DiagnosticSessionId is null && sessionId is not null;
        if (cancellationToken.IsCancellationRequested)
        {
            if (ownsSession)
            {
                _diagnostics?.Complete(
                    sessionId,
                    DiagnosticStatus.Cancelled,
                    started.Elapsed,
                    "OCR was cancelled before local processing started.",
                    DiagnosticSeverity.Warning);
            }
            else
            {
                _diagnostics?.Publish(
                    sessionId,
                    "OCR cancelled",
                    DiagnosticStatus.Cancelled,
                    DiagnosticSeverity.Warning,
                    DiagnosticSection.WarningsAndErrors,
                    "OCR was cancelled before local processing started.");
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        var settings = _configurationService.Current.Content;
        if (!settings.OcrEnabled)
        {
            PublishFallbackDecision(
                sessionId,
                false,
                "OCR is disabled in Settings.",
                settings.OcrLanguage,
                settings.PdfRasterizationDpi);
            return Finish(
                Skipped(request, OcrFailureCategory.Disabled, "OCR is disabled in Settings."),
                sessionId,
                ownsSession,
                started.Elapsed);
        }

        if (settings.OcrOnlyWhenNativeTextUnavailable && request.HasReliableNativeText)
        {
            PublishFallbackDecision(
                sessionId,
                false,
                "Reliable native text passed the deterministic quality policy.",
                settings.OcrLanguage,
                settings.PdfRasterizationDpi);
            return Finish(Skipped(
                request,
                OcrFailureCategory.None,
                "OCR was skipped because reliable native text is available."),
                sessionId,
                ownsSession,
                started.Elapsed);
        }

        cancellationToken.ThrowIfCancellationRequested();
        FileInfo info;
        try
        {
            info = new FileInfo(request.FullPath);
            if (!info.Exists)
            {
                return Finish(
                    Failed(OcrFailureCategory.MalformedInput, "OCR input is no longer available."),
                    sessionId,
                    ownsSession,
                    started.Elapsed);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Finish(
                Failed(OcrFailureCategory.MalformedInput, "OCR input could not be read."),
                sessionId,
                ownsSession,
                started.Elapsed);
        }

        var maximumBytes = Math.Min(
            request.MaximumFileBytes,
            settings.MaximumFileSizeMiB * 1024L * 1024L);
        if (info.Length > maximumBytes)
        {
            return Finish(
                Failed(OcrFailureCategory.FileTooLarge, "OCR was skipped because the file exceeds the configured size bound."),
                sessionId,
                ownsSession,
                started.Elapsed);
        }

        var boundedRequest = request with
        {
            Language = settings.OcrLanguage,
            MaximumFileBytes = maximumBytes,
            MaximumPages = Math.Min(request.MaximumPages, settings.MaximumPagesPerDocument),
            RasterizationDpi = settings.PdfRasterizationDpi,
            MaximumRasterDimension = settings.MaximumRasterDimension,
            MaximumTextCharacters = Math.Min(
                request.MaximumTextCharacters,
                settings.MaximumOcrTextCharacters),
            MaximumTemporaryStorageBytes = Math.Min(
                request.MaximumTemporaryStorageBytes,
                settings.MaximumTemporaryStorageMiB * 1024L * 1024L),
            Timeout = TimeSpan.FromSeconds(Math.Min(
                request.Timeout.TotalSeconds,
                settings.MaximumOcrDurationSeconds)),
            DiagnosticSessionId = sessionId,
        };
        _diagnostics?.Publish(
            sessionId,
            "OCR fallback decision",
            DiagnosticStatus.Active,
            DiagnosticSeverity.Information,
            DiagnosticSection.Overview,
            "OCR will run because reliable native text is unavailable or forced reprocessing is enabled.",
            [
                new DiagnosticField("Fallback required", "True"),
                new DiagnosticField("Fallback reason", request.ForceReprocessAllPages
                    ? "All pages were explicitly requested for reprocessing."
                    : "Reliable native text was not available for every required page."),
                new DiagnosticField("Language configuration", boundedRequest.Language),
                new DiagnosticField("Render DPI", boundedRequest.RasterizationDpi.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticField("Maximum rendered dimension", boundedRequest.MaximumRasterDimension.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ]);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(boundedRequest.Timeout);
        try
        {
            var result = await _engine.RecognizeAsync(boundedRequest, timeout.Token).ConfigureAwait(false);
            return Finish(
                result,
                sessionId,
                ownsSession,
                started.Elapsed,
                boundedRequest.MaximumTextCharacters,
                boundedRequest.MaximumPages);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (ownsSession)
            {
                _diagnostics?.Complete(
                    sessionId,
                    DiagnosticStatus.Cancelled,
                    started.Elapsed,
                    "OCR was cancelled by the caller.",
                    DiagnosticSeverity.Warning);
            }
            else
            {
                _diagnostics?.Publish(
                    sessionId,
                    "OCR cancelled",
                    DiagnosticStatus.Cancelled,
                    DiagnosticSeverity.Warning,
                    DiagnosticSection.WarningsAndErrors,
                    "OCR was cancelled by the caller.");
            }
            throw;
        }
        catch (OperationCanceledException)
        {
            return Finish(
                Failed(OcrFailureCategory.Timeout, "Local OCR timed out."),
                sessionId,
                ownsSession,
                started.Elapsed);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or
            System.ComponentModel.Win32Exception or TypeInitializationException or
            DllNotFoundException)
        {
            return Finish(
                Failed(OcrFailureCategory.EngineFailure, "Local OCR failed safely."),
                sessionId,
                ownsSession,
                started.Elapsed);
        }
    }

    private OcrResult Finish(
        OcrResult result,
        string? sessionId,
        bool ownsSession,
        TimeSpan elapsed,
        int maximumTextCharacters = ContentText.MaximumTextCharacters,
        int maximumPageRecords = ContentText.MaximumPageRecords)
    {
        result = BoundResult(result, maximumTextCharacters, maximumPageRecords);
        var normalized = result.NormalizedText ?? result.ExtractedText;
        var downstream = result.DownstreamText ?? result.ExtractedText;
        _diagnostics?.Publish(
            sessionId,
            "OCR result",
            ToDiagnosticStatus(result.Status),
            ToDiagnosticSeverity(result.Status),
            result.Status is OcrStatus.Failed or OcrStatus.Unavailable or OcrStatus.TextNotIndexedDueToBounds
                ? DiagnosticSection.WarningsAndErrors
                : DiagnosticSection.Outputs,
            result.Message,
            [
                new DiagnosticField("OCR status", result.Status.ToString()),
                new DiagnosticField("Failure category", result.FailureCategory.ToString()),
                new DiagnosticField("OCR engine", result.EngineIdentifier),
                new DiagnosticField("OCR engine version", result.EngineVersion ?? string.Empty),
                new DiagnosticField("Language configuration", result.Language ?? string.Empty),
                new DiagnosticField("Page count", result.PageCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty),
                new DiagnosticField("Raw OCR output", result.RawExtractedText ?? string.Empty, DiagnosticDataClassification.Content),
                new DiagnosticField("Normalized OCR text", normalized ?? string.Empty, DiagnosticDataClassification.Content),
                new DiagnosticField("Text supplied downstream", downstream ?? string.Empty, DiagnosticDataClassification.Content),
                new DiagnosticField("Raw character count", result.RawExtractedText?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                new DiagnosticField("Normalized character count", normalized?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                new DiagnosticField("Downstream character count", downstream?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0"),
                new DiagnosticField("Truncated", result.WasTruncated.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticField("Duration milliseconds", result.ProcessingDuration.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)),
            ]);
        foreach (var page in result.Pages.Take(DiagnosticLimits.MaximumPageRecords))
        {
            _diagnostics?.Publish(
                sessionId,
                $"Page {page.PageNumber}",
                ToDiagnosticStatus(page.Status),
                ToDiagnosticSeverity(page.Status),
                page.Status is OcrStatus.Failed or OcrStatus.TextNotIndexedDueToBounds
                    ? DiagnosticSection.WarningsAndErrors
                    : DiagnosticSection.IntermediateResults,
                page.Message,
                [
                    new DiagnosticField("Page number", page.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    new DiagnosticField("Text source", page.TextSource.ToString()),
                    new DiagnosticField("Per-page status", page.Status.ToString()),
                    new DiagnosticField("Render DPI", page.RenderDpi?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty),
                    new DiagnosticField("Rendered dimensions", page.RenderedWidth is null || page.RenderedHeight is null
                        ? "Not available"
                        : $"{page.RenderedWidth} x {page.RenderedHeight}"),
                    new DiagnosticField("Preprocessing steps", page.PreprocessingSteps.Count == 0
                        ? "None reported"
                        : string.Join(", ", page.PreprocessingSteps)),
                    new DiagnosticField("Page duration milliseconds", page.ProcessingDuration?.TotalMilliseconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "Not reported"),
                    new DiagnosticField("Raw OCR page text", page.RawText ?? string.Empty, DiagnosticDataClassification.Content),
                    new DiagnosticField("Normalized page text", page.NormalizedText ?? page.Text ?? string.Empty, DiagnosticDataClassification.Content),
                    new DiagnosticField("Confidence", page.Confidence?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Not exposed by engine"),
                ]);
        }
        if (result.Pages.Count > DiagnosticLimits.MaximumPageRecords)
        {
            _diagnostics?.Publish(
                sessionId,
                "Page diagnostics bounded",
                DiagnosticStatus.PartiallySucceeded,
                DiagnosticSeverity.Warning,
                DiagnosticSection.WarningsAndErrors,
                "Additional page records were not retained.",
                [new DiagnosticField("Omitted page records", (result.Pages.Count - DiagnosticLimits.MaximumPageRecords).ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        }
        foreach (var warning in result.Warnings.Take(16))
        {
            _diagnostics?.Publish(
                sessionId,
                "OCR warning",
                DiagnosticStatus.PartiallySucceeded,
                DiagnosticSeverity.Warning,
                DiagnosticSection.WarningsAndErrors,
                warning);
        }
        _diagnostics?.Publish(
            sessionId,
            "Rendered page preview retention",
            DiagnosticStatus.Skipped,
            DiagnosticSeverity.Information,
            DiagnosticSection.IntermediateResults,
            "Rendered page previews are not retained because temporary OCR images are deleted immediately and the configured diagnostic image-preview limit is zero.",
            [new DiagnosticField("Maximum image previews", DiagnosticLimits.MaximumImagePreviewsPerSession.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        if (ownsSession)
        {
            _diagnostics?.Complete(
                sessionId,
                ToDiagnosticStatus(result.Status),
                elapsed,
                result.Message,
                ToDiagnosticSeverity(result.Status));
        }

        return result with
        {
            NormalizedText = normalized,
            DownstreamText = downstream,
        };
    }

    private static OcrResult BoundResult(
        OcrResult result,
        int maximumTextCharacters,
        int maximumPageRecords)
    {
        var textLimit = Math.Clamp(maximumTextCharacters, 1, ContentText.MaximumTextCharacters);
        var pageLimit = Math.Clamp(maximumPageRecords, 1, ContentText.MaximumPageRecords);
        var pages = result.Pages
            .Take(pageLimit)
            .Select(page => page with
            {
                Text = NormalizeBounded(page.Text, textLimit),
                RawText = BoundRaw(page.RawText, textLimit),
                NormalizedText = NormalizeBounded(page.NormalizedText ?? page.Text, textLimit),
                PreprocessingSteps = Array.AsReadOnly(page.PreprocessingSteps
                    .Take(16)
                    .Select(step => ContentText.NormalizeField(step, 256))
                    .Where(step => step.Length > 0)
                    .ToArray()),
            })
            .ToArray();
        var wasTruncated = result.WasTruncated ||
                           IsLongerThan(result.ExtractedText, textLimit) ||
                           IsLongerThan(result.RawExtractedText, textLimit) ||
                           IsLongerThan(result.NormalizedText, textLimit) ||
                           IsLongerThan(result.DownstreamText, textLimit) ||
                           result.Pages.Count > pageLimit ||
                           result.Warnings.Count > 16 ||
                           result.Pages.Any(page =>
                               IsLongerThan(page.Text, textLimit) ||
                               IsLongerThan(page.RawText, textLimit) ||
                               IsLongerThan(page.NormalizedText, textLimit) ||
                               page.PreprocessingSteps.Count > 16);
        var warnings = result.Warnings
            .Take(wasTruncated ? 15 : 16)
            .Select(warning => ContentText.NormalizeField(warning, 512))
            .Where(warning => warning.Length > 0)
            .ToList();
        if (wasTruncated)
        {
            warnings.Insert(0, "OCR retention reached a configured text, page, step, or warning bound.");
        }

        return result with
        {
            ExtractedText = NormalizeBounded(result.ExtractedText, textLimit),
            RawExtractedText = BoundRaw(result.RawExtractedText, textLimit),
            NormalizedText = NormalizeBounded(result.NormalizedText ?? result.ExtractedText, textLimit),
            DownstreamText = NormalizeBounded(result.DownstreamText ?? result.ExtractedText, textLimit),
            Pages = Array.AsReadOnly(pages),
            Warnings = Array.AsReadOnly(warnings.Take(16).ToArray()),
            WasTruncated = wasTruncated,
        };
    }

    private static string? BoundRaw(string? value, int maximumCharacters) =>
        string.IsNullOrEmpty(value)
            ? value
            : value.Length <= maximumCharacters
                ? value
                : value[..maximumCharacters];

    private static string? NormalizeBounded(string? value, int maximumCharacters)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = ContentText.Normalize(value);
        return normalized.Length <= maximumCharacters
            ? normalized
            : normalized[..maximumCharacters];
    }

    private static bool IsLongerThan(string? value, int maximumCharacters) =>
        value is not null && value.Length > maximumCharacters;

    private void PublishFallbackDecision(
        string? sessionId,
        bool fallbackRequired,
        string reason,
        string language,
        int renderDpi) =>
        _diagnostics?.Publish(
            sessionId,
            "OCR fallback decision",
            fallbackRequired ? DiagnosticStatus.Active : DiagnosticStatus.Skipped,
            DiagnosticSeverity.Information,
            DiagnosticSection.Overview,
            fallbackRequired ? "OCR fallback will run." : "OCR fallback will not run.",
            [
                new DiagnosticField("Fallback required", fallbackRequired.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticField("Fallback reason", reason),
                new DiagnosticField("Language configuration", language),
                new DiagnosticField("Render DPI", renderDpi.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ]);

    private static DiagnosticStatus ToDiagnosticStatus(OcrStatus status) => status switch
    {
        OcrStatus.Completed => DiagnosticStatus.Succeeded,
        OcrStatus.PartiallyCompleted or OcrStatus.TextNotIndexedDueToBounds => DiagnosticStatus.PartiallySucceeded,
        OcrStatus.Skipped or OcrStatus.Pending => DiagnosticStatus.Skipped,
        OcrStatus.Processing => DiagnosticStatus.Active,
        _ => DiagnosticStatus.Failed,
    };

    private static DiagnosticSeverity ToDiagnosticSeverity(OcrStatus status) => status switch
    {
        OcrStatus.Failed or OcrStatus.Unavailable => DiagnosticSeverity.Error,
        OcrStatus.PartiallyCompleted or OcrStatus.TextNotIndexedDueToBounds => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Information,
    };

    private static OcrResult Skipped(
        OcrRequest request,
        OcrFailureCategory category,
        string message)
    {
        var pages = request.PdfPages
            .OrderBy(page => page.PageNumber)
            .Select(page => page.HasReliableNativeText
                ? new OcrPageResult(
                    page.PageNumber,
                    OcrPageTextSource.NativeText,
                    OcrStatus.Skipped,
                    page.NativeText,
                    null,
                    "Reliable PDF-native text was retained.")
                {
                    RawText = page.RawNativeText,
                    NormalizedText = page.NativeText,
                    PreprocessingSteps = ["Native PDF text quality check; rasterization skipped"],
                }
                : new OcrPageResult(
                    page.PageNumber,
                    OcrPageTextSource.Skipped,
                    OcrStatus.Skipped,
                    null,
                    null,
                    message))
            .ToArray();
        return new OcrResult(
            OcrStatus.Skipped,
            null,
            null,
            null,
            pages.Length == 0 ? null : pages.Length,
            [],
            category,
            TimeSpan.Zero,
            "none",
            null,
            message)
        {
            Pages = Array.AsReadOnly(pages),
        };
    }

    private static OcrResult Failed(OcrFailureCategory category, string message) => new(
        OcrStatus.Failed,
        null,
        null,
        null,
        null,
        [],
        category,
        TimeSpan.Zero,
        "none",
        null,
        message);
}
