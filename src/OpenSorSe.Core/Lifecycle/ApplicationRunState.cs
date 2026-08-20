using System.Text.Json;
using OpenSorSe.Core.Persistence;

namespace OpenSorSe.Core.Lifecycle;

/// <summary>Exposes whether the prior desktop run reached its orderly shutdown boundary.</summary>
public interface IApplicationRunState
{
    /// <summary>Gets whether a prior marker existed.</summary>
    bool HadPreviousRun { get; }

    /// <summary>Gets whether the prior marker was left running.</summary>
    bool PreviousShutdownWasAbnormal { get; }

    /// <summary>Marks the current run clean after critical shutdown completes.</summary>
    Task MarkCleanAsync(CancellationToken cancellationToken = default);
}

/// <summary>Maintains one bounded atomic lifecycle marker without telemetry.</summary>
public sealed class ApplicationRunStateMarker : IApplicationRunState
{
    private const int CurrentVersion = 1;
    private const long MaximumBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    /// <summary>Gets a no-write state for isolated composition and helper tests.</summary>
    public static IApplicationRunState NotRequired { get; } = new IsolatedRunState();

    private ApplicationRunStateMarker(string path, bool hadPreviousRun, bool abnormal)
    {
        _path = path;
        HadPreviousRun = hadPreviousRun;
        PreviousShutdownWasAbnormal = abnormal;
    }

    /// <inheritdoc />
    public bool HadPreviousRun { get; }

    /// <inheritdoc />
    public bool PreviousShutdownWasAbnormal { get; }

    /// <summary>Reads the prior marker and atomically marks the current run active.</summary>
    public static ApplicationRunStateMarker Begin(string stateDirectory)
    {
        if (string.IsNullOrWhiteSpace(stateDirectory) || !Path.IsPathFullyQualified(stateDirectory))
        {
            throw new ArgumentException("An absolute state directory is required.", nameof(stateDirectory));
        }

        Directory.CreateDirectory(stateDirectory);
        var path = Path.Combine(stateDirectory, "application-run-state.json");
        RunMarker? previous = null;
        var hadPreviousRun = File.Exists(path);
        var invalidPreviousMarker = false;
        if (hadPreviousRun)
        {
            try
            {
                if (new FileInfo(path).Length <= MaximumBytes)
                {
                    previous = JsonSerializer.Deserialize<RunMarker>(File.ReadAllText(path), JsonOptions);
                }

                invalidPreviousMarker = previous is null;
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                invalidPreviousMarker = true;
            }
        }

        var marker = new ApplicationRunStateMarker(
            path,
            hadPreviousRun,
            hadPreviousRun && (invalidPreviousMarker || previous is null || !previous.Clean || previous.Version != CurrentVersion));
        marker.WriteAsync(clean: false, CancellationToken.None).GetAwaiter().GetResult();
        return marker;
    }

    /// <inheritdoc />
    public Task MarkCleanAsync(CancellationToken cancellationToken = default) => WriteAsync(clean: true, cancellationToken);

    private Task WriteAsync(bool clean, CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(
            _path,
            new RunMarker(
                CurrentVersion,
                clean,
                Environment.ProcessId,
                DateTimeOffset.UtcNow,
                ApplicationVersionInfo.Current,
                ApplicationVersionInfo.SourceRevision),
            JsonOptions,
            MaximumBytes,
            cancellationToken,
            static (_, _) => new InvalidDataException("The application lifecycle marker exceeded its bound."));

    private sealed record RunMarker(
        int Version,
        bool Clean,
        int ProcessId,
        DateTimeOffset UpdatedAtUtc,
        string ApplicationVersion,
        string SourceRevision);

    private sealed class IsolatedRunState : IApplicationRunState
    {
        public bool HadPreviousRun => false;
        public bool PreviousShutdownWasAbnormal => false;
        public Task MarkCleanAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
