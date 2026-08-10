using OpenSorSe.Application.Content;
using OpenSorSe.Core.Platform;
using OpenSorSe.Core.Configuration;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies page-level OCR decisions, language detection, rendering, cleanup, and cache invalidation.</summary>
public sealed class OcrHardeningTests
{
    /// <summary>Verifies native-text quality rejects noise and accepts meaningful page text.</summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("........................................................", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("Invoice 2026-07-24 for local consulting services and project delivery.", true)]
    public void NativeTextQuality_ClassifiesDeterministically(string text, bool expected) =>
        Assert.Equal(expected, PdfNativeTextQuality.IsReliable(text));

    /// <summary>Verifies English/German detection and PDF readiness are reported without opening user files.</summary>
    [Fact]
    public async Task Capability_DetectsVersionsLanguagesAndRasterizer()
    {
        var runner = new FakeProcessRunner();
        var rasterizer = new FakeRasterizer(1);
        var engine = CreateEngine(rasterizer, runner);

        var capability = await engine.DetectCapabilityAsync(CancellationToken.None);

        Assert.True(capability.IsAvailable);
        Assert.True(capability.SupportsPdf);
        Assert.Equal(["deu", "eng"], capability.AvailableLanguages);
        Assert.Equal("fake-pdfium", capability.RasterizerIdentifier);
        Assert.Equal(2, runner.Calls.Count);
    }

    /// <summary>Verifies missing tool discovery stops before any process invocation.</summary>
    [Fact]
    public async Task Capability_MissingExecutableReportsDiscoveryReasonWithoutLaunching()
    {
        var runner = new FakeProcessRunner();
        var engine = new TesseractCliOcrEngine(
            new Configuration(),
            new FakeRasterizer(1),
            runner,
            new MissingToolLocator());

        var capability = await engine.DetectCapabilityAsync(CancellationToken.None);

        Assert.False(capability.IsAvailable);
        Assert.Contains("not installed", capability.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Calls);
    }

    /// <summary>Verifies a missing selected language pack prevents any recognition process.</summary>
    [Fact]
    public async Task MissingLanguage_IsRejectedBeforeRecognition()
    {
        var runner = new FakeProcessRunner { Languages = "eng" };
        var engine = CreateEngine(new FakeRasterizer(1), runner);

        var result = await engine.RecognizeAsync(
            Request("C:\\known.png") with { Language = "deu" },
            CancellationToken.None);

        Assert.Equal(OcrStatus.Unavailable, result.Status);
        Assert.Equal(OcrFailureCategory.EngineUnavailable, result.FailureCategory);
        Assert.Equal(2, runner.Calls.Count);
    }

    /// <summary>Verifies a mixed PDF rasterizes only the insufficient page and retains page provenance.</summary>
    [Fact]
    public async Task MixedPdf_RasterizesOnlyInsufficientPages_AndCleansWorkspace()
    {
        var runner = new FakeProcessRunner();
        var rasterizer = new FakeRasterizer(3);
        var engine = CreateEngine(rasterizer, runner);
        var pages = new[]
        {
            new PdfPageText(1, "This is reliable native text for the complete first document page.", true),
            new PdfPageText(2, "scan", false),
            new PdfPageText(3, "This is reliable native text for the complete third document page.", true),
        };

        var result = await engine.RecognizeAsync(
            Request("C:\\mixed.pdf") with { PdfPages = pages },
            CancellationToken.None);

        Assert.Equal(OcrStatus.Completed, result.Status);
        Assert.Equal([2], rasterizer.RenderedPages);
        Assert.Equal(3, result.Pages.Count);
        Assert.Equal(OcrPageTextSource.NativeText, result.Pages[0].TextSource);
        Assert.Null(result.Pages[0].Confidence);
        Assert.Equal(OcrPageTextSource.NativeAndOcrFallback, result.Pages[1].TextSource);
        Assert.Contains("[Page 2]", result.ExtractedText, StringComparison.Ordinal);
        Assert.True(rasterizer.WorkspaceDeleted);
    }

