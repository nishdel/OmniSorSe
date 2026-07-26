using OpenSorSe.Application.AI;
using OpenSorSe.Application.ChangePlans;
using OpenSorSe.Application.Models;
using OpenSorSe.Executor;
using OpenSorSe.Executor.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Verifies AI and folder proposals become review-only generic Change Plans.</summary>
public sealed class SuggestionChangePlanFactoryTests
{
    /// <summary>Verifies an accepted rename is captured with source identity and AI correlation metadata.</summary>
    [Fact]
    public async Task CreateRenamePlanAsync_ProducesNonMutatingAiChangePlan()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.File("invoice.pdf", "pdf");
        var file = Result("file:1", path);
        var service = CreateService();
        var suggestion = new AiFileRenameSuggestion(
            "request:rename",
            file.Id,
            "invoice-2026.pdf",
            "The visible metadata identifies an invoice.",
            0.9,
            "ollama",
            "local-model",
            DateTimeOffset.UtcNow);

        var plan = await service.CreateRenamePlanAsync(
            file,
            suggestion,
            suggestion.SuggestedFileName,
            "scan:1",
            CancellationToken.None);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(ChangeActionType.RenameFile, action.ActionType);
        Assert.Equal(ChangeSuggestionSource.Ai, action.SuggestionSource);
        Assert.Equal("local-model", action.AiModel);
        Assert.Equal("request:rename", action.AiRequestCorrelationId);
        Assert.Equal("scan:1", plan.SourceScanId);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(directory.PathOf("invoice-2026.pdf")));
    }

    /// <summary>Verifies a complete folder suggestion becomes ordered move and required-directory actions.</summary>
    [Fact]
    public async Task CreateFolderStructurePlanAsync_MapsEveryKnownFileWithoutMutation()
    {
        using var directory = new TemporaryDirectory();
        var first = Result("file:1", directory.File("one.txt", "one"));
        var second = Result("file:2", directory.File("two.txt", "two"));
        var service = CreateService();
        var suggestion = new AiFolderStructurePlan(
            "request:folder",
            [
                new AiSuggestedFolder("folder:docs", "Documents", null, "Documents", "Documents group.", 0.8),
            ],
            [
                new AiFolderStructurePlanItem(first.Id, first.DisplayFileName, "Documents"),
                new AiFolderStructurePlanItem(second.Id, second.DisplayFileName, "Documents"),
            ],
            "Group both text documents.",
            "ollama",
            "local-model",
            DateTimeOffset.UtcNow);

        var plan = await service.CreateFolderStructurePlanAsync(
            [first, second],
            suggestion,
            "scan:2",
            CancellationToken.None);

        Assert.Equal(2, plan.Actions.Count(action => action.ActionType == ChangeActionType.MoveFile));
        Assert.Single(plan.Actions, action => action.ActionType == ChangeActionType.CreateDirectory);
        Assert.All(
            plan.Actions.Where(action => action.ActionType == ChangeActionType.MoveFile),
            action => Assert.Equal(ChangeSuggestionSource.Ai, action.SuggestionSource));
        Assert.True(File.Exists(first.FullPath));
        Assert.True(File.Exists(second.FullPath));
        Assert.False(Directory.Exists(directory.PathOf("Documents")));
    }

    /// <summary>Verifies malformed or partial model mappings never reach the generic plan factory.</summary>
    [Fact]
    public async Task CreateFolderStructurePlanAsync_RejectsPartialOrUnknownFileMappings()
    {
        using var directory = new TemporaryDirectory();
        var file = Result("file:1", directory.File("one.txt", "one"));
        var suggestion = new AiFolderStructurePlan(
            "request:folder",
            [],
            [new AiFolderStructurePlanItem("unknown", "one.txt", "Documents")],
            "Invalid mapping.",
            "ollama",
            "local-model",
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateService().CreateFolderStructurePlanAsync(
                [file],
                suggestion,
                "scan:3",
                CancellationToken.None));
        Assert.True(File.Exists(file.FullPath));
    }

    private static SuggestionChangePlanFactory CreateService()
    {
        var fileSystem = new PhysicalFileSystemGateway();
        var validator = new ChangePlanValidator(fileSystem);
        return new SuggestionChangePlanFactory(
            new ChangePlanFactory(fileSystem, validator, new InMemoryChangePlanStore()));
    }

    private static ResultFile Result(string id, string path)
    {
        var info = new FileInfo(path);
        return new ResultFile(
            id,
            path,
            info.Name,
            info.Extension.ToLowerInvariant(),
            info.Length,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            FileCategory.Document,
            "Document",
            DuplicateStatus.Unique,
            null,
            false);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"OpenSorSe.Application.ChangePlan.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public string PathOf(string name) => System.IO.Path.Combine(Path, name);

        public string File(string name, string contents)
        {
            var path = PathOf(name);
            System.IO.File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
