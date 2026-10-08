using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Plugins;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Application.SmartTags;
using OpenSorSe.Application.Watching;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Lifecycle;
using OpenSorSe.Core.Platform;
using OpenSorSe.Desktop;
using OpenSorSe.Desktop.ViewModels;
using OpenSorSe.Desktop.Views;
using OpenSorSe.Indexing.Sqlite;
using OpenSorSe.Rules.Models;

internal static class Program
{
    internal const string ProductionRevision = "df3984fab5eaf94424ec6cd032e91468799d62d6";
    internal static string RuntimeRoot { get; private set; } = string.Empty;
    internal static string OutputRoot { get; private set; } = string.Empty;
    internal static string HarnessRevision { get; private set; } = string.Empty;

    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsLinux() || args.Length != 2)
        {
            Console.Error.WriteLine("Run on Linux/X11: OmniSorSe.Screenshots <fresh-runtime-directory> <output-directory>");
            return 2;
        }

        RuntimeRoot = Path.GetFullPath(args[0]);
        OutputRoot = Path.GetFullPath(args[1]);
        HarnessRevision = Environment.GetEnvironmentVariable("CAPTURE_HARNESS_SHA") ?? string.Empty;
        if (HarnessRevision.Length != 40 || !HarnessRevision.All(Uri.IsHexDigit))
            throw new InvalidOperationException("CAPTURE_HARNESS_SHA must identify the exact harness commit.");
        if (Directory.Exists(RuntimeRoot))
            throw new InvalidOperationException("A fresh runtime directory is required; existing profiles are never reused.");
        if (Directory.Exists(OutputRoot) && Directory.EnumerateFiles(OutputRoot, "*.png").Any())
            throw new InvalidOperationException("The output directory already contains captures; use a fresh directory.");

        Directory.CreateDirectory(RuntimeRoot);
        Directory.CreateDirectory(OutputRoot);
        return AppBuilder.Configure<CaptureApp>().UsePlatformDetect().WithInterFont()
            .StartWithClassicDesktopLifetime([]);
    }
}

