using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Application.Explorer;

/// <summary>Defines the additive, one-shot OmniSorSe-to-OmniBrille bootstrap contract.</summary>
public static class ExplorerCompanionBootstrapContract
{
    /// <summary>Gets the established OmniBrille argument containing only the one-time local pipe name.</summary>
    public const string HandoffArgument = "--omnisorse-handoff";

    /// <summary>Gets the maximum serialized bootstrap frame size.</summary>
    public const int MaximumFrameBytes = 4 * 1024;

    /// <summary>Gets the default time allowed for a launched companion to connect and acknowledge.</summary>
    public static TimeSpan AcknowledgementTimeout { get; } = TimeSpan.FromSeconds(15);

    /// <summary>Creates strict JSON options shared by the bootstrap producer and companion.</summary>
    public static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 8,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };
}

/// <summary>Describes one conservatively discovered companion executable.</summary>
public sealed record ExplorerCompanionExecutable(
    string FileName,
    IReadOnlyList<string> PrefixArguments,
    string DiscoverySource);

/// <summary>Describes whether a reviewed OmniBrille executable was found.</summary>
public sealed record ExplorerCompanionDiscoveryResult(
    ExplorerCompanionExecutable? Executable,
    string Status,
    bool IsMisconfigured = false)
{
    /// <summary>Gets whether launch may be attempted.</summary>
    public bool IsAvailable => Executable is not null;
}

/// <summary>Locates OmniBrille without recursively searching the filesystem or affecting startup.</summary>
public interface IExplorerCompanionLocator
{
    /// <summary>Performs one bounded, on-demand discovery operation.</summary>
    ExplorerCompanionDiscoveryResult Locate();
}

/// <summary>Finds configured, adjacent, or conventionally installed OmniBrille executables.</summary>
public sealed class ExplorerCompanionLocator : IExplorerCompanionLocator
{
    /// <summary>Gets the explicit development/administrative path override.</summary>
    public const string EnvironmentPathOverride = "OMNISORSE_OMNIBRILLE_PATH";

    private readonly IConfigurationService _configuration;
    private readonly Func<string, string?> _environment;
    private readonly Func<string, bool> _fileExists;
    private readonly string _baseDirectory;

    /// <summary>Creates a locator that probes only a small set of documented locations when requested.</summary>
    public ExplorerCompanionLocator(IConfigurationService configuration)
        : this(configuration, Environment.GetEnvironmentVariable, File.Exists, AppContext.BaseDirectory)
    {
    }

    internal ExplorerCompanionLocator(
        IConfigurationService configuration,
        Func<string, string?> environment,
        Func<string, bool> fileExists,
        string baseDirectory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _baseDirectory = Path.GetFullPath(baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory)));
    }

    /// <inheritdoc />
    public ExplorerCompanionDiscoveryResult Locate()
    {
        var configured = _configuration.Current.ExplorerCompanion.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return FromExplicitPath(configured, "Settings");
        }

        var environment = _environment(EnvironmentPathOverride);
        if (!string.IsNullOrWhiteSpace(environment))
        {
            return FromExplicitPath(environment, $"{EnvironmentPathOverride} override");
        }

        foreach (var candidate in Candidates().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_fileExists(candidate))
            {
                return new ExplorerCompanionDiscoveryResult(
                    new ExplorerCompanionExecutable(candidate, [], "Installed location"),
                    "OmniBrille is available.");
            }
        }

        return new ExplorerCompanionDiscoveryResult(
            null,
            "OmniBrille was not found. Install it in a standard location or choose its executable in Settings.");
    }

    private ExplorerCompanionDiscoveryResult FromExplicitPath(string value, string source)
    {
        var path = value.Trim();
        if (!Path.IsPathRooted(path) || !_fileExists(path))
        {
            return new ExplorerCompanionDiscoveryResult(
                null,
                $"The OmniBrille executable configured through {source} is unavailable.",
                IsMisconfigured: true);
        }

        return new ExplorerCompanionDiscoveryResult(
            new ExplorerCompanionExecutable(Path.GetFullPath(path), [], source),
            "OmniBrille is available.");
    }

    private IEnumerable<string> Candidates()
    {
        var executables = OperatingSystem.IsWindows()
            ? new[] { "OmniBrille.exe", "OmniBrille.Desktop.exe" }
            : new[] { "OmniBrille", "OmniBrille.Desktop", "omnibrille" };
        foreach (var executable in executables)
        {
            yield return Path.Combine(_baseDirectory, executable);
        }

        if (OperatingSystem.IsWindows())
        {
            var local = _environment("LOCALAPPDATA");
            if (!string.IsNullOrWhiteSpace(local))
            {
                foreach (var executable in executables)
                {
                    yield return Path.Combine(local, "Programs", "OmniBrille", executable);
                }
            }

            var programFiles = _environment("ProgramFiles");
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                foreach (var executable in executables)
                {
                    yield return Path.Combine(programFiles, "OmniBrille", executable);
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/OmniBrille.app/Contents/MacOS/OmniBrille";
            var home = _environment("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                yield return Path.Combine(home, "Applications", "OmniBrille.app", "Contents", "MacOS", "OmniBrille");
            }
        }
        else
        {
            var home = _environment("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                yield return Path.Combine(home, ".local", "bin", "omnibrille");
            }

            yield return "/usr/local/bin/omnibrille";
            yield return "/usr/bin/omnibrille";
        }

        var pathValue = _environment("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (var executable in executables)
                {
                    yield return Path.Combine(directory, executable);
                }
            }
        }
    }
}

