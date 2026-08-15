using OpenSorSe.Application.Explorer;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Application.Guidance;

/// <summary>Identifies the small user-facing readiness vocabulary used by Home.</summary>
public enum OptionalCapabilityState
{
    /// <summary>The optional capability can be used when requested.</summary>
    Ready,
    /// <summary>The user intentionally disabled the capability.</summary>
    Disabled,
    /// <summary>Required local configuration has not been supplied.</summary>
    NotConfigured,
    /// <summary>The configured or conventionally discovered dependency is unavailable.</summary>
    Unavailable,
    /// <summary>The configuration should be reviewed before the capability can be used.</summary>
    NeedsAttention,
}

/// <summary>Contains one bounded, non-sensitive optional-capability summary.</summary>
public sealed record OptionalCapabilityReadiness(
    string Id,
    string DisplayName,
    OptionalCapabilityState State,
    string Explanation);

/// <summary>Contains the durable, bounded library state needed by Home.</summary>
public sealed record ProductReadinessSnapshot
{
    /// <summary>Gets the number of registered durable indexing sources.</summary>
    public int SourceCount { get; init; }
    /// <summary>Gets the number of active files known to the durable index.</summary>
    public long KnownFileCount { get; init; }
    /// <summary>Gets whether filename and basic metadata Search is ready for every known file.</summary>
    public bool IsBaseSearchReady { get; init; }
    /// <summary>Gets the current truthful progressive-indexing phase.</summary>
    public IndexingProgressPhase Phase { get; init; } = IndexingProgressPhase.Complete;
    /// <summary>Gets the number of retained retryable or permanent stage failures.</summary>
    public long FailedStageCount { get; init; }
    /// <summary>Gets the number of unresolved Moderate Smart Tag files.</summary>
    public long PendingReviewCount { get; init; }
    /// <summary>Gets the total number of local dynamic Saved View rules.</summary>
    public int SavedViewCount { get; init; }
    /// <summary>Gets at most a few recently updated Saved View shortcuts without evaluating them.</summary>
    public IReadOnlyList<SavedDiscoveryView> SavedViewShortcuts { get; init; } = [];
    /// <summary>Gets compact optional-capability states produced only on an explicit Home refresh.</summary>
    public IReadOnlyList<OptionalCapabilityReadiness> Capabilities { get; init; } = [];
}

/// <summary>Queries durable product state without hydrating file or Smart Tag graphs.</summary>
public interface IProductReadinessService
{
    /// <summary>Returns one bounded point-in-time Home projection.</summary>
    Task<ProductReadinessSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds Home readiness from durable counts and existing bounded capability discovery.
/// It never executes Saved Views, contacts Ollama, or launches external tools.
/// </summary>
public sealed class ProductReadinessService : IProductReadinessService
{
    private const int MaximumSavedViewShortcuts = 3;
    private readonly IBackgroundIndexingService _indexing;
    private readonly ISavedDiscoveryViewStore _savedViews;
    private readonly IConfigurationService _configuration;
    private readonly IExternalToolLocator _toolLocator;
    private readonly IExplorerCompanionLocator _companionLocator;

    /// <summary>Initializes the bounded durable-state projector.</summary>
    public ProductReadinessService(
        IBackgroundIndexingService indexing,
        ISavedDiscoveryViewStore savedViews,
        IConfigurationService configuration,
        IExternalToolLocator toolLocator,
        IExplorerCompanionLocator companionLocator)
    {
        _indexing = indexing ?? throw new ArgumentNullException(nameof(indexing));
        _savedViews = savedViews ?? throw new ArgumentNullException(nameof(savedViews));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _toolLocator = toolLocator ?? throw new ArgumentNullException(nameof(toolLocator));
        _companionLocator = companionLocator ?? throw new ArgumentNullException(nameof(companionLocator));
    }

    /// <inheritdoc />
    public async Task<ProductReadinessSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var progressTask = _indexing.GetProgressAsync(cancellationToken);
        var sourcesTask = _indexing.GetSourcesAsync(cancellationToken);
        var viewsTask = _savedViews.ListAsync(cancellationToken);
        var unresolvedTask = _indexing.GetDiscoveryCandidatesAsync(
            new DiscoverySearchRequest(
                string.Empty,
                [new SearchFilter(
                    "home:unresolved-moderate",
                    SearchFilterKind.UnresolvedModerateSmartTag,
                    "true",
                    "Unresolved Moderate Smart Tag suggestions")],
                1),
            cancellationToken);

        await Task.WhenAll(progressTask, sourcesTask, viewsTask, unresolvedTask).ConfigureAwait(false);
        var progress = await progressTask.ConfigureAwait(false);
        var views = await viewsTask.ConfigureAwait(false);
        var unresolved = await unresolvedTask.ConfigureAwait(false);

