using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Platform;
using OpenSorSe.Rules.Models;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Rules.Tests;

/// <summary>
/// Verifies deterministic lexical planning without filesystem access or action execution.
/// </summary>
public sealed class ActionPlannerTests
{
    /// <summary>
    /// Verifies each supported actionable decision creates the corresponding operation.
    /// </summary>
    [Theory]
    [InlineData(RuleActionKind.Move, PlannedOperationKind.Move)]
    [InlineData(RuleActionKind.Copy, PlannedOperationKind.Copy)]
    [InlineData(RuleActionKind.Rename, PlannedOperationKind.Rename)]
    [InlineData(RuleActionKind.Delete, PlannedOperationKind.Delete)]
    public async Task PlanAsync_SupportedActions_CreateOperations(RuleActionKind actionKind, PlannedOperationKind expectedKind)
    {
        var action = actionKind switch
        {
            RuleActionKind.Move or RuleActionKind.Copy => new RuleAction(actionKind, DestinationDirectory),
            RuleActionKind.Rename => new RuleAction(actionKind, NameTemplate: "renamed.txt"),
            _ => new RuleAction(actionKind),
        };

        var result = await CreatePlanner().PlanAsync([Decision(action)]);

        var operation = Assert.Single(result.Operations);
        Assert.Equal(expectedKind, operation.Kind);
        Assert.Equal("plan:0", operation.OperationId);
        Assert.Equal(operation.File.FullPath, operation.SourcePath);
        Assert.Equal(expectedKind == PlannedOperationKind.Delete, operation.DestinationPath is null);
    }

    /// <summary>
    /// Verifies no-action decisions do not produce operations or issues.
    /// </summary>
    [Fact]
    public async Task PlanAsync_NoAction_ProducesNoOperationOrIssue()
    {
        var result = await CreatePlanner().PlanAsync([Decision(new RuleAction(RuleActionKind.NoAction), selectedRuleId: null)]);

        Assert.Empty(result.Operations);
        Assert.Empty(result.Issues);
        Assert.Equal(1L, result.Statistics.DecisionsWithNoAction);
    }

    /// <summary>
    /// Verifies move and copy lexically combine the directory and metadata filename.
    /// </summary>
    [Theory]
    [InlineData(RuleActionKind.Move)]
    [InlineData(RuleActionKind.Copy)]
    public async Task PlanAsync_MoveAndCopy_CombineDestinationWithMetadataName(RuleActionKind kind)
    {
        var result = await CreatePlanner().PlanAsync([Decision(new RuleAction(kind, DestinationDirectory))]);

        Assert.Equal(Path.Combine(DestinationDirectory, "file.txt"), Assert.Single(result.Operations).DestinationPath);
    }