/// <summary>Identifies the user-visible outcome of one companion launch request.</summary>
public enum ExplorerCompanionLaunchStatus
{
    /// <summary>The companion acknowledged a valid Protocol v1 connection.</summary>
    Connected,

    /// <summary>No companion executable was available.</summary>
    Unavailable,

    /// <summary>No enabled indexed source could be authorized.</summary>
    NoAuthorizedSources,

    /// <summary>The executable could not be started.</summary>
    LaunchFailed,

    /// <summary>The companion did not acknowledge within the bounded interval.</summary>
    AcknowledgementTimedOut,

    /// <summary>The companion rejected the bootstrap or Explorer version.</summary>
    Incompatible,

    /// <summary>The companion reported Explorer authentication rejection.</summary>
    AuthenticationRejected,

    /// <summary>The bootstrap failed safely.</summary>
    BootstrapFailed,

    /// <summary>The launch was cancelled by the caller.</summary>
    Cancelled,
}

/// <summary>Contains the bounded result presented by the desktop.</summary>
public sealed record ExplorerCompanionLaunchResult(
    ExplorerCompanionLaunchStatus Status,
    string Message,
    string? LaunchId = null,
    string? SessionId = null)
{
    /// <summary>Gets whether the companion established the intended session.</summary>
    public bool IsConnected => Status == ExplorerCompanionLaunchStatus.Connected;
}

/// <summary>Launches OmniBrille through one scoped local bootstrap.</summary>
public interface IExplorerCompanionLaunchService
{
    /// <summary>Discovers, authorizes, launches, and waits for bounded acknowledgement.</summary>
    Task<ExplorerCompanionLaunchResult> LaunchAsync(CancellationToken cancellationToken = default);
}

internal interface IExplorerCompanionProcess : IDisposable
{
    int Id { get; }
    Task Completion { get; }
}

internal sealed record ExplorerCompanionBootstrapResult(
    bool GrantDelivered,
    IExplorerCompanionProcess? Process,
    bool TimedOut,
    string? FailureCode = null);

