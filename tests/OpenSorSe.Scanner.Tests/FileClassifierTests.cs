using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Scanner.Tests;

/// <summary>Verifies deterministic metadata classification.</summary>
public sealed class FileClassifierTests
{
    /// <summary>Verifies every built-in category has a representative mapping.</summary>
    [Theory]
    [InlineData(".txt", FileCategory.Document)]
    [InlineData(".csv", FileCategory.Spreadsheet)]
    [InlineData(".ppt", FileCategory.Presentation)]
    [InlineData(".jpg", FileCategory.Image)]
    [InlineData(".mp3", FileCategory.Audio)]
    [InlineData(".mp4", FileCategory.Video)]
    [InlineData(".zip", FileCategory.Archive)]
    [InlineData(".cs", FileCategory.Code)]
    [InlineData(".db", FileCategory.Data)]
    [InlineData(".exe", FileCategory.Executable)]
    [InlineData(".ttf", FileCategory.Font)]
    public async Task ClassifyAsync_ClassifiesBuiltInExtensions(string extension, FileCategory category)
    {
        var result = await CreateClassifier().ClassifyAsync(new[] { Entry("sample" + extension) });
        Assert.Equal(category, result.Files[0].Classification!.Category);
    }

    /// <summary>Verifies ordered custom rules replace defaults and match case-insensitively.</summary>
    [Fact]
    public async Task ClassifyAsync_UsesFirstCustomMatch()
    {
        var options = new FileClassificationOptions(new[] { new FileClassificationRule("name", FileClassificationMatchKind.ExactFileName, "README.MD", FileCategory.Data), new FileClassificationRule("extension", FileClassificationMatchKind.Extension, ".MD", FileCategory.Document) });
        var result = await CreateClassifier().ClassifyAsync(new[] { Entry("readme.MD") }, options);
        Assert.Equal(FileCategory.Data, result.Files[0].Classification!.Category);
    }

    /// <summary>Verifies custom rules replace defaults and built-in matching ignores case.</summary>
    [Fact]
    public async Task ClassifyAsync_ReplacesDefaultsAndMatchesExtensionsIgnoringCase()
    {
        var defaultResult = await CreateClassifier().ClassifyAsync(new[] { Entry("SAMPLE.PDF") });
        var customResult = await CreateClassifier().ClassifyAsync(
            new[] { Entry("SAMPLE.PDF") },
            new FileClassificationOptions(new[] { new FileClassificationRule("only-font", FileClassificationMatchKind.Extension, ".ttf", FileCategory.Font) }));

        Assert.Equal(FileCategory.Document, defaultResult.Files[0].Classification!.Category);
        Assert.Equal(FileCategory.Unknown, customResult.Files[0].Classification!.Category);
        Assert.Empty(customResult.Issues);
    }

    /// <summary>Verifies unknown and missing metadata behavior preserves input values.</summary>
    [Fact]
    public async Task ClassifyAsync_PreservesOrderDuplicatesAndReportsMissingMetadata()
    {
        var hash = new FileHash("SHA-256", "a"); var known = Entry("none", hash: hash); var missing = new FileEntry("missing", Hash: hash);
        var result = await CreateClassifier().ClassifyAsync(new[] { known, missing, known });
        Assert.Equal(new[] { "none", "missing", "none" }, result.Files.Select(file => file.FullPath));
        Assert.All(result.Files, file => Assert.Same(hash, file.Hash));
        Assert.Equal(FileCategory.Unknown, result.Files[0].Classification!.Category);
        Assert.Single(result.Issues); Assert.Null(known.Classification);
    }

    /// <summary>Verifies invalid rules are rejected before processing and cancellation is deterministic.</summary>
    [Fact]
    public async Task ClassifyAsync_ValidatesRulesAndCancellation()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateClassifier().ClassifyAsync(new[] { Entry("x.txt") }, new FileClassificationOptions(new[] { new FileClassificationRule("bad", FileClassificationMatchKind.Extension, "txt", FileCategory.Document) })));
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateClassifier().ClassifyAsync(Array.Empty<FileEntry>(), cancellationToken: source.Token));
    }

    /// <summary>Verifies every documented invalid-rule boundary is rejected before processing.</summary>
    [Theory]
    [InlineData("", FileClassificationMatchKind.Extension, ".txt", FileCategory.Document)]
    [InlineData("valid", FileClassificationMatchKind.Extension, "", FileCategory.Document)]
    [InlineData("valid", FileClassificationMatchKind.Extension, ".txt", FileCategory.Unknown)]
    [InlineData("valid", FileClassificationMatchKind.Extension, "txt", FileCategory.Document)]
    [InlineData("valid", (FileClassificationMatchKind)999, ".txt", FileCategory.Document)]
    public async Task ClassifyAsync_RejectsInvalidRules(string id, FileClassificationMatchKind matchKind, string pattern, FileCategory category)
    {
        var rules = new[] { new FileClassificationRule(id, matchKind, pattern, category) };
        await Assert.ThrowsAsync<ArgumentException>(() => CreateClassifier().ClassifyAsync(new[] { Entry("x.txt") }, new FileClassificationOptions(rules)));
        var duplicateRules = new[] { new FileClassificationRule("same", FileClassificationMatchKind.Extension, ".txt", FileCategory.Document), new FileClassificationRule("SAME", FileClassificationMatchKind.Extension, ".md", FileCategory.Document) };
        await Assert.ThrowsAsync<ArgumentException>(() => CreateClassifier().ClassifyAsync(new[] { Entry("x.txt") }, new FileClassificationOptions(duplicateRules)));
    }

    /// <summary>Verifies empty input and existing classifications are handled deterministically.</summary>
    [Fact]
    public async Task ClassifyAsync_ReplacesExistingClassificationAndHandlesEmptyInput()
    {
        var entry = Entry("file.txt") with { Classification = new FileClassification(FileCategory.Image) };
        var result = await CreateClassifier().ClassifyAsync(new[] { entry });
        var empty = await CreateClassifier().ClassifyAsync(Array.Empty<FileEntry>());

        Assert.Equal(FileCategory.Document, result.Files[0].Classification!.Category);
        Assert.Equal(FileCategory.Image, entry.Classification!.Category);
        Assert.Equal(new FileClassificationStatistics(0, 0, 0, 0), empty.Statistics);
    }

    private static FileEntry Entry(string name, FileHash? hash = null) => new(name, new FileMetadata(name, Path.GetExtension(name), 0, null, null, null, FileAttributes.Normal), hash);
    private static FileClassifier CreateClassifier() => new(new TestLoggingService(), new TestErrorHandler());

    private sealed class TestLoggingService : ILoggingService
    {
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public void Dispose()
        {
        }

        public void Initialize(LogLevel minimumLevel)
        {
        }
    }

    private sealed class TestErrorHandler : IErrorHandler
    {
        public event EventHandler<ApplicationError>? ErrorReported;

        public void Report(ApplicationError applicationError) => ErrorReported?.Invoke(this, applicationError);
    }
}