    /// <summary>Verifies caller cancellation propagates and still removes temporary page data.</summary>
    [Fact]
    public async Task MixedPdf_Cancellation_CleansWorkspace()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new FakeProcessRunner { BlockRecognition = true };
        var rasterizer = new FakeRasterizer(1);
        var engine = CreateEngine(rasterizer, runner);

        var running = engine.RecognizeAsync(
            Request("C:\\scan.pdf") with
            {
                PdfPages = [new PdfPageText(1, null, false)],
            },
            cancellation.Token);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await runner.RecognitionStarted.Task.WaitAsync(testTimeout.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(rasterizer.WorkspaceDeleted);
    }

    /// <summary>Verifies an oversized model output is discarded rather than partially indexed.</summary>
    [Fact]
    public async Task OversizedOcrOutput_IsDiscarded()
    {
        var runner = new FakeProcessRunner { TruncateRecognition = true };
        var engine = CreateEngine(new FakeRasterizer(1), runner);

        var result = await engine.RecognizeAsync(
            Request("C:\\known.png"),
            CancellationToken.None);

        Assert.Equal(OcrStatus.TextNotIndexedDueToBounds, result.Status);
        Assert.Null(result.ExtractedText);
    }

    /// <summary>Verifies a rendered page stopped by the temp bound keeps an explicit failed provenance record.</summary>
    [Fact]
    public async Task TemporaryStorageBound_RecordsFailedPage_AndCleansWorkspace()
    {
        var runner = new FakeProcessRunner();
        var rasterizer = new FakeRasterizer(1);
        var engine = CreateEngine(rasterizer, runner);

        var result = await engine.RecognizeAsync(
            Request("C:\\scan.pdf") with
            {
                PdfPages = [new PdfPageText(1, null, false)],
                MaximumTemporaryStorageBytes = 2,
            },
            CancellationToken.None);

        Assert.Equal(OcrStatus.Failed, result.Status);
        var page = Assert.Single(result.Pages);
        Assert.Equal(OcrPageTextSource.Failed, page.TextSource);
        Assert.Equal(OcrStatus.TextNotIndexedDueToBounds, page.Status);
        Assert.True(rasterizer.WorkspaceDeleted);
        Assert.Equal(2, runner.Calls.Count);
    }

    /// <summary>Verifies OCR settings and component versions participate in cache invalidation.</summary>
    [Fact]
    public void CacheFingerprint_ChangesWhenLanguageOrEngineChanges()
    {
        var settings = new ContentSettings { OcrEnabled = true };
        var first = ContentCacheFingerprint.Create(
            settings,
            Capability("5.5.2", "5.2.1"));
        var languageChanged = ContentCacheFingerprint.Create(
            new ContentSettings { OcrEnabled = true, OcrLanguage = "eng+deu" },
            Capability("5.5.2", "5.2.1"));
        var engineChanged = ContentCacheFingerprint.Create(
            settings,
            Capability("5.6.0", "5.2.1"));

        Assert.NotEqual(first, languageChanged);
        Assert.NotEqual(first, engineChanged);
    }

    /// <summary>Verifies the selected PDFium wrapper can render one real generated PDF page.</summary>
    [Fact]
    public async Task PdfRasterizer_RendersGeneratedPdf_AndDeletesOwnedWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opensorse-pdf-render-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var pdfPath = Path.Combine(root, "generated.pdf");
        try
        {
            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            var page = builder.AddPage(PageSize.A4);
            page.AddText("OpenSorSe local PDF rendering test", 18, new PdfPoint(40, 760), font);
            await File.WriteAllBytesAsync(pdfPath, builder.Build());
            var rasterizer = new PdfPageRasterizer();
            var workspace = rasterizer.CreateWorkspace();

            var rendered = await rasterizer.RenderAsync(
                pdfPath,
                1,
                150,
                2048,
                workspace,
                CancellationToken.None);

            Assert.True(rendered.EncodedBytes > 8);
            var signature = new byte[8];
            await using (var stream = File.OpenRead(rendered.ImagePath))
            {
                await stream.ReadExactlyAsync(signature);
            }

            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, signature);
            rasterizer.DeleteWorkspace(workspace);
            Assert.False(Directory.Exists(workspace));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static TesseractCliOcrEngine CreateEngine(
        IPdfPageRasterizer rasterizer,
        ITesseractProcessRunner runner) =>
        new(new Configuration(), rasterizer, runner, new AvailableToolLocator());