internal interface IExplorerCompanionBootstrapTransport
{
    Task<ExplorerCompanionBootstrapResult> LaunchAsync(
        ExplorerCompanionExecutable executable,
        ExplorerSessionGrant grant,
        string handoffEndpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal interface IExplorerCompanionProcessStarter
{
    IExplorerCompanionProcess Start(
        ExplorerCompanionExecutable executable,
        string handoffEndpoint);
}

internal sealed class SystemExplorerCompanionProcessStarter : IExplorerCompanionProcessStarter
{
    public IExplorerCompanionProcess Start(
        ExplorerCompanionExecutable executable,
        string handoffEndpoint)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable.FileName,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable.FileName)) ?? AppContext.BaseDirectory,
            CreateNoWindow = false,
        };
        foreach (var argument in executable.PrefixArguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.ArgumentList.Add(ExplorerCompanionBootstrapContract.HandoffArgument);
        start.ArgumentList.Add(handoffEndpoint);
        var process = Process.Start(start) ?? throw new InvalidOperationException("The OmniBrille process could not be started.");
        return new SystemExplorerCompanionProcess(process);
    }

    private sealed class SystemExplorerCompanionProcess : IExplorerCompanionProcess
    {
        private readonly Process _process;
        private readonly Task _completion;

        public SystemExplorerCompanionProcess(Process process)
        {
            _process = process;
            Id = process.Id;
            _completion = CompleteAsync();
        }

        public int Id { get; }
        public Task Completion => _completion;

        public void Dispose() => _process.Dispose();

        private async Task CompleteAsync()
        {
            try
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // A process that exits between Start and observation is already terminal.
            }
        }
    }
}

internal sealed class NamedPipeExplorerCompanionBootstrapTransport : IExplorerCompanionBootstrapTransport
{
    private readonly IExplorerCompanionProcessStarter _starter;
    private readonly JsonSerializerOptions _json = ExplorerCompanionBootstrapContract.CreateJsonOptions();

