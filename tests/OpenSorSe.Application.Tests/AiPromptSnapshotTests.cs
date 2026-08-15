using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenSorSe.Application.AI;
using OpenSorSe.Application.Content;
using OpenSorSe.Application.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Application.Tests;

/// <summary>Locks approved small-model prompt text, schemas, and DTO property ordering.</summary>
public sealed class AiPromptSnapshotTests
{
    /// <summary>Any intentional prompt or schema change must update this reviewable approval snapshot.</summary>
    [Fact]
    public void ApprovedPromptsAndSchemas_HaveStableSnapshots()
    {
        var builder = new AiPromptBuilder();
        var rename = builder.BuildFileRenamePrompt(
            new AiFileRenameRequest(
                CreateFile("known:rename", "2026-07-invoice-draft.pdf", "Invoice"),
                ["2026-06-invoice.pdf", "notes.txt"]),
            new AiPreferenceSummary([], ["Finance"], [], ["invoice-old.pdf"]));
        var folder = builder.BuildFolderStructurePrompt(
            new AiFolderStructureRequest(
                [
                    CreateFile("known:b", "receipt-b.pdf", "Receipt"),
                    CreateFile("known:a", "invoice-a.pdf", "Invoice"),
                ],
                ["Finance/Invoices", "Receipts"]),
            new AiPreferenceSummary([], ["Archive"], [], []));
        var document = builder.BuildDocumentInterpretationPrompt(
            new AiDocumentTextRequest(
                "known:document",
                "invoice-a.pdf",
                "Invoice A dated 2026-07-24.",
                null,
                []));
        var repair = builder.BuildRepairPrompt(
            rename,
            "```json\n{\"taskId\":\"file-rename-v2\"}\n```",
            "The AI wrapped its response in Markdown.");
        var actual = string.Join(
            '\n',
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["rename-system"] = Hash(rename.SystemPrompt),
                ["rename-prompt"] = Hash(rename.Prompt),
                ["rename-schema"] = Hash(AiStructuredOutputContracts.GetSchemaJson(AiSuggestionKind.FileRename)),
                ["folder-system"] = Hash(folder.SystemPrompt),
                ["folder-prompt"] = Hash(folder.Prompt),
                ["folder-schema"] = Hash(AiStructuredOutputContracts.GetSchemaJson(AiSuggestionKind.FolderStructure)),
                ["document-system"] = Hash(document.SystemPrompt),
                ["document-prompt"] = Hash(document.Prompt),
                ["document-schema"] = Hash(AiStructuredOutputContracts.GetSchemaJson(AiSuggestionKind.DocumentTextInterpretation)),
                ["repair-system"] = Hash(repair.SystemPrompt),
                ["repair-prompt"] = Hash(repair.Prompt),
            }.Select(item => $"{item.Key}={item.Value}"));

        Assert.Equal(
            """
            rename-system=ad22f4a306503643cd79b7d380902fa8fd50688cba7acabaa24586c0c52edbf2
            rename-prompt=17a2a47d9d4d2cb710775e86da24562960ac6c157e38f126b4d4b2974cbb41cc
            rename-schema=c2d5f07b2a52a7454a842b99a03d660cc5d8efdab9f1c6948e576518d12757f9
            folder-system=89ffe06c709de3787f18ad4620fb46fb34902b2f3fc86503064ee553f8e75d86
            folder-prompt=730869fbc34adeb3f3287aec078382b633017550f790a76c4e1ba1f324f3d9df
            folder-schema=2dd0686ed4c49d94e5b95daca32e8fcc57ef6209fe8494b5defab6b56b51e803
            document-system=aadc81db1684308ffff35d526579fdca622390326cd4579b3e2631ef9085697f
            document-prompt=db1a3fcf64ec77a6a870eb95c24f0f4b2b73f207ef3f0f23220297d45d0eb963
            document-schema=768b9583318b7f844049a93e0e1de54fb2bd1b60af0373b1eeb23ff3bc0e57b4
            repair-system=1526c7ef09c6cc8916a471033453c47ad4e6831255c5e0909f393db5bfa50b81
            repair-prompt=6104df890c59e1c27c0fa5be8186f36ab8571a66b64f53737d3ccc404d08320f
            """,
            actual);
    }

    /// <summary>Wire DTO serialization order stays aligned with the prompt's deterministic property order.</summary>
    [Fact]
    public void WireDtos_SerializeInApprovedPropertyOrder()
    {
        var rename = JsonSerializer.Serialize(new AiFileRenameResponseContract
        {
            TaskId = AiPromptBuilder.FileRenameTaskId,
            Status = "suggestion",
            SourceFileId = "item-001",
            SuggestedStem = "invoice-draft",
            Reason = "Clearer.",
            Confidence = 0.5,
        });
        var folder = JsonSerializer.Serialize(new AiFolderStructureResponseContract
        {
            TaskId = AiPromptBuilder.FolderStructureTaskId,
            Status = "suggestion",
            Folders =
            [
                new AiFolderContract
                {
                    FolderId = "folder-001",
                    Name = "Other",
                    ParentFolderId = null,
                },
            ],
            Assignments =
            [
                new AiFolderAssignmentContract
                {
                    SourceFileId = "item-001",
                    FolderId = "folder-001",
                },
            ],
            Reason = "Fallback.",
        });
        var document = JsonSerializer.Serialize(new AiDocumentInterpretationResponseContract
        {
            TaskId = AiPromptBuilder.DocumentInterpretationTaskId,
            Status = "suggestion",
            SourceFileId = "item-001",
            DocumentType = "Invoice",
            Title = null,
            Tags = [],
            Dates = ["2026-07-24"],
            Issuer = null,
            SuggestedFolder = "Invoices",
            Reason = "Explicit fields.",
            Confidence = null,
        });

        Assert.Equal(
            """{"taskId":"file-rename-v2","status":"suggestion","sourceFileId":"item-001","suggestedStem":"invoice-draft","reason":"Clearer.","confidence":0.5}""",
            rename);
        Assert.Equal(
            """{"taskId":"folder-structure-v2","status":"suggestion","folders":[{"folderId":"folder-001","name":"Other","parentFolderId":null}],"assignments":[{"sourceFileId":"item-001","folderId":"folder-001"}],"reason":"Fallback."}""",
            folder);
        Assert.Equal(
            """{"taskId":"document-text-interpretation-v1","status":"suggestion","sourceFileId":"item-001","documentType":"Invoice","title":null,"tags":[],"dates":["2026-07-24"],"issuer":null,"suggestedFolder":"Invoices","reason":"Explicit fields.","confidence":null}""",
            document);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static ResultFile CreateFile(
        string id,
        string name,
        string classification) => new(
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
