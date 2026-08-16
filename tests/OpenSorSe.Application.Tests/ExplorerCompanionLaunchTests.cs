using OmniSorSe.ExplorerProtocol;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenSorSe.Application.Explorer;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Application.Tests;

/// <summary>Protects the lazy, scoped, one-shot OmniBrille launch boundary.</summary>
public sealed class ExplorerCompanionLaunchTests
{
    private static readonly ExplorerCompanionExecutable Executable = new(
        Path.Combine(Path.GetTempPath(), "OmniBrille.exe"),
        [],
        "test");

    /// <summary>An absent optional companion never starts or authorizes the dormant Explorer host.</summary>
    [Fact]
    public async Task UnavailableCompanion_LeavesHostDormant()
    {
        var host = new FakeHost();
        var bootstrap = new FakeBootstrap();
        var service = Service(
            new FakeLocator(new ExplorerCompanionDiscoveryResult(null, "not installed")),
            bootstrap,
            host,
            new FakeDataSource(Source("source-a", enabled: true)));

        var result = await service.LaunchAsync();

        Assert.Equal(ExplorerCompanionLaunchStatus.Unavailable, result.Status);
        Assert.Equal(ExplorerProtocolHostState.Unavailable, host.State);
        Assert.Equal(0, host.CreateCount);
        Assert.Equal(0, bootstrap.LaunchCount);
    }

    /// <summary>Successful launch preserves only enabled indexed-source scope and omits raw paths.</summary>
    [Fact]
    public async Task SuccessfulLaunch_IssuesScopedGrantAndAcknowledgesConnection()
    {
        var host = new FakeHost();
        var process = new FakeProcess();
        var bootstrap = FakeBootstrap.Connected(process);
        var service = Service(
            Available(),
            bootstrap,
            host,
            new FakeDataSource(Source("enabled", true), Source("disabled", false)));

        var result = await service.LaunchAsync();

        Assert.True(result.IsConnected);
        var scope = Assert.Single(host.Scopes);
        Assert.Equal(["enabled"], scope.AuthorizedSourceIds);
        Assert.False(scope.IncludeAuthorizedPaths);
        var grant = Assert.Single(bootstrap.Grants);
        Assert.Equal(result.SessionId, grant.SessionId);
        Assert.DoesNotContain("enabled", grant.AuthorizationToken, StringComparison.Ordinal);
        Assert.Equal("omnibrille-handoff-" + result.LaunchId, Assert.Single(bootstrap.HandoffEndpoints));
        process.Exit();
        await WaitUntilAsync(() => host.RevokedSessionIds.Contains(result.SessionId!));
    }

    /// <summary>Every repeated launch receives independent launch, session, and token material.</summary>
    [Fact]
    public async Task RepeatedLaunches_ReceiveIndependentOneTimeBootstraps()
    {
        var host = new FakeHost();
        var processes = new Queue<FakeProcess>([new(), new()]);
        var bootstrap = new FakeBootstrap(_ =>
        {
            var process = processes.Dequeue();
            return new ExplorerCompanionBootstrapResult(true, process, false);
        });
        var service = Service(Available(), bootstrap, host, new FakeDataSource(Source("source-a", true)));

        var first = await service.LaunchAsync();
        var second = await service.LaunchAsync();

        Assert.NotEqual(first.LaunchId, second.LaunchId);
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.NotEqual(
            bootstrap.Grants[0].AuthorizationToken,
            bootstrap.Grants[1].AuthorizationToken);
        Assert.NotEqual(bootstrap.HandoffEndpoints[0], bootstrap.HandoffEndpoints[1]);
        foreach (var process in bootstrap.Processes)
        {
            process.Exit();
        }
    }