    public NamedPipeExplorerCompanionBootstrapTransport(IExplorerCompanionProcessStarter starter) =>
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));

    public async Task<ExplorerCompanionBootstrapResult> LaunchAsync(
        ExplorerCompanionExecutable executable,
        ExplorerSessionGrant grant,
        string handoffEndpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(grant);
        if (string.IsNullOrWhiteSpace(handoffEndpoint) || handoffEndpoint.Length > 128 || handoffEndpoint.Any(char.IsControl))
        {
            throw new ArgumentException("The one-time OmniBrille handoff endpoint is invalid.", nameof(handoffEndpoint));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        await using var handoff = new NamedPipeServerStream(
            handoffEndpoint,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        IExplorerCompanionProcess? process = null;
        try
        {
            process = _starter.Start(executable, handoffEndpoint);
            using var handoffCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handoffCancellation.CancelAfter(timeout);
            try
            {
                var connection = handoff.WaitForConnectionAsync(handoffCancellation.Token);
                if (await Task.WhenAny(connection, process.Completion).ConfigureAwait(false) == process.Completion)
                {
                    handoffCancellation.Cancel();
                    try
                    {
                        await connection.ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is OperationCanceledException or IOException)
                    {
                        // The companion exited before consuming its one-time handoff.
                    }

                    return new ExplorerCompanionBootstrapResult(false, process, false, "companion-exited-before-handoff");
                }

                await connection.ConfigureAwait(false);
                await WriteFrameAsync(handoff, grant, _json, handoffCancellation.Token).ConfigureAwait(false);
                return new ExplorerCompanionBootstrapResult(true, process, false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ExplorerCompanionBootstrapResult(false, process, true, "handoff-timeout");
            }
        }
        catch
        {
            process?.Dispose();
            throw;
        }
    }

    internal static async Task WriteFrameAsync<T>(
        Stream stream,
        T value,
        JsonSerializerOptions json,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, json);
        if (payload.Length is 0 or > ExplorerCompanionBootstrapContract.MaximumFrameBytes)
        {
            throw new InvalidDataException("The companion bootstrap frame exceeds its bounded size.");
        }

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, payload.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

}

/// <summary>Coordinates one scoped, lazy, failure-isolated OmniBrille launch.</summary>
public sealed class ExplorerCompanionLaunchService : IExplorerCompanionLaunchService
{
    private static readonly TimeSpan ExplorerSessionLifetime = TimeSpan.FromMinutes(15);
    private readonly IExplorerCompanionLocator _locator;
    private readonly IExplorerCompanionBootstrapTransport _bootstrap;
    private readonly IExplorerProtocolHost _host;
    private readonly IExplorerSessionConnectionObserver? _connectionObserver;
    private readonly IExplorerDataSource _data;
    private readonly IDiagnosticsEventSink? _diagnostics;
    private readonly ConcurrentDictionary<string, Task> _processMonitors = new(StringComparer.Ordinal);

    /// <summary>Creates a production launch coordinator over existing Explorer boundaries.</summary>
    public ExplorerCompanionLaunchService(
        IExplorerCompanionLocator locator,
        IExplorerProtocolHost host,
        IExplorerDataSource data,
        IDiagnosticsEventSink? diagnostics = null)
        : this(
            locator,
            new NamedPipeExplorerCompanionBootstrapTransport(new SystemExplorerCompanionProcessStarter()),
            host,
            data,
            diagnostics)
    {
    }

    internal ExplorerCompanionLaunchService(
        IExplorerCompanionLocator locator,
        IExplorerCompanionBootstrapTransport bootstrap,
        IExplorerProtocolHost host,
        IExplorerDataSource data,
        IDiagnosticsEventSink? diagnostics = null)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _connectionObserver = host as IExplorerSessionConnectionObserver;
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _diagnostics = DiagnosticsIsolation.Protect(diagnostics);
    }

    /// <inheritdoc />
    public async Task<ExplorerCompanionLaunchResult> LaunchAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var discovery = _locator.Locate();
        if (!discovery.IsAvailable || discovery.Executable is null)
        {
            return Complete(
                discovery.IsMisconfigured ? ExplorerCompanionLaunchStatus.LaunchFailed : ExplorerCompanionLaunchStatus.Unavailable,
                discovery.Status);
        }

        string? diagnosticSession = null;
        string? sessionId = null;
        try
        {
            var sources = (await _data.GetSourcesAsync(cancellationToken).ConfigureAwait(false))
                .Where(source => source.Enabled)
                .Select(source => source.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Take(ExplorerProtocolDefaults.MaximumAuthorizedSources + 1)
                .ToArray();
            if (sources.Length == 0)
            {
                return Complete(
                    ExplorerCompanionLaunchStatus.NoAuthorizedSources,
                    "Index at least one enabled source before opening OmniBrille.");
            }

            if (sources.Length > ExplorerProtocolDefaults.MaximumAuthorizedSources)
            {
                return Complete(
                    ExplorerCompanionLaunchStatus.NoAuthorizedSources,
                    "The enabled indexed-source set exceeds the bounded Explorer authorization limit.");
            }

            diagnosticSession = _diagnostics?.BeginSession(
                DiagnosticCategory.SearchAndIndexing,
                "Launch OmniBrille",
                [new DiagnosticField("Authorized source count", sources.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
            var grant = await _host.CreateSessionAsync(
                new ExplorerSessionScope(sources, IncludeAuthorizedPaths: false),
                ExplorerSessionLifetime,
                cancellationToken).ConfigureAwait(false);
            sessionId = grant.SessionId;
            var launchId = RandomId();
            // Keep the opaque name compact because Unix named pipes map to domain-socket paths
            // whose platform limit includes the operating system's temporary-directory prefix.
            var handoffEndpoint = "obh-" + launchId;
            var acknowledgementStopwatch = Stopwatch.StartNew();
            var result = await _bootstrap.LaunchAsync(
                discovery.Executable,
                grant,
                handoffEndpoint,
                ExplorerCompanionBootstrapContract.AcknowledgementTimeout,
                cancellationToken).ConfigureAwait(false);

            ExplorerSessionConnectionOutcome? connectionOutcome = null;
            if (result.GrantDelivered && result.Process is not null && _connectionObserver is not null)
            {
                var remaining = ExplorerCompanionBootstrapContract.AcknowledgementTimeout - acknowledgementStopwatch.Elapsed;
                var observed = _connectionObserver.WaitForConnectionOutcomeAsync(
                    sessionId,
                    remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                    cancellationToken);
                var completion = await Task.WhenAny(observed, result.Process.Completion).ConfigureAwait(false);
                connectionOutcome = completion == observed ? await observed.ConfigureAwait(false) : null;
            }

            if (connectionOutcome == ExplorerSessionConnectionOutcome.Authenticated && result.Process is not null)
            {
                _diagnostics?.Complete(
                    diagnosticSession,
                    DiagnosticStatus.Succeeded,
                    stopwatch.Elapsed,
                    "OmniBrille acknowledged a scoped local Explorer session.");
                TrackProcess(launchId, sessionId, result.Process);
                return new ExplorerCompanionLaunchResult(
                    ExplorerCompanionLaunchStatus.Connected,
                    "OmniBrille connected to the authorized local index.",
                    launchId,
                    sessionId);
            }

            result.Process?.Dispose();
            await _host.RevokeSessionAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
            sessionId = null;
            var failed = connectionOutcome switch
            {
                ExplorerSessionConnectionOutcome.IncompatibleVersion => new ExplorerCompanionLaunchResult(
                    ExplorerCompanionLaunchStatus.Incompatible,
                    "This OmniBrille version does not support Explorer Protocol v1.",
                    launchId),
                ExplorerSessionConnectionOutcome.AuthenticationRejected => new ExplorerCompanionLaunchResult(
                    ExplorerCompanionLaunchStatus.AuthenticationRejected,
                    "OmniBrille could not authenticate the scoped local Explorer session.",
                    launchId),
                ExplorerSessionConnectionOutcome.Revoked => new ExplorerCompanionLaunchResult(
                    ExplorerCompanionLaunchStatus.AcknowledgementTimedOut,
                    "The one-time OmniBrille session expired or was revoked before connection completed.",
                    launchId),
                _ when result.TimedOut || result.GrantDelivered =>
                    new ExplorerCompanionLaunchResult(
                        ExplorerCompanionLaunchStatus.AcknowledgementTimedOut,
                        result.GrantDelivered
                            ? "OmniBrille received the one-time handoff but did not establish Explorer Protocol v1 in time."
                            : "OmniBrille started but did not receive the one-time local handoff in time.",
                        launchId),
                _ => new ExplorerCompanionLaunchResult(
                    ExplorerCompanionLaunchStatus.BootstrapFailed,
                    "OmniBrille could not complete its local connection bootstrap.",
                    launchId),
            };
            _diagnostics?.Complete(
                diagnosticSession,
                DiagnosticStatus.Rejected,
                stopwatch.Elapsed,
                failed.Message,
                DiagnosticSeverity.Warning,
                [new DiagnosticField("Failure category", failed.Status.ToString())]);
            return failed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (sessionId is not null)
            {
                await _host.RevokeSessionAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
            }

            _diagnostics?.Complete(
                diagnosticSession,
                DiagnosticStatus.Cancelled,
                stopwatch.Elapsed,
                "The OmniBrille launch was cancelled.");
            return Complete(ExplorerCompanionLaunchStatus.Cancelled, "Opening OmniBrille was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            if (sessionId is not null)
            {
                await _host.RevokeSessionAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
            }

            _diagnostics?.Complete(
                diagnosticSession,
                DiagnosticStatus.Failed,
                stopwatch.Elapsed,
                "OmniBrille could not be launched safely.",
                DiagnosticSeverity.Warning,
                [new DiagnosticField("Failure category", exception.GetType().Name)]);
            return Complete(
                ExplorerCompanionLaunchStatus.LaunchFailed,
                "OmniBrille could not be started. Check its executable path in Settings.");
        }
    }

    private void TrackProcess(string launchId, string sessionId, IExplorerCompanionProcess process)
    {
        var monitor = MonitorProcessAsync(sessionId, process);
        _processMonitors[launchId] = monitor;
        _ = monitor.ContinueWith(
            _ => _processMonitors.TryRemove(launchId, out var ignored),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task MonitorProcessAsync(string sessionId, IExplorerCompanionProcess process)
    {
        using (process)
        {
            try
            {
                await process.Completion.ConfigureAwait(false);
            }
            catch
            {
                // A companion process failure is isolated from OmniSorSe.
            }
        }

        try
        {
            await _host.RevokeSessionAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // OmniSorSe shutdown already revoked and zeroed all Explorer session secrets.
        }
    }

    private static ExplorerCompanionLaunchResult Complete(ExplorerCompanionLaunchStatus status, string message) =>
        new(status, message);

    private static string RandomId() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
}