    private static OcrRequest Request(string path) => new(
        path,
        "eng",
        50L * 1024 * 1024,
        25,
        TimeSpan.FromSeconds(30),
        false);

    private static OcrCapability Capability(string engineVersion, string rasterizerVersion) =>
        new(true, "tesseract-cli", engineVersion, [".png", ".pdf"], true, "Available")
        {
            AvailableLanguages = ["eng"],
            RasterizerIdentifier = "pdftoimage-pdfium",
            RasterizerVersion = rasterizerVersion,
        };

    private sealed class Configuration : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = new()
        {
            Content = new ContentSettings { OcrEnabled = true },
        };

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class AvailableToolLocator : IExternalToolLocator
    {
        public ExternalToolLocation Locate(string commandName, string? configuredPath = null) =>
            new(true, configuredPath ?? commandName, "Available for the controlled process-runner test.");
    }

    private sealed class MissingToolLocator : IExternalToolLocator
    {
        public ExternalToolLocation Locate(string commandName, string? configuredPath = null) =>
            new(false, null, "The executable is not installed or not present on PATH.");
    }

    private sealed class FakeProcessRunner : ITesseractProcessRunner
    {
        public string Languages { get; init; } = "eng\ndeu";
        public bool BlockRecognition { get; init; }
        public bool TruncateRecognition { get; init; }
        public List<IReadOnlyList<string>> Calls { get; } = [];
        public TaskCompletionSource<bool> RecognitionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TesseractProcessResult> ExecuteAsync(
            string executable,
            IReadOnlyList<string> arguments,
            int maximumOutputCharacters,
            int maximumErrorCharacters,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Calls.Add(arguments.ToArray());
            if (arguments.SequenceEqual(["--version"]))
            {
                return new TesseractProcessResult(0, "tesseract 5.5.2", string.Empty, false, false);
            }

            if (arguments.SequenceEqual(["--list-langs"]))
            {
                return new TesseractProcessResult(
                    0,
                    $"List of available languages (2):\n{Languages}",
                    string.Empty,
                    false,
                    false);
            }

            RecognitionStarted.TrySetResult(true);
            if (BlockRecognition)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new TesseractProcessResult(
                0,
                "recognized bounded local page text",
                string.Empty,
                TruncateRecognition,
                false);
        }
    }

    private sealed class FakeRasterizer(int pageCount) : IPdfPageRasterizer
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"opensorse-fake-rasterizer-{Guid.NewGuid():N}");

        public List<int> RenderedPages { get; } = [];
        public bool WorkspaceDeleted { get; private set; }

        public Task<PdfRasterizerCapability> DetectCapabilityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PdfRasterizerCapability(true, "fake-pdfium", "1", "Available"));

        public Task<int> GetPageCountAsync(string fullPath, CancellationToken cancellationToken) =>
            Task.FromResult(pageCount);

        public async Task<RenderedPdfPage> RenderAsync(
            string fullPath,
            int pageNumber,
            int dpi,
            int maximumDimension,
            string workspacePath,
            CancellationToken cancellationToken)
        {
            RenderedPages.Add(pageNumber);
            var path = Path.Combine(workspacePath, $"page-{pageNumber:D4}.png");
            await File.WriteAllBytesAsync(path, [1, 2, 3], cancellationToken);
            return new RenderedPdfPage(pageNumber, path, 3);
        }

        public string CreateWorkspace()
        {
            Directory.CreateDirectory(_root);
            return _root;
        }

        public void DeleteWorkspace(string workspacePath)
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, recursive: true);
            }

            WorkspaceDeleted = true;
        }
    }
}