// Resources, views, view-models and all registered services come from the real Desktop.
// Only the isolated profile lifecycle and the driving of existing commands live here.
internal sealed class CaptureApp : App
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly List<object> _captures = [];
    private readonly Dictionary<string, object?> _facts = [];
    private SortedDictionary<string, string> _before = new(StringComparer.Ordinal);
    private string _library = string.Empty;

    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime
            ?? throw new InvalidOperationException("The real desktop lifetime is required.");
        RequestedThemeVariant = ThemeVariant.Light;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Posting allows the UI event loop to run while production initialization awaits I/O.
        // Do not call App's default startup, which opens the user's normal profile.
        Dispatcher.UIThread.Post(async () => await RunAsync(desktop));
    }

    private async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        ServiceProvider? services = null;
        ProfileOwnershipLease? ownership = null;
        ApplicationRunStateMarker? runState = null;
        var succeeded = false;
        Exception? failure = null;
        try
        {
            await WriteManifestAsync(false, null);
            AssertProductionAssembly();
            _library = Path.Combine(Program.RuntimeRoot, "Sample library");
            CopyFixture(Path.Combine(AppContext.BaseDirectory, "fixtures", "library"), _library);
            _before = HashFixture(_library);
            Require(_before.Count == 9, "The nine-file synthetic fixture must be present.");
            _facts["fixtureFiles"] = _before;
            _facts["fixtureSha256"] = HashManifest(_before);

            var profile = Path.Combine(Program.RuntimeRoot, "profile");
            var paths = new ApplicationPathProvider(PlatformServices.CurrentPlatform, _ => null, profile, profile);
            paths.EnsureOwnedDirectories();
            ownership = ProfileOwnershipLease.Acquire(paths.Paths.StateDirectory);
            runState = ApplicationRunStateMarker.Begin(paths.Paths.StateDirectory);
            var factory = typeof(App).GetMethod("CreateServiceProviderForPaths", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("Production Desktop composition root was not found.");
            services = (ServiceProvider)factory.Invoke(null, [paths, ownership, runState])!;
            await services.GetRequiredService<IApplicationHost>().InitializeAsync();

            var config = services.GetRequiredService<IConfigurationService>();
            var draft = SettingsDraft.FromSettings(config.Current);
            draft.SemanticSearchEnabled = true;
            draft.DeepIndexingEnabled = true;
            draft.DefaultIndexingLevel = IndexingLevel.Standard;
            draft.EmbeddingsEnabled = false;
            draft.AiEnabled = false;
            draft.DeepAiProcessingEnabled = false;
            draft.ShowAdvancedFeatures = true;
            await config.SaveAsync(draft.ToSettings(), CancellationToken.None);
            await services.GetRequiredService<IPluginManager>().InitializeAsync(CancellationToken.None);
            await services.GetRequiredService<IWorkflowLibraryService>().InitializeAsync(CancellationToken.None);
            await services.GetRequiredService<IWatchedFolderCoordinator>().InitializeAsync(CancellationToken.None);
            await services.GetRequiredService<SqliteDeepIndexStore>().InitializeAsync(CancellationToken.None);
            await services.GetRequiredService<ISmartTagService>().InitializeAsync(CancellationToken.None);
            var indexing = services.GetRequiredService<IBackgroundIndexingService>();
            await indexing.InitializeAsync();
            var vectors = services.GetRequiredService<VectorIndexCoordinator>();
            await vectors.InitializeAsync();

            var main = services.GetRequiredService<MainViewModel>();
            var window = new MainWindow(main)
            {
                Width = 1600,
                Height = 1000,
                Position = new PixelPoint(0, 0),
                CanResize = false,
                Title = "OmniSorSe v3.0.0-rc.1 — Synthetic sample library",
            };
            desktop.MainWindow = window;
            window.Show();
            window.Activate();
            await SettleAsync();
            Require(window.TryGetPlatformHandle()?.Handle != IntPtr.Zero, "A native desktop window is required.");

            Console.WriteLine("Scanning nine synthetic source files through MainViewModel.");
            await main.StartProcessingAsync(new ScanRequest([_library]) { EnableSearchIndex = true });
            Require(main.Results.Snapshot?.Files.Count == 9, "The production scan must discover all nine fixture files.");
            await AwaitIndexAsync(indexing);
            var documents = await indexing.GetDocumentsAsync(100);
            Require(documents.Count == 9 && documents.All(file => !string.IsNullOrWhiteSpace(file.ExtractedText)),
                "Every fixture must have actual extracted document text before capture.");
            _facts["indexedDocumentCount"] = documents.Count;
            _facts["indexProgress"] = await indexing.GetProgressAsync();
            _facts["aiEnabled"] = config.Current.Ai.Enabled;
            _facts["embeddingsEnabled"] = config.Current.SemanticSearch.EmbeddingsEnabled;
            _facts["semanticIndexStatus"] = vectors.CurrentStatus;

            var relationships = services.GetRequiredService<IRelationshipService>();
            foreach (var document in documents)
                await relationships.AnalyzeFileAsync(document.FileId);
            main.Notifications.ClearAllCommand.Execute(null);

            await main.NavigateAsync(NavigationDestination.SemanticSearch);
            await main.SemanticSearch.RefreshAsync();
            main.SemanticSearch.QueryText = "camping";
            await main.SemanticSearch.SearchCommand.ExecuteAsync(null);
            Require(main.SemanticSearch.Hits.Count >= 3, "The real camping query must find three fixture documents.");
            _facts["searchQuery"] = main.SemanticSearch.QueryText;
            _facts["searchResults"] = main.SemanticSearch.Hits.Select(hit => new
            {
                hit.FileName, hit.Explanation, hit.CoverageIndicator, hit.AiEnrichmentIndicator,
            }).ToArray();
            await CaptureAsync(window, "01-search.png", "Search", main.SemanticSearch.Status.Message);

            var searchView = window.GetVisualDescendants().OfType<SemanticSearchView>().Single();
            var explanation = FindExpander(searchView, "Why this result?");
            explanation.IsExpanded = true;
            await SettleAsync();
            explanation.BringIntoView();
            await SettleAsync();
            var explanationBody = explanation.Content as Control
                ?? throw new InvalidOperationException("The real ranking explanation body was not realized.");
            explanationBody.BringIntoView();
            await SettleAsync();
            var explanationViewport = explanationBody.GetVisualAncestors().OfType<ScrollContentPresenter>().First();
            var bodyPosition = explanationBody.TranslatePoint(new Point(0, 0), explanationViewport)
                ?? throw new InvalidOperationException("The ranking explanation has no viewport position.");
            Require(explanationBody.IsEffectivelyVisible && explanationBody.Bounds.Height > 0 &&
                bodyPosition.Y >= -1 && bodyPosition.Y + explanationBody.Bounds.Height <= explanationViewport.Bounds.Height + 1,
                "The complete ranking explanation body must be visible inside the result-list viewport.");
            _facts["rankingExplanationViewport"] = new
            {
                top = bodyPosition.Y,
                height = explanationBody.Bounds.Height,
                viewportHeight = explanationViewport.Bounds.Height,
                fullyVisible = true,
            };
            await CaptureAsync(window, "02-search-ranking.png", "Expanded real ranking explanation",
                main.SemanticSearch.Hits[0].Explanation);
            explanation.IsExpanded = false;
            explanation.GetVisualAncestors().OfType<ScrollViewer>().First().Offset = Vector.Zero;

            // This is the documented production in-memory rule-review API, not persisted rules
            // or fabricated matches. These rules are not executed and are added after scanning.
            main.RuleEditor.Load([
                new FileRule("sample-text", "Group text documents", 20,
                    [new RuleCondition(RuleConditionKind.ExtensionEquals, ".txt")],
                    new RuleAction(RuleActionKind.Move, Path.Combine(_library, "Documents"))),
                new FileRule("sample-budget", "Keep the monthly budget together", 10,
                    [new RuleCondition(RuleConditionKind.ExactFileNameEquals, "Household budget.txt")],
                    new RuleAction(RuleActionKind.Copy, Path.Combine(_library, "Finance"))),
            ]);
            main.RuleEditor.SelectedRule = main.RuleEditor.Rules[0];
            await main.NavigateAsync(NavigationDestination.Rules);
            _facts["rulesScope"] = "Synthetic in-memory rule inputs; current production review-only surface; not persisted or executed.";
            await CaptureAsync(window, "03-rules.png", "Sorting rules", main.RuleEditor.StatusText);

            var organization = main.Results.Organization;
            await organization.OpenAsync(new OrganizationSelectionContext(OrganizationSelectionOrigin.Files,
                "synthetic library", documents.Where(file => file.RelativePath.StartsWith("Inbox/", StringComparison.Ordinal))
                    .Select(file => file.FileId).ToArray()));
            organization.NamingPattern = "{originalName}";
            organization.DestinationPattern = "{category}";
            organization.SelectedStrategy = organization.Strategies.Single(item => item.Strategy == OrganizationStrategy.Improve);
            await organization.PreviewCommand.ExecuteAsync(null);
            Require(organization.HasProposal && organization.AllRows.Any(row => row.Model.IsEligible && !row.Model.IsUnchanged),
                "The real organization service must produce eligible changed-path proposals.");
            await main.NavigateAsync(NavigationDestination.Organize);
            await SettleAsync();
            var organizeView = window.GetVisualDescendants().OfType<OrganizeView>().Single();
            FindExpander(organizeView, "Recommendation recipe").IsExpanded = false;
            await ExpandTreesAsync(organizeView);
            _facts["organization"] = new
            {
                strategy = organization.SelectedStrategy.Label,
                organization.NamingPattern, organization.DestinationPattern,
                organization.StatusText, organization.ActionCountText,
                rows = organization.AllRows.Select(row => new { row.CurrentPath, row.TargetPath, row.Readiness, row.ReasonText }).ToArray(),
                applied = false,
            };
            await CaptureAsync(window, "04-organize.png", "Current and recommended folder trees", organization.StatusText);

            await main.NavigateAsync(NavigationDestination.Duplicates);
            var duplicates = main.Results.DuplicateReview;
            Require(duplicates.VisibleGroups.Count == 1, "The real hash detector must find the single duplicate fixture pair.");
            duplicates.SelectedGroup = duplicates.VisibleGroups[0];
            Require(duplicates.MemberRows.Count == 2, "The duplicate drawer must contain two actual scanned files.");
            _facts["duplicateGroupCount"] = duplicates.VisibleGroups.Count;
            await CaptureAsync(window, "05-duplicates.png", "Exact duplicate pair", duplicates.StatusText);

            await main.NavigateAsync(NavigationDestination.Collections);
            await main.Collections.RefreshAsync();
            await WaitUntilAsync(() => !main.Collections.IsBusy, TimeSpan.FromSeconds(30), "Relationship page refresh");
            var budget = documents.Single(file => file.FileName == "Household budget.txt");
            await main.Collections.SelectFileAsync(budget.FileId);
            await WaitUntilAsync(() => !main.Collections.IsBusy && main.Collections.SelectedFile?.FileId == budget.FileId,
                TimeSpan.FromSeconds(30), "Selected file relationships");
            main.Collections.RelationshipFilter = RelationshipType.DocumentSet;
            await main.Collections.RefreshRelatedFilesCommand.ExecuteAsync(null);
            Require(main.Collections.RelatedFiles.Count == 1 &&
                main.Collections.RelatedFiles[0].FileName == "Household budget copy.txt",
                "The real DocumentSet filter must show the retained exact-content relationship.");
            _facts["relatedFileFilter"] = nameof(RelationshipType.DocumentSet);
            _facts["relatedFiles"] = main.Collections.RelatedFiles.Select(file => new
            {
                file.FileName, file.Relationship.Type, file.Relationship.Confidence, file.Relationship.Explanation,
            }).ToArray();
            _facts["relatedSemanticStatus"] = main.Collections.SemanticStatusText;
            await CaptureAsync(window, "06-related-files.png", "Evidence-backed Related Files", main.Collections.StatusText);

            await main.NavigateAsync(NavigationDestination.SemanticSearch);
            await main.SemanticSearch.RefreshIndexingStatusCommand.ExecuteAsync(null);
            await main.SemanticSearch.VectorIndex!.RefreshCommand.ExecuteAsync(null);
            FindExpander(searchView, "Semantic model and indexing").IsExpanded = true;
            FindExpander(searchView, "Background indexing status and maintenance").IsExpanded = true;
            _facts["indexingStatusText"] = main.SemanticSearch.BackgroundStateText;
            _facts["semanticStatusText"] = main.SemanticSearch.VectorIndex.StatusText;
            await CaptureAsync(window, "07-ai-indexing-status.png", "Actual disabled model and local indexing status",
                main.SemanticSearch.VectorIndex.StatusText);
            Require(_captures.Count == 7, "Exactly seven native window captures are required.");
            AssertFixtureUnchanged();
            succeeded = true;
        }
        catch (Exception exception)
        {
            failure = exception;
            Console.Error.WriteLine(exception);
        }
        finally
        {
            try
            {
                if (services is not null)
                {
                    await services.GetRequiredService<IApplicationHost>().ShutdownAsync();
                    await services.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20));
                }
                if (runState is not null) await runState.MarkCleanAsync();
                if (_before.Count > 0) AssertFixtureUnchanged();
            }
            catch (Exception exception)
            {
                succeeded = false;
                failure ??= exception;
                Console.Error.WriteLine(exception);
            }
            try { ownership?.Dispose(); }
            catch (Exception exception)
            {
                succeeded = false;
                failure ??= exception;
                Console.Error.WriteLine(exception);
            }
            await WriteManifestAsync(succeeded, failure?.ToString());
            desktop.Shutdown(succeeded ? 0 : 1);
        }
    }

    private static void AssertProductionAssembly()
    {
        var assembly = typeof(App).Assembly;
        var revision = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "SourceRevision").Value;
        Require(revision == Program.ProductionRevision, "Production assembly must carry the immutable RC source revision.");
        Console.WriteLine($"Production assembly: {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Console.WriteLine($"Capture harness: {Program.HarnessRevision}; OS: {RuntimeInformation.OSDescription}");
    }

    private static async Task AwaitIndexAsync(IBackgroundIndexingService indexing)
    {
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            var progress = await indexing.GetProgressAsync();
            if (progress.DiscoveryComplete && progress.Coverage.ExtractedTextCount == 9 && progress.Remaining == 0)
            {
                Require(progress.Failed == 0, "The synthetic indexing run must have no failed files.");
                Console.WriteLine($"Indexed: {progress.Completed}; extracted text: {progress.Coverage.ExtractedTextCount}; status: {progress.Status}");
                return;
            }
            await Task.Delay(250);
        }
        throw new TimeoutException($"Synthetic indexing did not finish: {JsonSerializer.Serialize(await indexing.GetProgressAsync())}");
    }

    private async Task CaptureAsync(Window window, string filename, string scenario, string actualStatus)
    {
        await SettleAsync();
        var handle = window.TryGetPlatformHandle() ?? throw new InvalidOperationException("No native window handle.");
        Require(handle.Handle != IntPtr.Zero, "A nonzero native window handle is required.");
        var path = Path.Combine(Program.OutputRoot, filename);
        var start = new ProcessStartInfo("import") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in new[] { "-window", $"0x{handle.Handle.ToInt64():x}", "-strip", path })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("ImageMagick import did not start.");
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Native X11 window capture timed out.");
        }
        Require(process.ExitCode == 0, $"ImageMagick import failed: {await errors}");
        var png = await File.ReadAllBytesAsync(path);
        Require(png.Length > 1024 && png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            $"{filename} is not a nonempty PNG.");
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        Require(width == 1600 && height == 1000, $"{filename} has unexpected native dimensions {width}x{height}.");
        _captures.Add(new { filename, scenario, actualStatus, width, height, bytes = png.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(png)), nativeHandleType = handle.HandleDescriptor,
            method = "ImageMagick import -window <real X11 window ID> -strip" });
        Console.WriteLine($"Captured {filename}: {width}x{height}, {png.Length} bytes. {actualStatus}");
        await WriteManifestAsync(false, null);
    }

    private static Expander FindExpander(Control root, string header) =>
        root.GetVisualDescendants().OfType<Expander>().FirstOrDefault(item => Equals(item.Header, header))
        ?? throw new InvalidOperationException($"Production expander was not realized: {header}");

    private static async Task ExpandTreesAsync(Control root)
    {
        for (var depth = 0; depth < 5; depth++)
        {
            foreach (var item in root.GetVisualDescendants().OfType<TreeViewItem>())
                item.IsExpanded = true;
            await SettleAsync();
        }
    }

    private static async Task SettleAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(600);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, string operation)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(100);
        Require(predicate(), $"{operation} did not settle.");
    }

    private static void CopyFixture(string source, string destination)
    {
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, overwrite: false);
            File.SetLastWriteTimeUtc(target, new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc));
        }
    }

    private static SortedDictionary<string, string> HashFixture(string root)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            result.Add(Path.GetRelativePath(root, file).Replace('\\', '/'),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        return result;
    }

    private static string HashManifest(SortedDictionary<string, string> files) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", files.Select(file => $"{file.Key}\t{file.Value}")))));

    private void AssertFixtureUnchanged()
    {
        Require(_before.SequenceEqual(HashFixture(_library)), "Source fixture paths or contents changed.");
        _facts["sourceFixtureUnchanged"] = true;
    }

    private Task WriteManifestAsync(bool completed, string? error) => File.WriteAllTextAsync(
        Path.Combine(Program.OutputRoot, "manifest.json"),
        JsonSerializer.Serialize(new
        {
            format = 1, productionTag = "v3.0.0-rc.1", productionSourceRevision = Program.ProductionRevision,
            captureHarnessRevision = Program.HarnessRevision,
            productionAssemblyVersion = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription, display = Environment.GetEnvironmentVariable("DISPLAY"),
            theme = "Light", width = 1600, height = 1000, capturedAtUtc = DateTimeOffset.UtcNow,
            fixture = "Nine synthetic UTF-8 text documents, including one byte-identical duplicate pair.",
            evidenceScope = "Native Linux X11 production-window pixels; command-driven synthetic workflow. Not a human usability, Windows, macOS, or learned-model validation.",
            completed, error, facts = _facts, captures = _captures,
        }, JsonOptions));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