    /// <summary>
    /// Verifies supported rename template tokens and source-directory preservation.
    /// </summary>
    [Fact]
    public async Task PlanAsync_Rename_ResolvesTokensAndPreservesSourceDirectory()
    {
        var file = File(fileName: "Report.TXT", extension: ".TXT", category: FileCategory.Document);
        var result = await CreatePlanner().PlanAsync([Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: "{name}-{category}{extension}"), file)]);

        var operation = Assert.Single(result.Operations);
        Assert.Equal(Path.Combine(SourceDirectory, "Report-Document.txt"), operation.DestinationPath);
    }

    /// <summary>
    /// Verifies extensionless metadata resolves the extension token to an empty string.
    /// </summary>
    [Fact]
    public async Task PlanAsync_Rename_ExtensionlessFile_UsesEmptyExtension()
    {
        var file = File(fileName: "README", extension: string.Empty);
        var result = await CreatePlanner().PlanAsync([Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: "{name}{extension}-copy"), file)]);

        Assert.Equal(Path.Combine(SourceDirectory, "README-copy"), Assert.Single(result.Operations).DestinationPath);
    }

    /// <summary>
    /// Verifies invalid templates and lexical paths yield one issue without an operation.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidPlanningDecisions))]
    public async Task PlanAsync_InvalidDecisionData_ReturnsOneIssue(RuleDecision decision, ActionPlanningIssueKind kind)
    {
        var result = await CreatePlanner().PlanAsync([decision]);

        Assert.Empty(result.Operations);
        Assert.Equal(kind, Assert.Single(result.Issues).Kind);
        Assert.Equal(1L, result.Statistics.DecisionsFailed);
    }

    /// <summary>
    /// Verifies missing metadata and missing category token data are recoverable per-decision issues.
    /// </summary>
    [Fact]
    public async Task PlanAsync_MissingRequiredData_ReturnsIssuesAndContinues()
    {
        var missingMove = Decision(new RuleAction(RuleActionKind.Move, DestinationDirectory), new FileEntry(SourcePath("missing.txt")));
        var missingCopy = Decision(new RuleAction(RuleActionKind.Copy, DestinationDirectory), new FileEntry(SourcePath("missing.txt")));
        var missingRename = Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: "{name}"), new FileEntry(SourcePath("missing.txt")));
        var missingCategory = Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: "{category}.txt"), File(category: null));
        var validDelete = Decision(new RuleAction(RuleActionKind.Delete), new FileEntry(SourcePath("delete.bin")));
        var result = await CreatePlanner().PlanAsync([missingMove, missingCopy, missingRename, missingCategory, validDelete]);

        Assert.Equal(4, result.Issues.Count);
        Assert.All(result.Issues, issue => Assert.Equal(ActionPlanningIssueKind.MissingMetadata, issue.Kind));
        Assert.Equal(PlannedOperationKind.Delete, Assert.Single(result.Operations).Kind);
    }

    /// <summary>
    /// Verifies decisions, operations, file entries, and duplicate decisions preserve input order and immutability.
    /// </summary>
    [Fact]
    public async Task PlanAsync_PreservesOrderDuplicatesAndInputValues()
    {
        var metadata = new FileMetadata("file.txt", ".txt", 1, null, null, null, FileAttributes.Normal);
        var hash = new FileHash("SHA-256", new string('a', 64));
        var classification = new FileClassification(FileCategory.Document);
        var duplicate = new DuplicateClassification(DuplicateStatus.Unique);
        var file = new FileEntry(SourcePath("file.txt"), metadata, hash, classification, duplicate);
        var decision = Decision(new RuleAction(RuleActionKind.Move, DestinationDirectory), file);
        var result = await CreatePlanner().PlanAsync([decision, decision]);

        Assert.Equal(["plan:0", "plan:1"], result.Operations.Select(operation => operation.OperationId));
        Assert.All(result.Operations, operation =>
        {
            Assert.Same(file, operation.File);
            Assert.Same(metadata, operation.File.Metadata);
            Assert.Same(hash, operation.File.Hash);
            Assert.Same(classification, operation.File.Classification);
            Assert.Same(duplicate, operation.File.Duplicate);
        });
        Assert.Equal(DestinationDirectory, decision.Action.DestinationPath);
    }

    /// <summary>
    /// Verifies mixed decisions produce accurate ordered statistics and continue after recoverable issues.
    /// </summary>
    [Fact]
    public async Task PlanAsync_MixedDecisions_ProducesAccurateStatistics()
    {
        var move = Decision(new RuleAction(RuleActionKind.Move, DestinationDirectory));
        var invalid = Decision(new RuleAction(RuleActionKind.Copy, "relative"));
        var rename = Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: "new.txt"));
        var delete = Decision(new RuleAction(RuleActionKind.Delete));
        var noAction = Decision(new RuleAction(RuleActionKind.NoAction), selectedRuleId: null);
        var result = await CreatePlanner().PlanAsync([move, invalid, rename, delete, noAction]);

        Assert.Equal([PlannedOperationKind.Move, PlannedOperationKind.Rename, PlannedOperationKind.Delete], result.Operations.Select(operation => operation.Kind));
        Assert.Equal(new ActionPlanningStatistics(5, 3, 1, 1, 1, 0, 1, 1, 1), result.Statistics);
    }

    /// <summary>
    /// Verifies empty and null collections and null decisions follow the collection contract.
    /// </summary>
    [Fact]
    public async Task PlanAsync_CollectionValidationAndEmptyInput_AreDeterministic()
    {
        IReadOnlyCollection<RuleDecision> nullCollection = null!;
        await Assert.ThrowsAsync<ArgumentNullException>(() => CreatePlanner().PlanAsync(nullCollection));
        await Assert.ThrowsAsync<ArgumentException>(() => CreatePlanner().PlanAsync(new List<RuleDecision> { null! }));
        var empty = await CreatePlanner().PlanAsync(Array.Empty<RuleDecision>());

        Assert.Empty(empty.Operations);
        Assert.Empty(empty.Issues);
        Assert.Equal(new ActionPlanningStatistics(0, 0, 0, 0, 0, 0, 0, 0, 0), empty.Statistics);
    }

    /// <summary>
    /// Verifies pre-cancellation stops planning without a partial result.
    /// </summary>
    [Fact]
    public async Task PlanAsync_PreCancelled_ThrowsOperationCanceledException()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreatePlanner().PlanAsync([Decision(new RuleAction(RuleActionKind.Delete))], source.Token));
    }

    /// <summary>Verifies source-equality checks follow the injected host path case policy.</summary>
    [Fact]
    public async Task PlanAsync_CaseOnlyRename_FollowsConfiguredPathSemantics()
    {
        var sourcePath = Path.Combine(
            Path.GetTempPath(),
            "OmniSorSe-ActionPlanner-Case",
            "file.txt");
        var file = new FileEntry(
            sourcePath,
            new FileMetadata("file.txt", ".txt", 1, null, null, null, FileAttributes.Normal),
            null,
            new FileClassification(FileCategory.Document));
        var decision = Decision(
            new RuleAction(RuleActionKind.Rename, NameTemplate: "FILE.txt"),
            file);

        var windowsResult = await CreatePlanner(new WindowsPathSemantics()).PlanAsync([decision]);
        var linuxResult = await CreatePlanner(new LinuxPathSemantics()).PlanAsync([decision]);

        Assert.Empty(windowsResult.Operations);
        Assert.Equal(
            ActionPlanningIssueKind.SourceEqualsDestination,
            Assert.Single(windowsResult.Issues).Kind);
        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(sourcePath)!, "FILE.txt"),
            Assert.Single(linuxResult.Operations).DestinationPath);
    }

    /// <summary>
    /// Supplies malformed decisions covering supported planning validation boundaries.
    /// </summary>
    public static IEnumerable<object[]> InvalidPlanningDecisions()
    {
        yield return [Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: "{unknown}")), ActionPlanningIssueKind.InvalidNameTemplate];
        yield return [Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: string.Empty)), ActionPlanningIssueKind.InvalidNameTemplate];
        yield return [Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: "folder/name")), ActionPlanningIssueKind.InvalidNameTemplate];
        yield return [Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: ".")), ActionPlanningIssueKind.InvalidNameTemplate];
        yield return [Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: "..")), ActionPlanningIssueKind.InvalidNameTemplate];
        yield return [Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: "bad\0name")), ActionPlanningIssueKind.InvalidNameTemplate];
        yield return [Decision(new RuleAction(RuleActionKind.Move, "relative")), ActionPlanningIssueKind.InvalidDestinationPath];
        yield return [Decision(new RuleAction(RuleActionKind.Rename, NameTemplate: "{name}{extension}")), ActionPlanningIssueKind.SourceEqualsDestination];
        yield return [Decision(new RuleAction(RuleActionKind.Move, DestinationDirectory), selectedRuleId: null), ActionPlanningIssueKind.InvalidDecision];
        yield return [Decision(new RuleAction((RuleActionKind)999)), ActionPlanningIssueKind.UnsupportedAction];
    }

    private static ActionPlanner CreatePlanner(IPathSemantics? pathSemantics = null) =>
        new(new TestLoggingService(), new TestErrorHandler(), pathSemantics);

    private static string SourceDirectory =>
        Path.Combine(Path.GetTempPath(), "OmniSorSe-Rules-Source");

    private static string DestinationDirectory =>
        Path.Combine(Path.GetTempPath(), "OmniSorSe-Rules-Destination");

    private static string SourcePath(string name) => Path.Combine(SourceDirectory, name);

    private static FileEntry File(string fileName = "file.txt", string extension = ".txt", FileCategory? category = FileCategory.Document) =>
        new(SourcePath("file.txt"), new FileMetadata(fileName, extension, 1, null, null, null, FileAttributes.Normal), null,
            category is null ? null : new FileClassification(category.Value));

    private static RuleDecision Decision(RuleAction action, FileEntry? file = null, string? selectedRuleId = "rule") =>
        new(file ?? File(), action, selectedRuleId, selectedRuleId is null ? null : "Rule", selectedRuleId is null ? null : 1, Array.Empty<string>());

    private sealed class TestLoggingService : ILoggingService
    {
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void Dispose() { }
        public void Initialize(LogLevel minimumLevel) { }
    }

    private sealed class TestErrorHandler : IErrorHandler
    {
        public event EventHandler<ApplicationError>? ErrorReported;
        public void Report(ApplicationError applicationError) => ErrorReported?.Invoke(this, applicationError);
    }
}
