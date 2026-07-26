using System.Text.Json;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Exercises response patterns commonly emitted by small local instruction models.</summary>
public sealed class AiSmallModelCompatibilityTests
{
    private readonly AiResponseParser _parser = new();

    /// <summary>Valid exact JSON and explicit no-suggestion both satisfy the v2 rename contract.</summary>
    [Theory]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"invoice-draft","reason":"Clearer order.","confidence":0.5}""", false)]
    [InlineData("""{"taskId":"file-rename-v2","status":"no_suggestion","reason":"The evidence is ambiguous."}""", true)]
    public void Rename_ExactJson_IsAccepted(string json, bool noSuggestion)
    {
        var result = ParseRename(json);

        Assert.True(result.IsValid);
        Assert.Equal(noSuggestion, result.IsNoSuggestion);
    }

    /// <summary>Formatting and type mistakes are diagnosable and eligible for only the service's one repair pass.</summary>
    [Theory]
    [InlineData("```json\n{\"taskId\":\"file-rename-v2\"}\n```", "Markdown")]
    [InlineData("Here is the JSON: {\"taskId\":\"file-rename-v2\"}", "malformed JSON")]
    [InlineData("""{"TaskId":"file-rename-v2","status":"no_suggestion","reason":"Ambiguous."}""", "taskId")]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"invoice-draft"}""", "reason")]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"invoice-draft","reason":{"text":"Clearer."}}""", "received object")]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"invoice-draft","reason":["Clearer."]}""", "received array")]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"invoice-draft","reason":"Clearer.","confidence":"high"}""", "confidence")]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"invoice-draft","reason":"Clearer.","confidence":1.5}""", "confidence")]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":["invoice-draft","draft-invoice"],"reason":"Two choices."}""", "suggestedStem")]
    [InlineData("""{"taskId":"file-rename-v2","status":"no_suggestion","reason":"Ambiguous."} trailing prose""", "malformed JSON")]
    [InlineData("""{"taskId":"file-rename-v2","status":"no_suggestion","reason":"Ambiguous."}{"taskId":"file-rename-v2","status":"no_suggestion","reason":"Again."}""", "malformed JSON")]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"invoice-draft","reason":"Clearer." """, "malformed JSON")]
    public void Rename_RepairableSmallModelShapeFailure_IsUnderstandable(
        string json,
        string expectedMessage)
    {
        var result = ParseRename(json);

        Assert.False(result.IsValid);
        Assert.True(result.CanRepair);
        Assert.Contains(expectedMessage, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Identity, path, extension, and invented-evidence failures reject the whole response without repair.</summary>
    [Theory]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"unknown","suggestedStem":"invoice-draft","reason":"Clearer."}""", AiResponseFailureKind.UnsafeIdentity)]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"C:\\private\\invoice","reason":"Clearer."}""", AiResponseFailureKind.UnsafePath)]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"example-filename","reason":"Copied example."}""", AiResponseFailureKind.ModelMisuse)]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"invoice-draft.pdf","reason":"Extension leaked."}""", AiResponseFailureKind.ModelMisuse)]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"CON","reason":"Reserved name."}""", AiResponseFailureKind.ModelMisuse)]
    [InlineData("""{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"invoice-draft","reason":"Clearer.","metadata":{"issuer":"Invented"}}""", AiResponseFailureKind.ModelMisuse)]
    public void Rename_UnsafeOrInventedResponse_IsNotRepairable(
        string json,
        AiResponseFailureKind expected)
    {
        var result = ParseRename(json);

        Assert.False(result.IsValid);
        Assert.False(result.CanRepair);
        Assert.Equal(expected, result.FailureKind);
    }

    /// <summary>Every supplied file is accepted only when assigned once to a declared safe folder.</summary>
    [Fact]
    public void Folder_ExactAssignments_AreAccepted()
    {
        var result = ParseFolder(
            FolderJson(
                """
                [{"folderId":"folder-001","name":"Document","parentFolderId":null},
                 {"folderId":"folder-002","name":"Other","parentFolderId":null}]
                """,
                """
                [{"sourceFileId":"item-001","folderId":"folder-001"},
                 {"sourceFileId":"item-002","folderId":"folder-002"}]
                """));

        Assert.True(result.IsValid);
        Assert.Equal(["known:a", "known:b"], result.Value!.Items.Select(item => item.FileId));
    }

    /// <summary>Duplicate and missing assignments are identity failures rejected without repair.</summary>
    [Theory]
    [InlineData("""[{"sourceFileId":"item-001","folderId":"folder-001"},{"sourceFileId":"item-001","folderId":"folder-001"}]""", "same source file")]
    [InlineData("""[{"sourceFileId":"item-001","folderId":"folder-001"}]""", "exactly one assignment")]
    public void Folder_DuplicateOrMissingAssignment_IsRejected(
        string assignments,
        string expectedMessage)
    {
        var result = ParseFolder(
            FolderJson(
                """[{"folderId":"folder-001","name":"Document","parentFolderId":null}]""",
                assignments));

        Assert.False(result.IsValid);
        Assert.False(result.CanRepair);
        Assert.Contains(expectedMessage, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Unknown opaque IDs and path attempts are unsafe and never repaired.</summary>
    [Theory]
    [InlineData(
        """[{"folderId":"folder-001","name":"Document","parentFolderId":null}]""",
        """[{"sourceFileId":"unknown","folderId":"folder-001"},{"sourceFileId":"item-002","folderId":"folder-001"}]""",
        AiResponseFailureKind.UnsafeIdentity)]
    [InlineData(
        """[{"folderId":"folder-001","name":"C:\\Private","parentFolderId":null}]""",
        """[{"sourceFileId":"item-001","folderId":"folder-001"},{"sourceFileId":"item-002","folderId":"folder-001"}]""",
        AiResponseFailureKind.UnsafePath)]
    [InlineData(
        """[{"folderId":"folder-001","name":"Document","parentFolderId":"../"}]""",
        """[{"sourceFileId":"item-001","folderId":"folder-001"},{"sourceFileId":"item-002","folderId":"folder-001"}]""",
        AiResponseFailureKind.UnsafePath)]
    public void Folder_UnsafeIdentityOrPath_IsNotRepairable(
        string folders,
        string assignments,
        AiResponseFailureKind expected)
    {
        var result = ParseFolder(FolderJson(folders, assignments));

        Assert.False(result.IsValid);
        Assert.False(result.CanRepair);
        Assert.Equal(expected, result.FailureKind);
    }

    /// <summary>Excessive hierarchy depth fails the independent deterministic relationship validator.</summary>
    [Fact]
    public void Folder_ExcessiveDepth_IsRejected()
    {
        var result = ParseFolder(
            FolderJson(
                """
                [{"folderId":"folder-001","name":"Document","parentFolderId":null},
                 {"folderId":"folder-002","name":"Other","parentFolderId":"folder-001"},
                 {"folderId":"folder-003","name":"Archive","parentFolderId":"folder-002"},
                 {"folderId":"folder-004","name":"Review","parentFolderId":"folder-003"}]
                """,
                """
                [{"sourceFileId":"item-001","folderId":"folder-004"},
                 {"sourceFileId":"item-002","folderId":"folder-004"}]
                """),
            ["Document", "Other", "Archive", "Review"]);

        Assert.False(result.IsValid);
        Assert.Contains("maximum folder depth", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Unknown folder references, duplicate folder IDs, and cycles reject the complete plan.</summary>
    [Theory]
    [InlineData(
        """[{"folderId":"folder-001","name":"Document","parentFolderId":null}]""",
        """[{"sourceFileId":"item-001","folderId":"folder-999"},{"sourceFileId":"item-002","folderId":"folder-001"}]""",
        "unknown source file or folder")]
    [InlineData(
        """[{"folderId":"folder-001","name":"Document","parentFolderId":null},{"folderId":"folder-001","name":"Other","parentFolderId":null}]""",
        """[{"sourceFileId":"item-001","folderId":"folder-001"},{"sourceFileId":"item-002","folderId":"folder-001"}]""",
        "duplicate folder identities")]
    [InlineData(
        """[{"folderId":"folder-001","name":"Document","parentFolderId":"folder-002"},{"folderId":"folder-002","name":"Other","parentFolderId":"folder-001"}]""",
        """[{"sourceFileId":"item-001","folderId":"folder-001"},{"sourceFileId":"item-002","folderId":"folder-002"}]""",
        "circular")]
    public void Folder_InvalidRelationships_AreRejectedAsAWhole(
        string folders,
        string assignments,
        string expectedMessage)
    {
        var result = ParseFolder(FolderJson(folders, assignments));

        Assert.False(result.IsValid);
        Assert.False(result.CanRepair);
        Assert.Contains(expectedMessage, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The model cannot exceed the independently enforced eight-folder contract.</summary>
    [Fact]
    public void Folder_ExcessiveFolderCount_IsRejected()
    {
        var folders = Enumerable.Range(1, AiResponseLimits.MaximumFolders + 1)
            .Select(index => new
            {
                folderId = $"folder-{index:D3}",
                name = $"Folder{index}",
                parentFolderId = (string?)null,
            })
            .ToArray();
        var json = JsonSerializer.Serialize(new
        {
            taskId = AiPromptBuilder.FolderStructureTaskId,
            status = "suggestion",
            folders,
            assignments = new[]
            {
                new { sourceFileId = "item-001", folderId = "folder-001" },
                new { sourceFileId = "item-002", folderId = "folder-002" },
            },
            reason = "Too many folders.",
        });

        var result = ParseFolder(
            json,
            folders.Select(folder => folder.name).Append("Other").ToArray());

        Assert.False(result.IsValid);
        Assert.False(result.CanRepair);
        Assert.Contains("excessive folder list", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private AiResponseParseResult<AiParsedFileRename> ParseRename(string json) =>
        _parser.ParseFileRename(
            json,
            new AiFileRenameRequest(CreateFile("known:1", "draft-invoice.pdf", "Invoice"), []),
            [new AiPromptSourceMapping("item-001", "known:1", "draft-invoice.pdf")]);

    private AiResponseParseResult<AiParsedFolderStructure> ParseFolder(
        string json,
        IReadOnlyList<string>? allowedNames = null)
    {
        var files = new[]
        {
            CreateFile("known:a", "a.pdf"),
            CreateFile("known:b", "b.pdf"),
        };
        return _parser.ParseFolderStructure(
            json,
            files,
            [
                new AiPromptSourceMapping("item-001", "known:a", "a.pdf"),
                new AiPromptSourceMapping("item-002", "known:b", "b.pdf"),
            ],
            allowedNames ?? ["Document", "Other"]);
    }

    private static string FolderJson(
        string folders,
        string assignments)
    {
        using var foldersDocument = JsonDocument.Parse(folders);
        using var assignmentsDocument = JsonDocument.Parse(assignments);
        return JsonSerializer.Serialize(new
        {
            taskId = AiPromptBuilder.FolderStructureTaskId,
            status = "suggestion",
            folders = foldersDocument.RootElement,
            assignments = assignmentsDocument.RootElement,
            reason = "A short user-facing grouping explanation.",
        });
    }

    private static ResultFile CreateFile(
        string id,
        string name,
        string classification = "Document") => new(
        id,
        $"C:\\Private\\{name}",
        name,
        Path.GetExtension(name),
        10,
        DateTimeOffset.UnixEpoch,
        FileCategory.Document,
        classification,
        DuplicateStatus.Unique,
        null,
        false);
}
