using System.Reflection;
using OpenSorSe.Application.KnowledgeGraph;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Indexing.Sqlite.KnowledgeGraph;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies the production dependency graph can be constructed without launching Avalonia.</summary>
public sealed class CompositionRootTests
{
    /// <summary>Resolves the production shell while the service provider validates every registration.</summary>
    [Fact]
    public async Task CreateServiceProvider_ResolvesMainViewModelAndDisposesAsync()
    {
        var factory = typeof(App).GetMethod(
            "CreateServiceProvider",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(factory);
        var provider = Assert.IsAssignableFrom<IServiceProvider>(factory.Invoke(null, null));

        try
        {
            var main = Assert.IsType<MainViewModel>(provider.GetService(typeof(MainViewModel)));
            var graphLifecycle = Assert.IsType<SqliteGraphStorageLifecycle>(
                provider.GetService(typeof(IGraphStorageLifecycle)));
            Assert.IsType<RelationshipGraphAuthorityBridge>(
                provider.GetService(typeof(IGraphLegacyAuthorityBridge)));
            Assert.IsType<GraphDecisionService>(provider.GetService(typeof(IGraphDecisionService)));
            Assert.NotNull(main.Settings.PlatformDiagnostics);
            Assert.NotNull(graphLifecycle);
            Assert.Equal(
                Enum.GetValues<PlatformCapabilityKind>().Length,
                main.Settings.PlatformDiagnostics.Capabilities.Count);
        }
        finally
        {
            if (provider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                (provider as IDisposable)?.Dispose();
            }
        }
    }

    /// <summary>
    /// Protects the startup composition boundary that preflights durable recovery state, initializes the
    /// targeted index, and then forwards the exact recovered facts without an unrelated stage between.
    /// </summary>
    [Fact]
    public void AppStartup_ForwardsRecoveredOperationsAfterBackgroundIndexInitialization()
    {
        var sourcePath = Path.Combine(RepositoryRoot(), "src", "OpenSorSe.Desktop", "App.axaml.cs");
        var source = File.ReadAllText(sourcePath);
        var journalPreflight = source.IndexOf(
            "GetRequiredService<IOperationJournalStore>()",
            StringComparison.Ordinal);
        var indexInitialization = source.IndexOf(
            "GetRequiredService<IBackgroundIndexingService>()",
            journalPreflight,
            StringComparison.Ordinal);
        var recoveryCapture = source.IndexOf(
            "var recoveredOperations =",
            indexInitialization,
            StringComparison.Ordinal);
        var reconciliation = source.IndexOf(
            "ReconcileRecoveredOperationsAsync(recoveredOperations",
            recoveryCapture,
            StringComparison.Ordinal);

        Assert.True(journalPreflight >= 0, "Startup must validate the authoritative journal before activating other subsystems.");
        Assert.True(indexInitialization > journalPreflight, "Background indexing must initialize after authoritative-state preflight.");
        Assert.True(recoveryCapture > indexInitialization, "Interruption recovery must run after the targeted index is ready.");
        Assert.True(reconciliation > recoveryCapture, "The exact recovered records must immediately reach post-operation reconciliation.");
    }

    /// <summary>Protects the consumer wiring and batching bounds that the algorithm-only tests cannot prove.</summary>
    [Fact]
    public void Shell_RoutesHistoryUndoAndBatchesStartupRecoveryPaths()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "OpenSorSe.Desktop",
            "ViewModels",
            "MainViewModel.cs"));
        Assert.Contains(
            "UndoHistory.OperationUndoCompleted += OnOperationHistoryUndoCompleted;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "UndoHistory.OperationUndoCompleted -= OnOperationHistoryUndoCompleted;",
            source,
            StringComparison.Ordinal);
        var start = source.IndexOf(
            "internal async Task ReconcileRecoveredOperationsAsync",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "private async Task<ChangePlanReconciliationResult?> ReconcileChangePlanOperationAsync",
            start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var recoveryBody = source[start..end];
        Assert.Contains("GetRecoveryRefreshRoots(operations)", recoveryBody, StringComparison.Ordinal);
        Assert.Contains("OperationJournalSchema.MaximumOperations", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(recoveryBody, "ReconcilePathsAsync"));
    }

    /// <summary>Ensures native package smoke follows the same recover-then-reconcile startup boundary.</summary>
    [Fact]
    public void PackageSmoke_ForwardsRecoveredOperationsAfterIndexInitialization()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "OpenSorSe.Desktop",
            "PackageSmokeTest.cs"));
        var planPreflight = source.IndexOf(
            "GetRequiredService<IChangePlanStore>()",
            StringComparison.Ordinal);
        var journalPreflight = source.IndexOf(
            "GetRequiredService<IOperationJournalStore>()",
            StringComparison.Ordinal);
        var indexInitialization = source.IndexOf(
            "GetRequiredService<IBackgroundIndexingService>()",
            StringComparison.Ordinal);
        var recovery = source.IndexOf("RecoverInterruptedAsync", indexInitialization, StringComparison.Ordinal);
        var reconciliation = source.IndexOf(
            "ReconcileRecoveredOperationsAsync",
            recovery,
            StringComparison.Ordinal);

        Assert.True(planPreflight >= 0);
        Assert.True(journalPreflight > planPreflight);
        Assert.True(indexInitialization > journalPreflight);
        Assert.True(recovery > indexInitialization);
        Assert.True(reconciliation > recovery);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var offset = 0; (offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0; offset += value.Length)
        {
            count++;
        }

        return count;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenSorSe.sln")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