        return new ProductReadinessSnapshot
        {
            SourceCount = (await sourcesTask.ConfigureAwait(false)).Count,
            KnownFileCount = progress.Coverage.KnownFileCount,
            IsBaseSearchReady = progress.IsBaseCoverageComplete && progress.Coverage.IsAvailable,
            Phase = progress.Phase,
            FailedStageCount = progress.Coverage.FailedStageCount,
            PendingReviewCount = unresolved.CandidateCoverage.UsedCompleteLibrarySelection
                ? unresolved.CandidateCoverage.MatchingFileCount
                : 0,
            SavedViewCount = views.Count,
            SavedViewShortcuts = views
                .OrderByDescending(view => view.UpdatedAtUtc)
                .ThenBy(view => view.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaximumSavedViewShortcuts)
                .ToArray(),
            Capabilities = InspectCapabilities(_configuration.Current),
        };
    }

    private IReadOnlyList<OptionalCapabilityReadiness> InspectCapabilities(ApplicationSettings settings)
    {
        var content = settings.Content;
        var media = settings.MediaIntelligence;
        return
        [
            InspectOllama(settings),
            InspectTool(
                "tesseract",
                "Tesseract OCR",
                content.OcrEnabled || media.ImageOcrEnabled,
                "tesseract",
                content.TesseractExecutablePath,
                "OCR-based image and scanned-document text will not be extracted."),
            InspectTool(
                "ffprobe",
                "ffprobe",
                media.Enabled && (media.AudioMetadataEnabled || media.VideoMetadataEnabled),
                "ffprobe",
                media.FfprobeExecutablePath,
                "Audio and video metadata inspection is unavailable."),
            InspectTool(
                "ffmpeg",
                "ffmpeg",
                media.Enabled && (media.VideoFrameAnalysisEnabled || media.VideoTranscriptionEnabled),
                "ffmpeg",
                media.FfmpegExecutablePath,
                "Video frame analysis and video-audio extraction are unavailable."),
            InspectWhisper(media),
            InspectCompanion(),
        ];
    }

    private static OptionalCapabilityReadiness InspectOllama(ApplicationSettings settings)
    {
        if (!settings.Ai.Enabled)
        {
            return new("ollama", "Ollama", OptionalCapabilityState.Disabled, "Optional local AI assistance is off.");
        }

        return string.IsNullOrWhiteSpace(settings.Ai.SelectedModel)
            ? new("ollama", "Ollama", OptionalCapabilityState.NotConfigured, "Choose a local model in Settings to use optional AI assistance.")
            : new("ollama", "Ollama", OptionalCapabilityState.Ready, "Configured for an on-demand local availability check; Home does not contact the provider.");
    }

    private OptionalCapabilityReadiness InspectTool(
        string id,
        string displayName,
        bool enabled,
        string command,
        string? configuredPath,
        string unavailableExplanation)
    {
        if (!enabled)
        {
            return new(id, displayName, OptionalCapabilityState.Disabled, "This optional capability is off.");
        }

        var location = _toolLocator.Locate(command, configuredPath);
        if (location.IsAvailable)
        {
            return new(id, displayName, OptionalCapabilityState.Ready, "A local executable is available for on-demand work.");
        }

        return new(
            id,
            displayName,
            string.IsNullOrWhiteSpace(configuredPath) ? OptionalCapabilityState.NotConfigured : OptionalCapabilityState.NeedsAttention,
            unavailableExplanation);
    }

    private OptionalCapabilityReadiness InspectWhisper(MediaIntelligenceSettings settings)
    {
        if (!settings.Enabled || !settings.AudioTranscriptionEnabled && !settings.VideoTranscriptionEnabled)
        {
            return new("whisper", "whisper.cpp", OptionalCapabilityState.Disabled, "Optional local transcription is off.");
        }

        if (string.IsNullOrWhiteSpace(settings.WhisperModelPath))
        {
            return new("whisper", "whisper.cpp", OptionalCapabilityState.NotConfigured, "Choose a user-managed runtime and model to enable local transcription.");
        }

        var runtime = _toolLocator.Locate("whisper-cli", settings.WhisperExecutablePath);
        var modelAvailable = File.Exists(settings.WhisperModelPath);
        return runtime.IsAvailable && modelAvailable
            ? new("whisper", "whisper.cpp", OptionalCapabilityState.Ready, "The user-managed local runtime and model are available.")
            : new("whisper", "whisper.cpp", OptionalCapabilityState.NeedsAttention, "The configured local transcription runtime or model is unavailable.");
    }

    private OptionalCapabilityReadiness InspectCompanion()
    {
        var companion = _companionLocator.Locate();
        return companion.IsAvailable
            ? new("omnibrille", "OmniBrille", OptionalCapabilityState.Ready, "The optional visual companion can be launched on demand.")
            : new(
                "omnibrille",
                "OmniBrille",
                companion.IsMisconfigured ? OptionalCapabilityState.NeedsAttention : OptionalCapabilityState.NotConfigured,
                "The visual companion is unavailable; OmniSorSe remains fully usable.");
    }
}