    /// <summary>Expiry/no acknowledgement revokes the Explorer session and cannot leave false success.</summary>
    [Fact]
    public async Task AcknowledgementTimeout_RevokesIssuedSession()
    {
        var host = new FakeHost();
        var bootstrap = new FakeBootstrap(_ =>
            new ExplorerCompanionBootstrapResult(false, new FakeProcess(), true, "timeout"));
        var service = Service(Available(), bootstrap, host, new FakeDataSource(Source("source-a", true)));

        var result = await service.LaunchAsync();

        Assert.Equal(ExplorerCompanionLaunchStatus.AcknowledgementTimedOut, result.Status);
        Assert.Single(host.RevokedSessionIds);
        Assert.Null(result.SessionId);
    }

    /// <summary>A delivered grant without compatible authenticated Protocol use never becomes false success.</summary>
    [Fact]
    public async Task DeliveredGrantWithoutProtocolUse_FailsClosed()
    {
        var host = new FakeHost { ConnectionOutcome = null };
        var process = new FakeProcess();
        process.Exit();
        var bootstrap = new FakeBootstrap(_ => new ExplorerCompanionBootstrapResult(true, process, false));
        var service = Service(Available(), bootstrap, host, new FakeDataSource(Source("source-a", true)));

        var result = await service.LaunchAsync();

        Assert.Equal(ExplorerCompanionLaunchStatus.AcknowledgementTimedOut, result.Status);
        Assert.Single(host.RevokedSessionIds);
    }

    /// <summary>Attributable authentication and version failures remain distinct and fail closed.</summary>
    [Theory]
    [InlineData(ExplorerSessionConnectionOutcome.AuthenticationRejected, ExplorerCompanionLaunchStatus.AuthenticationRejected)]
    [InlineData(ExplorerSessionConnectionOutcome.IncompatibleVersion, ExplorerCompanionLaunchStatus.Incompatible)]
    [InlineData(ExplorerSessionConnectionOutcome.Revoked, ExplorerCompanionLaunchStatus.AcknowledgementTimedOut)]
    public async Task AttributableConnectionFailure_IsReportedHonestly(
        ExplorerSessionConnectionOutcome outcome,
        ExplorerCompanionLaunchStatus expected)
    {
        var host = new FakeHost { ConnectionOutcome = outcome };
        var service = Service(
            Available(),
            FakeBootstrap.Connected(new FakeProcess()),
            host,
            new FakeDataSource(Source("source-a", true)));

        var result = await service.LaunchAsync();

        Assert.Equal(expected, result.Status);
        Assert.Single(host.RevokedSessionIds);
    }

    /// <summary>A process-start failure is isolated and the newly issued session is revoked.</summary>
    [Fact]
    public async Task ProcessLaunchFailure_DoesNotAffectOmniSorSe()
    {
        var host = new FakeHost();
        var bootstrap = new FakeBootstrap(_ => throw new System.ComponentModel.Win32Exception("synthetic"));
        var service = Service(Available(), bootstrap, host, new FakeDataSource(Source("source-a", true)));

        var result = await service.LaunchAsync();

        Assert.Equal(ExplorerCompanionLaunchStatus.LaunchFailed, result.Status);
        Assert.Single(host.RevokedSessionIds);
    }

    /// <summary>No arbitrary root is synthesized when the established index has no enabled source.</summary>
    [Fact]
    public async Task NoEnabledIndexedSource_DoesNotCreateSession()
    {
        var host = new FakeHost();
        var service = Service(
            Available(),
            new FakeBootstrap(),
            host,
            new FakeDataSource(Source("disabled", false)));

        var result = await service.LaunchAsync();

        Assert.Equal(ExplorerCompanionLaunchStatus.NoAuthorizedSources, result.Status);
        Assert.Equal(0, host.CreateCount);
    }

