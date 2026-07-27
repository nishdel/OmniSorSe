using System.Diagnostics;
using System.Text;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Application.Content;

/// <summary>Integrates an externally managed local Tesseract CLI with bounded image and mixed-PDF OCR.</summary>
public sealed class TesseractCliOcrEngine : IOcrEngine
{
    private static readonly IReadOnlyList<string> Extensions =
        Array.AsReadOnly([".png", ".jpg", ".jpeg", ".tif", ".tiff", ".pdf"]);
    private readonly SemaphoreSlim _capabilityMutex = new(1, 1);
    private readonly IConfigurationService _configurationService;
    private readonly IPdfPageRasterizer _rasterizer;
    private readonly ITesseractProcessRunner _processRunner;
    private readonly IExternalToolLocator _toolLocator;
    private OcrCapability? _cachedCapability;

    /// <summary>Initializes the local engine with current settings and the bounded PDF renderer.</summary>
    public TesseractCliOcrEngine(
        IConfigurationService configurationService,
        IPdfPageRasterizer rasterizer,
        ITesseractProcessRunner processRunner)
        : this(
            configurationService,
            rasterizer,
            processRunner,
            new ExternalToolLocator())
    {
    }

    /// <summary>Initializes the local engine with explicit cross-platform tool discovery.</summary>
    public TesseractCliOcrEngine(
        IConfigurationService configurationService,
        IPdfPageRasterizer rasterizer,
        ITesseractProcessRunner processRunner,
        IExternalToolLocator toolLocator)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _toolLocator = toolLocator ?? throw new ArgumentNullException(nameof(toolLocator));
    }

    /// <inheritdoc />
    public async Task<OcrCapability> DetectCapabilityAsync(CancellationToken cancellationToken)
    {
        if (_cachedCapability is not null)
        {
            return _cachedCapability;
        }

        await _capabilityMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedCapability is not null)
            {
                return _cachedCapability;
            }

            var location = LocateExecutable();
            if (!location.IsAvailable || string.IsNullOrWhiteSpace(location.ExecutablePath))
            {
                return Unavailable($"Tesseract is unavailable: {location.Explanation}");
            }

            var executable = location.ExecutablePath;
            try
            {
                var versionResult = await _processRunner.ExecuteAsync(
                    executable,
                    ["--version"],
                    512,
                    1024,
                    TimeSpan.FromSeconds(3),
                    cancellationToken).ConfigureAwait(false);
                if (versionResult.ExitCode != 0 || versionResult.StandardOutputTruncated)
                {
                    return Unavailable("Tesseract was found but did not return a valid bounded version response.");
                }

                var languagesResult = await _processRunner.ExecuteAsync(
                    executable,
                    ["--list-langs"],
                    4096,
                    2048,
                    TimeSpan.FromSeconds(3),
                    cancellationToken).ConfigureAwait(false);
                if (languagesResult.ExitCode != 0 || languagesResult.StandardOutputTruncated)
                {
                    return Unavailable("Tesseract language packs could not be detected.");
                }

                var languages = ParseLanguages(languagesResult.StandardOutput);
                if (languages.Count == 0)
                {
                    return Unavailable("Tesseract reported no installed language packs.");
                }

                var rasterizer = await _rasterizer
                    .DetectCapabilityAsync(cancellationToken)
                    .ConfigureAwait(false);
                var version = versionResult.StandardOutput
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                return _cachedCapability = new OcrCapability(
                    true,
                    "tesseract-cli",
                    ContentText.NormalizeField(version, 128),
                    Extensions,
                    rasterizer.IsAvailable,
                    rasterizer.IsAvailable
                        ? "Local Tesseract image and scanned-PDF OCR are available."
                        : "Local Tesseract image OCR is available, but PDF page rendering is unavailable.")
                {
                    AvailableLanguages = languages,
                    RasterizerIdentifier = rasterizer.IsAvailable ? rasterizer.Identifier : null,
                    RasterizerVersion = rasterizer.IsAvailable ? rasterizer.Version : null,
                };
            }
            catch (Exception exception) when (
                exception is System.ComponentModel.Win32Exception or
                InvalidOperationException or
                IOException or
                OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return Unavailable("No compatible local Tesseract CLI was detected. Install it and the selected language packs, then recheck.");
            }
        }
        finally
        {
            _capabilityMutex.Release();
        }
    }

    /// <summary>Clears a successful capability snapshot so an explicit UI recheck observes installation changes.</summary>
    public void ResetCapability() => _cachedCapability = null;

    /// <inheritdoc />
    public async Task<OcrResult> RecognizeAsync(
        OcrRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = Stopwatch.StartNew();
        var capability = await DetectCapabilityAsync(cancellationToken).ConfigureAwait(false);
        if (!capability.IsAvailable)
        {
            return Failure(
                OcrStatus.Unavailable,
                OcrFailureCategory.EngineUnavailable,
                capability,
                started.Elapsed,
                capability.Message);
        }

        var requestedLanguages = ParseRequestedLanguages(request.Language);
        var missingLanguages = requestedLanguages
            .Where(language => !capability.AvailableLanguages.Contains(language, StringComparer.Ordinal))
            .ToArray();
        if (missingLanguages.Length > 0)
        {
            return Failure(
                OcrStatus.Unavailable,
                OcrFailureCategory.EngineUnavailable,
                capability,
                started.Elapsed,
                $"The selected Tesseract language pack is unavailable: {string.Join(", ", missingLanguages)}.");
        }

        var extension = Path.GetExtension(request.FullPath).ToLowerInvariant();
        if (!capability.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Failure(
                OcrStatus.Unavailable,
                OcrFailureCategory.UnsupportedInput,
                capability,
                started.Elapsed,
                "The available local OCR engine does not support this file type.");
        }

        return extension == ".pdf"
            ? await RecognizePdfAsync(request, capability, started, cancellationToken).ConfigureAwait(false)
            : await RecognizeImageAsync(
                request.FullPath,
                request,
                capability,
                started,
                cancellationToken).ConfigureAwait(false);
    }

    private async Task<OcrResult> RecognizePdfAsync(
        OcrRequest request,
        OcrCapability capability,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        if (!capability.SupportsPdf)
        {
            return Failure(
                OcrStatus.Unavailable,
                OcrFailureCategory.EngineUnavailable,
                capability,
                started.Elapsed,
                "The bundled local PDF page renderer is unavailable.");
        }

        string? workspace = null;
        var pageResults = new List<OcrPageResult>();
        var warnings = new List<string>();
        var combinedOcrText = new StringBuilder();
        var temporaryBytes = 0L;
        var failedPages = 0;
        try
        {
            var pageCount = await _rasterizer
                .GetPageCountAsync(request.FullPath, cancellationToken)
                .ConfigureAwait(false);
            if (pageCount < 1)
            {
                return Failure(
                    OcrStatus.Failed,
                    OcrFailureCategory.MalformedInput,
                    capability,
                    started.Elapsed,
                    "The PDF contains no readable pages.");
            }

            var pagesToInspect = Math.Min(pageCount, request.MaximumPages);
            if (pageCount > request.MaximumPages)
            {
                warnings.Add(
                    $"The PDF has {pageCount} pages; OCR was limited to the first {request.MaximumPages} pages.");
            }

            var nativePages = request.PdfPages.ToDictionary(page => page.PageNumber);
            for (var pageNumber = 1; pageNumber <= pagesToInspect; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                nativePages.TryGetValue(pageNumber, out var native);
                if (!request.ForceReprocessAllPages && native?.HasReliableNativeText == true)
                {
                    pageResults.Add(new OcrPageResult(
                        pageNumber,
                        OcrPageTextSource.NativeText,
                        OcrStatus.Skipped,
                        native.NativeText,
                        null,
                        "Reliable native page text was retained; rasterization was skipped.")
                    {
                        RawText = native.RawNativeText,
                        NormalizedText = native.NativeText,
                        PreprocessingSteps = ["Native PDF text quality check; rasterization skipped"],
                    });
                    continue;
                }

                workspace ??= _rasterizer.CreateWorkspace();
                RenderedPdfPage rendered;
                try
                {
                    rendered = await _rasterizer.RenderAsync(
                        request.FullPath,
                        pageNumber,
                        request.RasterizationDpi,
                        request.MaximumRasterDimension,
                        workspace,
                        cancellationToken).ConfigureAwait(false);
                    temporaryBytes += rendered.EncodedBytes;
                    if (temporaryBytes > request.MaximumTemporaryStorageBytes)
                    {
                        TryDeleteFile(rendered.ImagePath);
                        warnings.Add("PDF OCR stopped at the configured temporary-storage bound.");
                        failedPages++;
                        pageResults.Add(new OcrPageResult(
                            pageNumber,
                            OcrPageTextSource.Failed,
                            OcrStatus.TextNotIndexedDueToBounds,
                            null,
                            null,
                            "The rendered page exceeded the configured temporary-storage bound."));
                        break;
                    }

                    var pageRequest = request with
                    {
                        FullPath = rendered.ImagePath,
                        HasReliableNativeText = false,
                        PdfPages = [],
                    };
                    var pageResult = await RecognizeImageAsync(
                        rendered.ImagePath,
                        pageRequest,
                        capability,
                        Stopwatch.StartNew(),
                        cancellationToken).ConfigureAwait(false);
                    if (pageResult.HasText)
                    {
                        var source = string.IsNullOrWhiteSpace(native?.NativeText)
                            ? OcrPageTextSource.Ocr
                            : OcrPageTextSource.NativeAndOcrFallback;
                        pageResults.Add(new OcrPageResult(
                            pageNumber,
                            source,
                            pageResult.Status,
                            pageResult.ExtractedText,
                            pageResult.Confidence,
                            pageResult.Message)
                        {
                            RawText = pageResult.RawExtractedText,
                            NormalizedText = pageResult.NormalizedText ?? pageResult.ExtractedText,
                            RenderDpi = request.RasterizationDpi,
                            RenderedWidth = rendered.Width,
                            RenderedHeight = rendered.Height,
                            PreprocessingSteps = ["Rendered PDF page to bounded grayscale PNG; no additional preprocessing"],
                            ProcessingDuration = pageResult.ProcessingDuration,
                        });
                        AppendPageBounded(
                            combinedOcrText,
                            pageNumber,
                            pageResult.ExtractedText!,
                            request.MaximumTextCharacters);
                    }
                    else
                    {
                        failedPages++;
                        pageResults.Add(new OcrPageResult(
                            pageNumber,
                            OcrPageTextSource.Failed,
                            pageResult.Status,
                            null,
                            null,
                            pageResult.Message));
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or InvalidDataException or
                    UnauthorizedAccessException or ArgumentException or
                    TypeInitializationException or DllNotFoundException)
                {
                    failedPages++;
                    pageResults.Add(new OcrPageResult(
                        pageNumber,
                        OcrPageTextSource.Failed,
                        OcrStatus.Failed,
                        null,
                        null,
                        "The PDF page could not be rendered or recognized locally."));
                }
                finally
                {
                    if (workspace is not null)
                    {
                        TryDeletePageFile(workspace, pageNumber);
                    }
                }
            }

            var wasBounded = combinedOcrText.Length >= request.MaximumTextCharacters ||
                             pageCount > request.MaximumPages ||
                             temporaryBytes > request.MaximumTemporaryStorageBytes;
            var hasOcrText = combinedOcrText.Length > 0;
            var status = hasOcrText
                ? failedPages > 0 || wasBounded ? OcrStatus.PartiallyCompleted : OcrStatus.Completed
                : failedPages > 0 ? OcrStatus.Failed : OcrStatus.Skipped;
            var failure = hasOcrText || failedPages == 0
                ? OcrFailureCategory.None
                : OcrFailureCategory.EngineFailure;
            return new OcrResult(
                status,
                hasOcrText ? ContentText.Normalize(combinedOcrText.ToString()) : null,
                request.Language,
                null,
                pageCount,
                Array.AsReadOnly(warnings.Distinct(StringComparer.Ordinal).Take(16).ToArray()),
                failure,
                started.Elapsed,
                capability.EngineIdentifier,
                capability.EngineVersion,
                status switch
                {
                    OcrStatus.Completed => "Local PDF OCR completed.",
                    OcrStatus.PartiallyCompleted => "Local PDF OCR completed partially; review page details.",
                    OcrStatus.Skipped => "Every bounded PDF page already had reliable native text.",
                    _ => "Local PDF OCR could not extract usable text.",
                })
            {
                Pages = Array.AsReadOnly(pageResults.ToArray()),
                RasterizerIdentifier = capability.RasterizerIdentifier,
                RawExtractedText = CombineRawPageText(pageResults, request.MaximumTextCharacters),
                NormalizedText = hasOcrText ? ContentText.Normalize(combinedOcrText.ToString()) : null,
                DownstreamText = hasOcrText ? ContentText.Normalize(combinedOcrText.ToString()) : null,
                WasTruncated = wasBounded,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or
            ArgumentException or TypeInitializationException or DllNotFoundException)
        {
            return Failure(
                OcrStatus.Failed,
                OcrFailureCategory.MalformedInput,
                capability,
                started.Elapsed,
                "The PDF could not be opened safely for local OCR.");
        }
        finally
        {
            if (workspace is not null)
            {
                try
                {
                    _rasterizer.DeleteWorkspace(workspace);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private async Task<OcrResult> RecognizeImageAsync(
        string inputPath,
        OcrRequest request,
        OcrCapability capability,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        try
        {
            var execution = await _processRunner.ExecuteAsync(
                GetExecutable(),
                [inputPath, "stdout", "-l", request.Language],
                request.MaximumTextCharacters + 1,
                2048,
                request.Timeout,
                cancellationToken).ConfigureAwait(false);
            if (execution.ExitCode != 0)
            {
                return Failure(
                    OcrStatus.Failed,
                    OcrFailureCategory.EngineFailure,
                    capability,
                    started.Elapsed,
                    TranslateTesseractError(execution.StandardError));
            }

            if (execution.StandardOutputTruncated)
            {
                return Failure(
                    OcrStatus.TextNotIndexedDueToBounds,
                    OcrFailureCategory.EngineFailure,
                    capability,
                    started.Elapsed,
                    "The local OCR response exceeded the configured text bound and was discarded.") with
                {
                    WasTruncated = true,
                };
            }

            var normalized = ContentText.Normalize(execution.StandardOutput);
            if (normalized.Length == 0)
            {
                return Failure(
                    OcrStatus.Failed,
                    OcrFailureCategory.EmptyText,
                    capability,
                    started.Elapsed,
                    "The local OCR engine returned no usable text.");
            }

            return new OcrResult(
                OcrStatus.Completed,
                normalized,
                request.Language,
                null,
                1,
                [],
                OcrFailureCategory.None,
                started.Elapsed,
                capability.EngineIdentifier,
                capability.EngineVersion,
                "OCR completed locally.")
            {
                RawExtractedText = execution.StandardOutput,
                NormalizedText = normalized,
                DownstreamText = normalized,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failure(
                OcrStatus.Failed,
                OcrFailureCategory.Timeout,
                capability,
                started.Elapsed,
                "Local OCR timed out.");
        }
        catch (Exception exception) when (
            exception is IOException or
            System.ComponentModel.Win32Exception or
            InvalidOperationException)
        {
            return Failure(
                OcrStatus.Failed,
                OcrFailureCategory.EngineFailure,
                capability,
                started.Elapsed,
                "The local OCR engine could not process this file.");
        }
    }

    private string GetExecutable()
    {
        var location = LocateExecutable();
        return location.IsAvailable && !string.IsNullOrWhiteSpace(location.ExecutablePath)
            ? location.ExecutablePath
            : throw new InvalidOperationException(location.Explanation);
    }

    private ExternalToolLocation LocateExecutable() =>
        _toolLocator.Locate(
            "tesseract",
            _configurationService.Current.Content.TesseractExecutablePath);

    internal static IReadOnlyList<string> ParseLanguages(string? output) =>
        Array.AsReadOnly((output ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("List of available languages", StringComparison.OrdinalIgnoreCase))
            .Where(language => language.Length is > 0 and <= 32)
            .Where(language => language.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(language => language, StringComparer.Ordinal)
            .Take(128)
            .ToArray());

    private static IReadOnlyList<string> ParseRequestedLanguages(string language) =>
        language.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void AppendPageBounded(
        StringBuilder output,
        int pageNumber,
        string text,
        int maximumCharacters)
    {
        var prefix = output.Length == 0
            ? $"[Page {pageNumber}] "
            : $" [Page {pageNumber}] ";
        var remaining = maximumCharacters - output.Length;
        if (remaining <= 0)
        {
            return;
        }

        output.Append(prefix.AsSpan(0, Math.Min(prefix.Length, remaining)));
        remaining = maximumCharacters - output.Length;
        if (remaining > 0)
        {
            output.Append(text.AsSpan(0, Math.Min(text.Length, remaining)));
        }
    }

    private static string? CombineRawPageText(
        IReadOnlyList<OcrPageResult> pages,
        int maximumCharacters)
    {
        var output = new StringBuilder();
        foreach (var page in pages.Where(page => !string.IsNullOrEmpty(page.RawText)))
        {
            AppendPageBounded(output, page.PageNumber, page.RawText!, maximumCharacters);
        }

        return output.Length == 0 ? null : output.ToString();
    }

    private static string TranslateTesseractError(string error)
    {
        if (error.Contains("Failed loading language", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Error opening data file", StringComparison.OrdinalIgnoreCase))
        {
            return "Tesseract could not load the selected language pack.";
        }

        if (error.Contains("Image too large", StringComparison.OrdinalIgnoreCase))
        {
            return "The rendered image exceeded the local OCR engine's supported dimensions.";
        }

        return "The local OCR engine could not process this input.";
    }

    private static void TryDeletePageFile(string workspace, int pageNumber) =>
        TryDeleteFile(Path.Combine(
            workspace,
            $"page-{pageNumber.ToString("D4", System.Globalization.CultureInfo.InvariantCulture)}.png"));

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static OcrCapability Unavailable(string message) => new(
        false,
        "tesseract-cli",
        null,
        Extensions,
        false,
        message);

    private static OcrResult Failure(
        OcrStatus status,
        OcrFailureCategory category,
        OcrCapability capability,
        TimeSpan duration,
        string message) => new(
            status,
            null,
            null,
            null,
            null,
            [],
            category,
            duration,
            capability.EngineIdentifier,
            capability.EngineVersion,
            message);

}