    /// <summary>Configured discovery is bounded, takes precedence, and reports stale paths honestly.</summary>
    [Fact]
    public void Locator_UsesConfiguredAbsolutePathWithoutRecursiveSearch()
    {
        var configured = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "reviewed", "OmniBrille.exe"));
        var settings = new ApplicationSettings
        {
            ExplorerCompanion = new ExplorerCompanionSettings { ExecutablePath = configured },
        };
        var configuration = new FakeConfiguration(settings);
        var checkedPaths = new List<string>();
        var locator = new ExplorerCompanionLocator(
            configuration,
            _ => null,
            path =>
            {
                checkedPaths.Add(path);
                return string.Equals(path, configured, StringComparison.Ordinal);
            },
            Path.GetTempPath());

        var available = locator.Locate();
        Assert.True(available.IsAvailable);
        Assert.Equal([configured], checkedPaths);

        var missing = new ExplorerCompanionLocator(
            configuration,
            _ => null,
            _ => false,
            Path.GetTempPath()).Locate();
        Assert.False(missing.IsAvailable);
        Assert.True(missing.IsMisconfigured);
    }

    /// <summary>Strict bootstrap JSON rejects unknown fields and oversized frames.</summary>
    [Fact]
    public async Task BootstrapContract_IsStrictAndBounded()
    {
        var json = ExplorerCompanionBootstrapContract.CreateJsonOptions();
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExplorerSessionGrant>(
            """{"transport":"named-pipe","endpoint":"x","sessionId":"s","authorizationToken":"t","expiresAtUtc":"2030-01-01T00:00:00Z","protocolMajor":1,"protocolMinor":0,"extra":true}""",
            json));
        await using var stream = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            NamedPipeExplorerCompanionBootstrapTransport.WriteFrameAsync(
                stream,
                new string('x', ExplorerCompanionBootstrapContract.MaximumFrameBytes * 2),
                json,
                CancellationToken.None));
    }

    /// <summary>The named bootstrap serves one current-user grant and cannot be replayed through the same endpoint.</summary>
    [Fact]
    public async Task NamedPipeHandoff_IsSingleUse()
    {
        var endpoint = $"omnibrille-handoff-{Guid.NewGuid():N}";
        var grant = new ExplorerSessionGrant(
            "named-pipe",
            $"ose-{Guid.NewGuid():N}",
            "session",
            Convert.ToBase64String(Guid.NewGuid().ToByteArray()),
            DateTimeOffset.UtcNow.AddMinutes(1),
            ExplorerProtocolVersion.Major,
            ExplorerProtocolVersion.Minor);
        var starter = new ReadingProcessStarter();
        var transport = new NamedPipeExplorerCompanionBootstrapTransport(starter);

        var result = await transport.LaunchAsync(
            Executable,
            grant,
            endpoint,
            TimeSpan.FromSeconds(3),
            CancellationToken.None);

        Assert.True(result.GrantDelivered);
        Assert.Equal(grant, await starter.ReceivedGrant);
        using var second = new System.IO.Pipes.NamedPipeClientStream(
            ".",
            endpoint,
            System.IO.Pipes.PipeDirection.In,
            System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.ConnectAsync(timeout.Token));
        starter.Process.Exit();
    }

    /// <summary>Runs the established one-time handoff and Protocol v1 negotiation in a separate process.</summary>
    [Fact]
    public async Task SeparateProcessHarness_CompletesBootstrapAndProtocolNegotiation()
    {
        var harness = HarnessPath();
        Assert.True(File.Exists(harness), $"Harness was not built at {harness}.");
        var source = new FakeDataSource(Source("source-a", true));
        await using var host = new NamedPipeExplorerProtocolHost(
            source,
            PlatformServices.CreatePathSemantics(HostPlatformKind.Windows));
        var transport = new CapturingBootstrapTransport(
            new NamedPipeExplorerCompanionBootstrapTransport(new SystemExplorerCompanionProcessStarter()));
        var locator = new FakeLocator(new ExplorerCompanionDiscoveryResult(
            new ExplorerCompanionExecutable(DotnetHostPath(), [harness], "test harness"),
            "available"));
        var service = new ExplorerCompanionLaunchService(
            locator,
            transport,
            host,
            source);

        var result = await service.LaunchAsync();

        Assert.True(result.IsConnected, result.Message);
        var grant = Assert.Single(transport.Grants);
        Assert.Equal(result.SessionId, grant.SessionId);
        Assert.False(grant.ExpiresAtUtc <= DateTimeOffset.UtcNow);
        Assert.True(grant.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(15));
        Assert.StartsWith("omnibrille-handoff-", Assert.Single(transport.HandoffEndpoints), StringComparison.Ordinal);
        await WaitUntilAsync(() => host.State == ExplorerProtocolHostState.Disconnected);
    }

    private static ExplorerCompanionLaunchService Service(
        IExplorerCompanionLocator locator,
        IExplorerCompanionBootstrapTransport bootstrap,
        IExplorerProtocolHost host,
        IExplorerDataSource data) => new(locator, bootstrap, host, data);

    private static FakeLocator Available() => new(new ExplorerCompanionDiscoveryResult(Executable, "available"));

    private static IndexingSource Source(string id, bool enabled) => new(
        id,
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), id)),
        id,
        IndexingLevel.Deep,
        IncludeSubfolders: true,
        Enabled: enabled,
        Priority: 0,
        Exclusions: []);

    private static string HarnessPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenSorSe.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        return Path.Combine(
            directory!.FullName,
            "tests",
            "OmniSorSe.CompanionTestHarness",
            "bin",
            configuration,
            "net10.0",
            "OmniSorSe.CompanionTestHarness.dll");
    }

    private static string DotnetHostPath()
    {
        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var path = Path.GetFullPath(Path.Combine(
            RuntimeEnvironment.GetRuntimeDirectory(),
            "..",
            "..",
            "..",
            executable));
        Assert.True(File.Exists(path), $"The active test runtime host was not found at {path}.");
        return path;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    private sealed class FakeLocator(ExplorerCompanionDiscoveryResult result) : IExplorerCompanionLocator
    {
        public ExplorerCompanionDiscoveryResult Locate() => result;
    }

    private sealed class FakeBootstrap : IExplorerCompanionBootstrapTransport
    {
        private readonly Func<ExplorerSessionGrant, ExplorerCompanionBootstrapResult> _handler;

        public FakeBootstrap(Func<ExplorerSessionGrant, ExplorerCompanionBootstrapResult>? handler = null) =>
            _handler = handler ?? (_ => throw new InvalidOperationException("Bootstrap should not run."));

        public List<ExplorerSessionGrant> Grants { get; } = [];
        public List<string> HandoffEndpoints { get; } = [];
        public List<FakeProcess> Processes { get; } = [];
        public int LaunchCount => Grants.Count;

        public static FakeBootstrap Connected(FakeProcess process) => new(_ =>
            new ExplorerCompanionBootstrapResult(true, process, false));

        public Task<ExplorerCompanionBootstrapResult> LaunchAsync(
            ExplorerCompanionExecutable executable,
            ExplorerSessionGrant grant,
            string handoffEndpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Grants.Add(grant);
            HandoffEndpoints.Add(handoffEndpoint);
            var result = _handler(grant);
            if (result.Process is FakeProcess process)
            {
                Processes.Add(process);
            }

            return Task.FromResult(result);
        }
    }

    private sealed class CapturingBootstrapTransport(IExplorerCompanionBootstrapTransport inner)
        : IExplorerCompanionBootstrapTransport
    {
        public List<ExplorerSessionGrant> Grants { get; } = [];
        public List<string> HandoffEndpoints { get; } = [];

        public Task<ExplorerCompanionBootstrapResult> LaunchAsync(
            ExplorerCompanionExecutable executable,
            ExplorerSessionGrant grant,
            string handoffEndpoint,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            Grants.Add(grant);
            HandoffEndpoints.Add(handoffEndpoint);
            return inner.LaunchAsync(executable, grant, handoffEndpoint, timeout, cancellationToken);
        }
    }

    private sealed class FakeProcess : IExplorerCompanionProcess
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Id => 42;
        public Task Completion => _completion.Task;
        public void Exit() => _completion.TrySetResult();
        public void Dispose() => _completion.TrySetResult();
    }

    private sealed class ReadingProcessStarter : IExplorerCompanionProcessStarter
    {
        private readonly TaskCompletionSource<ExplorerSessionGrant> _received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeProcess Process { get; } = new();
        public Task<ExplorerSessionGrant> ReceivedGrant => _received.Task;

        public IExplorerCompanionProcess Start(ExplorerCompanionExecutable executable, string handoffEndpoint)
        {
            _ = ReadAsync(handoffEndpoint);
            return Process;
        }

        private async Task ReadAsync(string endpoint)
        {
            try
            {
                using var pipe = new System.IO.Pipes.NamedPipeClientStream(
                    ".",
                    endpoint,
                    System.IO.Pipes.PipeDirection.In,
                    System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(CancellationToken.None);
                var length = new byte[sizeof(int)];
                await pipe.ReadExactlyAsync(length);
                var bytes = new byte[System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(length)];
                await pipe.ReadExactlyAsync(bytes);
                _received.TrySetResult(JsonSerializer.Deserialize<ExplorerSessionGrant>(
                    bytes,
                    ExplorerCompanionBootstrapContract.CreateJsonOptions())!);
            }
            catch (Exception exception)
            {
                _received.TrySetException(exception);
            }
        }
    }

    private sealed class FakeHost : IExplorerProtocolHost, IExplorerSessionConnectionObserver
    {
        private int _sequence;

        public ExplorerProtocolHostState State { get; private set; } = ExplorerProtocolHostState.Unavailable;
        public int CreateCount => Scopes.Count;
        public List<ExplorerSessionScope> Scopes { get; } = [];
        public List<string> RevokedSessionIds { get; } = [];
        public ExplorerSessionConnectionOutcome? ConnectionOutcome { get; set; } =
            ExplorerSessionConnectionOutcome.Authenticated;

        public Task<ExplorerSessionGrant> CreateSessionAsync(
            ExplorerSessionScope scope,
            TimeSpan? lifetime = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Scopes.Add(scope);
            var sequence = Interlocked.Increment(ref _sequence);
            State = ExplorerProtocolHostState.Authorized;
            return Task.FromResult(new ExplorerSessionGrant(
                "named-pipe",
                $"pipe-{sequence}",
                $"session-{sequence}",
                $"token-{sequence}-{Guid.NewGuid():N}",
                DateTimeOffset.UtcNow.AddMinutes(15),
                ExplorerProtocolVersion.Major,
                ExplorerProtocolVersion.Minor));
        }

        public Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RevokedSessionIds.Add(sessionId);
            State = ExplorerProtocolHostState.Disconnected;
            return Task.CompletedTask;
        }

        public Task<ExplorerSessionConnectionOutcome?> WaitForConnectionOutcomeAsync(
            string sessionId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ConnectionOutcome);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDataSource(params IndexingSource[] sources) : IExplorerDataSource
    {
        public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IndexingSource>>(sources);

        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);

        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
            IReadOnlyList<string> fileIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>([]);

        public Task<SearchExecutionResult> SearchAsync(
            OpenSorSe.Application.Semantic.SearchRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SearchExecutionResult(
                SemanticState.Ready,
                "No results.",
                [],
                new SearchInterpretation(request.QueryText, request.QueryText, [], []),
                new SearchCoverage(0, 0, 0, 0, 0, 0)));

        public Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(
            string fileId,
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RelatedFile>>([]);
    }

    private sealed class FakeConfiguration(ApplicationSettings settings) : IConfigurationService
    {
        public ApplicationSettings Current { get; private set; } = settings;
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings value, CancellationToken cancellationToken)
        {
            Current = value;
            return Task.CompletedTask;
        }
    }
}
