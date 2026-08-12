using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using OmniSorSe.ExplorerProtocol;
using OpenSorSe.Application.ContentIntelligence;
using OpenSorSe.Application.Explorer;
using OpenSorSe.Application.Indexing;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.Relationships;
using OpenSorSe.Application.Semantic;
using OpenSorSe.Core.Configuration;
using OpenSorSe.Core.Diagnostics;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Application.Tests;

/// <summary>Protects the local, authorized, bounded, provider-neutral Explorer Protocol v1 boundary.</summary>
public sealed class ExplorerProtocolTests
{
    private const string SourceA = "source-a";
    private const string SourceB = "source-b";

    /// <summary>Verifies the public protocol version and operation surface contain no mutation verbs.</summary>
    [Fact]
    public void Contracts_AreVersionedAndReadOnly()
    {
        Assert.Equal(1, ExplorerProtocolVersion.Major);
        Assert.Equal(0, ExplorerProtocolVersion.Minor);
        Assert.DoesNotContain(
            Enum.GetNames<ExplorerOperation>(),
            name => new[] { "delete", "rename", "move", "create", "clear", "write", "execute" }
                .Any(verb => name.Contains(verb, StringComparison.OrdinalIgnoreCase)));
        Assert.All(
            typeof(ExplorerNode).Assembly.GetReferencedAssemblies(),
            reference => Assert.DoesNotContain("Sqlite", reference.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verifies strict protocol JSON rejects unknown fields and arbitrary type metadata.</summary>
    [Fact]
    public void Serialization_IsStrictAndDoesNotEnableRuntimeTypes()
    {
        var options = ExplorerProtocolJson.CreateOptions();
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExplorerChildrenRequest>(
            "{\"parentNodeId\":\"n1_value\",\"unexpected\":true}",
            options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ExplorerChildrenRequest>(
            "{\"$type\":\"System.IO.FileInfo\",\"parentNodeId\":\"n1_value\"}",
            options));
    }

    /// <summary>Verifies session tokens are high entropy and only a matching value authorizes.</summary>
    [Fact]
    public void Sessions_RequireHighEntropyMatchingToken()
    {
        using var manager = new ExplorerSessionManager();
        var (session, token) = manager.Create(Scope(SourceA), TimeSpan.FromMinutes(1));

        Assert.True(token.Length >= 43);
        Assert.Equal(ExplorerSessionValidation.Valid, manager.Validate(session.SessionId, token).Validation);
        Assert.Equal(ExplorerSessionValidation.Unauthorized, manager.Validate(session.SessionId, token + "x").Validation);
        Assert.DoesNotContain(
            typeof(ExplorerSessionContext).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType == typeof(string) && field.Name.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verifies absolute expiry and immediate revocation both deny later use.</summary>
    [Fact]
    public void Sessions_ExpireAndRevoke()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-12T10:00:00Z"));
        using var manager = new ExplorerSessionManager(time);
        var (expiredSession, expiredToken) = manager.Create(Scope(SourceA), TimeSpan.FromSeconds(15));
        time.Advance(TimeSpan.FromSeconds(16));
        Assert.Equal(ExplorerSessionValidation.Expired, manager.Validate(expiredSession.SessionId, expiredToken).Validation);

        var (revokedSession, revokedToken) = manager.Create(Scope(SourceA), TimeSpan.FromMinutes(1));
        manager.Revoke(revokedSession.SessionId);
        Assert.Equal(ExplorerSessionValidation.Unauthorized, manager.Validate(revokedSession.SessionId, revokedToken).Validation);
    }

    /// <summary>Verifies opaque node identifiers cannot be replayed across sessions.</summary>
    [Fact]
    public void OpaqueNodeIds_AreSessionBound()
    {
        using var manager = new ExplorerSessionManager();
        var first = manager.Create(Scope(SourceA), TimeSpan.FromMinutes(1)).Session;
        var second = manager.Create(Scope(SourceA), TimeSpan.FromMinutes(1)).Session;
        var identity = new ExplorerNodeIdentity(ExplorerNodeKind.Source, SourceA);

        var firstId = first.RegisterNode(identity);
        var secondId = second.RegisterNode(identity);

        Assert.NotEqual(firstId, secondId);
        Assert.False(second.TryResolveNode(firstId, out _));
        Assert.DoesNotContain(SourceA, firstId, StringComparison.Ordinal);
    }

    /// <summary>Verifies roots are limited to explicitly authorized indexed sources.</summary>
    [Fact]
    public async Task Roots_ReturnOnlyAuthorizedIndexedScope()
    {
        var fixture = CreateFixture();
        var page = await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None);

        var root = Assert.Single(page.Nodes);
        Assert.Equal("Projects", root.Name);
        Assert.Equal(ExplorerNodeKind.Source, root.Kind);
        Assert.Null(root.AuthorizedPath);
        Assert.DoesNotContain(page.Nodes, node => node.Name == "Private");
    }

    /// <summary>Verifies raw paths require an explicit path-projection grant in addition to source scope.</summary>
    [Fact]
    public async Task Paths_AreOmittedUnlessExplicitlyAuthorized()
    {
        var source = new FakeExplorerDataSource();
        source.Sources =
        [
            new IndexingSource(SourceA, "C:\\fixture\\projects", "Projects", IndexingLevel.Deep, true, true, 0, []),
        ];
        var manager = new ExplorerSessionManager();
        var session = manager.Create(new ExplorerSessionScope([SourceA], IncludeAuthorizedPaths: true), null).Session;
        var reads = new ExplorerReadService(source, PlatformServices.CreatePathSemantics(HostPlatformKind.Windows));

        var root = Assert.Single((await reads.GetAccessibleRootsAsync(session, CancellationToken.None)).Nodes);

        Assert.Equal("C:\\fixture\\projects", root.AuthorizedPath);
        manager.Dispose();
    }

    /// <summary>Verifies structure navigation is stable, bounded, and groups indexed descendants as folders.</summary>
    [Fact]
    public async Task Children_AreStableFoldersThenFiles()
    {
        var fixture = CreateFixture();
        var root = Assert.Single((await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None)).Nodes);
        var page = await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(root.Id),
            CancellationToken.None);

        Assert.Equal(["docs", "root.txt"], page.Nodes.Select(node => node.Name).ToArray());
        Assert.Equal(ExplorerNodeKind.Folder, page.Nodes[0].Kind);
        Assert.Equal(ExplorerNodeKind.File, page.Nodes[1].Kind);
        Assert.All(page.Nodes, node => Assert.Equal(root.Id, node.ParentId));
    }

    /// <summary>Verifies detail projection retains the structural parent rather than creating a self-cycle.</summary>
    [Fact]
    public async Task FolderDetails_RetainOpaqueStructuralParent()
    {
        var fixture = CreateFixture();
        var root = Assert.Single((await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None)).Nodes);
        var children = await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(root.Id),
            CancellationToken.None);
        var folder = Assert.Single(children.Nodes, node => node.Kind == ExplorerNodeKind.Folder);

        var details = await fixture.Reads.GetNodeDetailsAsync(
            fixture.Session,
            new ExplorerNodeDetailsRequest(folder.Id),
            CancellationToken.None);

        Assert.Equal(root.Id, details.Node.ParentId);
        Assert.NotEqual(details.Node.Id, details.Node.ParentId);
    }

    /// <summary>Verifies child paging uses an opaque bounded continuation contract.</summary>
    [Fact]
    public async Task Children_SupportBoundedContinuation()
    {
        var fixture = CreateFixture();
        var root = Assert.Single((await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None)).Nodes);
        var first = await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(root.Id, 1),
            CancellationToken.None);
        var second = await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(root.Id, 1, first.ContinuationToken),
            CancellationToken.None);

        Assert.True(first.IsTruncated);
        Assert.NotNull(first.ContinuationToken);
        Assert.Single(second.Nodes);
        Assert.NotEqual(first.Nodes[0].Id, second.Nodes[0].Id);
    }

    /// <summary>Verifies malformed relative paths never escape into structural results.</summary>
    [Fact]
    public async Task Children_RejectPersistedTraversalSegments()
    {
        var fixture = CreateFixture();
        fixture.Source.Documents.Add(Document("bad", SourceA, "..\\secret.txt", "secret.txt"));
        var root = Assert.Single((await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None)).Nodes);

        var page = await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(root.Id),
            CancellationToken.None);

        Assert.DoesNotContain(page.Nodes, node => node.Name == "secret.txt");
    }

    /// <summary>Verifies Search preserves service order, rejects ungrounded/other-scope hits, and never enables AI.</summary>
    [Fact]
    public async Task Search_IsGroundedScopedAndDeterministicFirst()
    {
        var fixture = CreateFixture();
        fixture.Source.SearchHits =
        [
            Hit("file-root", "root.txt", 500, "Exact filename"),
            Hit("file-private", "private.txt", 400, "Private metadata"),
            Hit(null, "legacy.txt", 300, "Ungrounded legacy hit"),
            Hit("file-nested", "network.md", 200, "Topic match: Raspberry Pi"),
        ];

        var result = await fixture.Reads.SearchAsync(
            fixture.Session,
            new ExplorerSearchRequest("Raspberry Pi"),
            CancellationToken.None);

        Assert.Equal(["root.txt", "network.md"], result.Results.Select(item => item.Node.Name).ToArray());
        Assert.Equal([1, 2], result.Results.Select(item => item.Rank).ToArray());
        Assert.False(result.UsedAiAssistance);
        Assert.False(fixture.Source.LastSearchRequest!.UseAiAssistance);
        Assert.Equal("Unified local Search completed over the authorized indexed scope.", result.Coverage);
        Assert.DoesNotContain("Private", result.Coverage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies oversized and empty Search requests fail before provider work.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_RejectsEmptyQuery(string query)
    {
        var fixture = CreateFixture();
        var exception = await Assert.ThrowsAsync<ExplorerProtocolException>(() => fixture.Reads.SearchAsync(
            fixture.Session,
            new ExplorerSearchRequest(query),
            CancellationToken.None));
        Assert.Equal(ExplorerErrorCode.MalformedRequest, exception.Code);
    }

    /// <summary>Verifies Related Files filters an otherwise valid relationship outside session scope.</summary>
    [Fact]
    public async Task RelatedFiles_FilterOutOtherAuthorizedScopes()
    {
        var fixture = CreateFixture();
        var roots = await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None);
        var rootChildren = await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(roots.Nodes[0].Id),
            CancellationToken.None);
        var file = Assert.Single(rootChildren.Nodes, node => node.Kind == ExplorerNodeKind.File);
        fixture.Source.Related =
        [
            Related("file-root", "file-nested", "Shared Raspberry Pi topic"),
            Related("file-root", "file-private", "Should remain private"),
        ];

        var result = await fixture.Reads.GetRelatedAsync(
            fixture.Session,
            new ExplorerRelatedRequest(file.Id),
            CancellationToken.None);

        var node = Assert.Single(result.Nodes);
        Assert.Equal("network.md", node.Name);
        var edge = Assert.Single(result.Edges);
        Assert.Equal(ExplorerEdgeKind.Topic, edge.Kind);
        Assert.DoesNotContain("private", edge.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.IsTruncated);

        var details = await fixture.Reads.GetNodeDetailsAsync(
            fixture.Session,
            new ExplorerNodeDetailsRequest(file.Id),
            CancellationToken.None);
        Assert.DoesNotContain(details.RelationshipSummaries, value =>
            value.Contains("private", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verifies details expose bounded facts but never full OCR, transcript, or precise GPS.</summary>
    [Fact]
    public async Task Details_DoNotLeakFullContentOrGps()
    {
        var fixture = CreateFixture(includeSensitiveMedia: true);
        var roots = await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None);
        var docs = await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(roots.Nodes[0].Id),
            CancellationToken.None);
        var file = Assert.Single(docs.Nodes, node => node.Kind == ExplorerNodeKind.File);

        var details = await fixture.Reads.GetNodeDetailsAsync(
            fixture.Session,
            new ExplorerNodeDetailsRequest(file.Id),
            CancellationToken.None);
        var json = JsonSerializer.Serialize(details, ExplorerProtocolJson.CreateOptions());

        Assert.True(details.Media!.HasOcrEvidence);
        Assert.True(details.Media.HasTranscriptEvidence);
        Assert.DoesNotContain("private spoken sentence", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private OCR sentence", json, StringComparison.Ordinal);
        Assert.DoesNotContain("48.123", json, StringComparison.Ordinal);
        Assert.DoesNotContain("11.456", json, StringComparison.Ordinal);
    }

    /// <summary>Verifies a removed indexed node fails safely after its opaque identity was issued.</summary>
    [Fact]
    public async Task RemovedNode_NoLongerReturnsStaleDetails()
    {
        var fixture = CreateFixture();
        var root = Assert.Single((await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None)).Nodes);
        var children = await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(root.Id),
            CancellationToken.None);
        var file = Assert.Single(children.Nodes, node => node.Kind == ExplorerNodeKind.File);
        fixture.Source.Documents.RemoveAll(document => document.FileId == "file-root");

        var exception = await Assert.ThrowsAsync<ExplorerProtocolException>(() => fixture.Reads.GetNodeDetailsAsync(
            fixture.Session,
            new ExplorerNodeDetailsRequest(file.Id),
            CancellationToken.None));

        Assert.Equal(ExplorerErrorCode.NodeNotFound, exception.Code);
        Assert.DoesNotContain("root.txt", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies query and continuation bounds reject hostile values before unbounded provider work.</summary>
    [Fact]
    public async Task RequestValues_AreBoundedAndMalformedContinuationIsRejected()
    {
        var fixture = CreateFixture();
        var query = new string('q', ExplorerProtocolDefaults.CreateLimits().MaximumQueryCharacters + 1);
        var search = await Assert.ThrowsAsync<ExplorerProtocolException>(() => fixture.Reads.SearchAsync(
            fixture.Session,
            new ExplorerSearchRequest(query),
            CancellationToken.None));
        Assert.Equal(ExplorerErrorCode.RequestTooLarge, search.Code);

        var root = Assert.Single((await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None)).Nodes);
        var continuation = await Assert.ThrowsAsync<ExplorerProtocolException>(() => fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(root.Id, ContinuationToken: "not-a-valid-continuation"),
            CancellationToken.None));
        Assert.Equal(ExplorerErrorCode.MalformedRequest, continuation.Code);
    }

    /// <summary>Verifies neighborhoods obey hard node/edge limits and label structural containment.</summary>
    [Fact]
    public async Task Neighborhood_IsBoundedAndStructural()
    {
        var fixture = CreateFixture();
        var root = Assert.Single((await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None)).Nodes);

        var neighborhood = await fixture.Reads.GetNeighborhoodAsync(
            fixture.Session,
            new ExplorerNeighborhoodRequest(root.Id, Depth: 2, MaximumNodes: 2, MaximumEdges: 1, IncludeContext: false),
            CancellationToken.None);

        Assert.True(neighborhood.Nodes.Count <= 2);
        var edge = Assert.Single(neighborhood.Edges);
        Assert.Equal(ExplorerEdgeKind.Contains, edge.Kind);
        Assert.Equal(ExplorerEvidenceClass.Structural, edge.EvidenceClass);
    }

    /// <summary>Verifies the smallest node budget returns a truthful bounded response instead of a zero-limit failure.</summary>
    [Fact]
    public async Task Neighborhood_MinimumNodeBudgetReturnsFocusAndReportsTruncation()
    {
        var fixture = CreateFixture();
        var root = Assert.Single((await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None)).Nodes);

        var structural = await fixture.Reads.GetNeighborhoodAsync(
            fixture.Session,
            new ExplorerNeighborhoodRequest(root.Id, Depth: 2, MaximumNodes: 1, MaximumEdges: 1, IncludeContext: false),
            CancellationToken.None);

        Assert.Equal(root.Id, Assert.Single(structural.Nodes).Id);
        Assert.Empty(structural.Edges);
        Assert.True(structural.IsTruncated);

        var file = Assert.Single((await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(root.Id),
            CancellationToken.None)).Nodes, node => node.Kind == ExplorerNodeKind.File);
        var contextual = await fixture.Reads.GetNeighborhoodAsync(
            fixture.Session,
            new ExplorerNeighborhoodRequest(file.Id, MaximumNodes: 1, MaximumEdges: 1, IncludeContext: true),
            CancellationToken.None);

        Assert.Equal(file.Id, Assert.Single(contextual.Nodes).Id);
        Assert.Empty(contextual.Edges);
        Assert.True(contextual.IsTruncated);
    }

    /// <summary>Verifies unknown node identifiers do not leak whether a path or record exists.</summary>
    [Fact]
    public async Task UnknownNode_ReturnsOutOfScopeWithoutExistenceLeak()
    {
        var fixture = CreateFixture();
        var exception = await Assert.ThrowsAsync<ExplorerProtocolException>(() => fixture.Reads.GetNodeDetailsAsync(
            fixture.Session,
            new ExplorerNodeDetailsRequest("n1_not-issued"),
            CancellationToken.None));

        Assert.Equal(ExplorerErrorCode.OutOfScope, exception.Code);
        Assert.DoesNotContain("file", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies capability negotiation still requires authorization and rejects incompatible majors.</summary>
    [Fact]
    public async Task Dispatcher_RequiresAuthorizationBeforeNegotiation()
    {
        var fixture = CreateFixture();
        using var dispatcher = new ExplorerProtocolDispatcher(fixture.Manager, fixture.Reads);
        var empty = JsonSerializer.SerializeToElement(new { }, ExplorerProtocolJson.CreateOptions());
        var unauthorized = await dispatcher.DispatchAsync(
            new ExplorerRequestEnvelope(99, "request-1", fixture.Session.SessionId, "wrong", ExplorerOperation.GetProtocolInfo, empty),
            CancellationToken.None);
        var incompatible = await dispatcher.DispatchAsync(
            new ExplorerRequestEnvelope(99, "request-2", fixture.Session.SessionId, fixture.Token, ExplorerOperation.GetProtocolInfo, empty),
            CancellationToken.None);

        Assert.Equal(ExplorerErrorCode.Unauthorized, unauthorized.Error!.Code);
        Assert.Equal(ExplorerErrorCode.UnsupportedProtocol, incompatible.Error!.Code);
    }

    /// <summary>Verifies malformed operation payloads become stable protocol errors without internal detail.</summary>
    [Fact]
    public async Task Dispatcher_IsolatesMalformedPayload()
    {
        var fixture = CreateFixture();
        using var dispatcher = new ExplorerProtocolDispatcher(fixture.Manager, fixture.Reads);
        var malformed = JsonSerializer.SerializeToElement(new { wrong = true }, ExplorerProtocolJson.CreateOptions());

        var response = await dispatcher.DispatchAsync(
            new ExplorerRequestEnvelope(
                ExplorerProtocolVersion.Major,
                "request-3",
                fixture.Session.SessionId,
                fixture.Token,
                ExplorerOperation.GetChildren,
                malformed),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(ExplorerErrorCode.MalformedRequest, response.Error!.Code);
        Assert.DoesNotContain("exception", response.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlite", response.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies diagnostics retain only bounded operational facts, never authorization or content.</summary>
    [Fact]
    public async Task Diagnostics_DoNotRetainSecretsQueriesPathsOrContent()
    {
        var fixture = CreateFixture();
        fixture.Source.SearchHits = [Hit("file-root", "root.txt", 10, "Private explanation")];
        var diagnostics = new InMemoryDiagnosticsCollector();
        diagnostics.Configure(new DiagnosticsSettings
        {
            EnableDiagnostics = true,
            SearchAndIndexingDiagnostics = true,
            ShowUnredactedDiagnosticContent = true,
        });
        using var dispatcher = new ExplorerProtocolDispatcher(fixture.Manager, fixture.Reads, diagnostics);
        const string privateQuery = "private-query-never-retain";
        var payload = JsonSerializer.SerializeToElement(
            new ExplorerSearchRequest(privateQuery),
            ExplorerProtocolJson.CreateOptions());

        var response = await dispatcher.DispatchAsync(
            new ExplorerRequestEnvelope(
                ExplorerProtocolVersion.Major,
                "diagnostic-request",
                fixture.Session.SessionId,
                fixture.Token,
                ExplorerOperation.Search,
                payload),
            CancellationToken.None);
        var retained = JsonSerializer.Serialize(diagnostics.GetRecent());

        Assert.True(response.Success);
        Assert.DoesNotContain(fixture.Token, retained, StringComparison.Ordinal);
        Assert.DoesNotContain(privateQuery, retained, StringComparison.Ordinal);
        Assert.DoesNotContain("root.txt", retained, StringComparison.Ordinal);
        Assert.DoesNotContain("Private explanation", retained, StringComparison.Ordinal);
        Assert.Contains("Result count", retained, StringComparison.Ordinal);
    }

    /// <summary>Verifies request admission returns a stable busy result under animation-style saturation.</summary>
    [Fact]
    public async Task Dispatcher_BoundsConcurrentAndQueuedRequests()
    {
        var fixture = CreateFixture();
        fixture.Source.SearchDelay = TimeSpan.FromMilliseconds(400);
        using var dispatcher = new ExplorerProtocolDispatcher(fixture.Manager, fixture.Reads);
        var payload = JsonSerializer.SerializeToElement(new ExplorerSearchRequest("network"), ExplorerProtocolJson.CreateOptions());
        var requests = Enumerable.Range(0, ExplorerProtocolDefaults.MaximumQueuedRequests + 1)
            .Select(index => dispatcher.DispatchAsync(
                new ExplorerRequestEnvelope(
                    ExplorerProtocolVersion.Major,
                    $"queued-{index}",
                    fixture.Session.SessionId,
                    fixture.Token,
                    ExplorerOperation.Search,
                    payload),
                CancellationToken.None))
            .ToArray();

        var responses = await Task.WhenAll(requests);

        Assert.Contains(responses, response => response.Error?.Code == ExplorerErrorCode.TemporarilyUnavailable);
        Assert.All(responses, response => Assert.True(
            response.Success || response.Error?.Code == ExplorerErrorCode.TemporarilyUnavailable));
    }

    /// <summary>Verifies cancellation reaches a long-running provider and returns a stable cancellation response.</summary>
    [Fact]
    public async Task Dispatcher_CancelsProviderWork()
    {
        var fixture = CreateFixture();
        fixture.Source.SearchDelay = TimeSpan.FromSeconds(5);
        using var dispatcher = new ExplorerProtocolDispatcher(fixture.Manager, fixture.Reads);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var payload = JsonSerializer.SerializeToElement(new ExplorerSearchRequest("network"), ExplorerProtocolJson.CreateOptions());

        var response = await dispatcher.DispatchAsync(
            new ExplorerRequestEnvelope(
                ExplorerProtocolVersion.Major,
                "request-4",
                fixture.Session.SessionId,
                fixture.Token,
                ExplorerOperation.Search,
                payload),
            cancellation.Token);

        Assert.Equal(ExplorerErrorCode.Cancelled, response.Error!.Code);
        Assert.True(fixture.Source.SearchWasCancelled);
    }

    /// <summary>Runs one actual current-user-only named-pipe round trip on the Windows host.</summary>
    [Fact]
    public async Task NamedPipeHost_CompletesAuthorizedNativeRoundTrip()
    {
        var source = CreateDataSource();
        await using var host = new NamedPipeExplorerProtocolHost(
            source,
            PlatformServices.CreatePathSemantics(HostPlatformKind.Windows));
        Assert.Equal(ExplorerProtocolHostState.Unavailable, host.State);
        var grant = await host.CreateSessionAsync(Scope(SourceA), TimeSpan.FromMinutes(1));
        var response = await SendAsync(
            grant,
            ExplorerOperation.GetProtocolInfo,
            new { },
            grant.AuthorizationToken);

        Assert.True(response.Success);
        var info = response.Payload!.Value.Deserialize<ExplorerProtocolInfo>(ExplorerProtocolJson.CreateOptions());
        Assert.Equal("OmniSorSe", info!.ApplicationName);
        Assert.True(info.IsReadOnly);
        Assert.Equal("named-pipe", grant.Transport);
    }

    /// <summary>Verifies the native transport rejects an invalid secret without returning scoped data.</summary>
    [Fact]
    public async Task NamedPipeHost_RejectsInvalidSecret()
    {
        await using var host = new NamedPipeExplorerProtocolHost(
            CreateDataSource(),
            PlatformServices.CreatePathSemantics(HostPlatformKind.Windows));
        var grant = await host.CreateSessionAsync(Scope(SourceA), TimeSpan.FromMinutes(1));

        var response = await SendAsync(grant, ExplorerOperation.GetAccessibleRoots, new { }, "invalid-token");

        Assert.False(response.Success);
        Assert.Equal(ExplorerErrorCode.Unauthorized, response.Error!.Code);
        Assert.Null(response.Payload);
    }

    /// <summary>Verifies an external pipe peer disappearing cancels active provider work promptly.</summary>
    [Fact]
    public async Task NamedPipeHost_ClientDisconnectCancelsActiveProviderWorkPromptly()
    {
        var source = CreateDataSource();
        source.SearchDelay = TimeSpan.FromSeconds(30);
        await using var host = new NamedPipeExplorerProtocolHost(
            source,
            PlatformServices.CreatePathSemantics(HostPlatformKind.Windows));
        var grant = await host.CreateSessionAsync(Scope(SourceA), TimeSpan.FromMinutes(1));
        var options = ExplorerProtocolJson.CreateOptions();
        var request = new ExplorerRequestEnvelope(
            grant.ProtocolMajor,
            Guid.NewGuid().ToString("N"),
            grant.SessionId,
            grant.AuthorizationToken,
            ExplorerOperation.Search,
            JsonSerializer.SerializeToElement(new ExplorerSearchRequest("network"), options));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, options);
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);

        var client = new NamedPipeClientStream(
            ".",
            grant.Endpoint,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(timeout.Token);
        await client.WriteAsync(length, timeout.Token);
        await client.WriteAsync(bytes, timeout.Token);
        await client.FlushAsync(timeout.Token);
        while (source.LastSearchRequest is null)
        {
            await Task.Delay(10, timeout.Token);
        }

        var stopwatch = Stopwatch.StartNew();
        await client.DisposeAsync();
        while (!source.SearchWasCancelled)
        {
            await Task.Delay(10, timeout.Token);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    /// <summary>Verifies native listener saturation remains bounded and cannot fault host disposal.</summary>
    [Fact]
    public async Task NamedPipeHost_ListenerSaturationDoesNotFaultHostLifecycle()
    {
        var source = CreateDataSource();
        source.SearchDelay = TimeSpan.FromMilliseconds(300);
        await using var host = new NamedPipeExplorerProtocolHost(
            source,
            PlatformServices.CreatePathSemantics(HostPlatformKind.Windows));
        var grant = await host.CreateSessionAsync(Scope(SourceA), TimeSpan.FromMinutes(1));

        var responses = await Task.WhenAll(Enumerable.Range(0, 24).Select(index => SendAsync(
            grant,
            ExplorerOperation.Search,
            new ExplorerSearchRequest("network " + index, 2),
            grant.AuthorizationToken)));

        Assert.All(responses, response => Assert.True(
            response.Success || response.Error?.Code == ExplorerErrorCode.TemporarilyUnavailable));
    }

    /// <summary>Verifies Explorer absence is a cheap, non-error capability state.</summary>
    [Fact]
    public void CompanionPresence_IsUnavailableWithoutFilesystemProbe()
    {
        IExplorerCompanionPresence presence = new UnavailableExplorerCompanionPresence();
        Assert.False(presence.IsAvailable);
        Assert.Contains("future companion", presence.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Protects representative structural projection from catastrophic unbounded work.</summary>
    [Fact]
    public async Task StructureProjection_RemainsBoundedForLargeSyntheticIndex()
    {
        var fixture = CreateFixture();
        fixture.Source.Documents.Clear();
        for (var index = 0; index < 5_000; index++)
        {
            fixture.Source.Documents.Add(Document($"f-{index}", SourceA, $"folder-{index % 250}/file-{index}.txt", $"file-{index}.txt"));
        }

        var stopwatch = Stopwatch.StartNew();
        var root = Assert.Single((await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None)).Nodes);
        var children = await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(root.Id, 100),
            CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(250, children.TotalAvailable);
        Assert.Equal(100, children.Nodes.Count);
        Assert.True(children.IsTruncated);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
    }

    /// <summary>Verifies the hard document-examination cap is exposed as incomplete rather than silently complete.</summary>
    [Fact]
    public async Task StructureProjection_DocumentCapReportsTruncationWithoutInventingContinuation()
    {
        var fixture = CreateFixture();
        fixture.Source.Documents.Clear();
        fixture.Source.Documents.Add(Document("authorized", SourceA, "visible.txt", "visible.txt"));
        for (var index = 1; index < ExplorerProtocolDefaults.MaximumDocumentsExamined; index++)
        {
            fixture.Source.Documents.Add(Document($"other-{index}", SourceB, $"other-{index}.txt", $"other-{index}.txt"));
        }

        var root = Assert.Single((await fixture.Reads.GetAccessibleRootsAsync(fixture.Session, CancellationToken.None)).Nodes);
        var children = await fixture.Reads.GetChildrenAsync(
            fixture.Session,
            new ExplorerChildrenRequest(root.Id),
            CancellationToken.None);

        Assert.Equal("visible.txt", Assert.Single(children.Nodes).Name);
        Assert.True(children.IsTruncated);
        Assert.Null(children.ContinuationToken);
    }

    private static ExplorerFixture CreateFixture(bool includeSensitiveMedia = false)
    {
        var source = CreateDataSource(includeSensitiveMedia);
        var manager = new ExplorerSessionManager();
        var created = manager.Create(Scope(SourceA), TimeSpan.FromMinutes(1));
        var reads = new ExplorerReadService(
            source,
            PlatformServices.CreatePathSemantics(HostPlatformKind.Windows));
        return new ExplorerFixture(source, manager, created.Session, created.Token, reads);
    }

    private static FakeExplorerDataSource CreateDataSource(bool includeSensitiveMedia = false)
    {
        var root = Document("file-root", SourceA, "root.txt", "root.txt");
        if (includeSensitiveMedia)
        {
            root = root with
            {
                MediaEvidence = new IndexedMediaEvidence
                {
                    Kind = MediaKind.Video,
                    Metadata = new MediaMetadata
                    {
                        Kind = MediaKind.Video,
                        Container = "mp4",
                        Width = 1920,
                        Height = 1080,
                        Latitude = 48.123,
                        Longitude = 11.456,
                    },
                    OcrText = "private OCR sentence",
                    Transcript = "private spoken sentence",
                    ProcessingFingerprint = "fixture",
                },
            };
        }

        return new FakeExplorerDataSource
        {
            Sources =
            [
                new IndexingSource(SourceA, "C:\\fixtures\\projects", "Projects", IndexingLevel.Deep, true, true, 0, []),
                new IndexingSource(SourceB, "C:\\fixtures\\private", "Private", IndexingLevel.Deep, true, true, 0, []),
            ],
            Documents =
            [
                root,
                Document("file-nested", SourceA, "docs\\network.md", "network.md"),
                Document("file-private", SourceB, "private.txt", "private.txt"),
            ],
        };
    }

    private static ProgressiveSearchDocument Document(
        string id,
        string sourceId,
        string relativePath,
        string fileName) =>
        new()
        {
            FileId = id,
            FullPath = sourceId == SourceA
                ? "C:\\fixtures\\projects\\" + relativePath
                : "C:\\fixtures\\private\\" + relativePath,
            FileName = fileName,
            RelativePath = relativePath,
            FolderName = Path.GetDirectoryName(relativePath) ?? string.Empty,
            Extension = Path.GetExtension(fileName),
            FileType = "Document",
            SourceId = sourceId,
            SourceName = sourceId == SourceA ? "Projects" : "Private",
            Length = 100,
            IsFullyIndexed = true,
            ContentIntelligence = new IndexedContentIntelligence
            {
                Provider = "deterministic-content-intelligence",
                ProviderVersion = "1",
                ProcessingFingerprint = "fixture",
                Topics =
                [
                    new ContentConcept
                    {
                        Kind = ContentConceptKind.Topic,
                        DisplayName = "Raspberry Pi",
                        NormalizedValue = "raspberry pi",
                        Confidence = ContentIntelligenceConfidence.Strong,
                        Provider = "deterministic-content-intelligence",
                        ProviderVersion = "1",
                        Origin = ContentIntelligenceOrigin.Deterministic,
                    },
                ],
                Summary = new ContentSummaryEvidence
                {
                    Text = "Notes about Raspberry Pi monitoring.",
                    Provider = "deterministic-content-intelligence",
                    ProviderVersion = "1",
                    Origin = ContentIntelligenceOrigin.Deterministic,
                },
            },
        };

    private static SemanticSearchHit Hit(string? fileId, string fileName, double score, string explanation) =>
        new("C:\\fixture\\" + fileName, fileName, score, explanation, [], false, false, false, fileId);

    private static RelatedFile Related(string firstFileId, string relatedFileId, string explanation) =>
        new()
        {
            FileId = relatedFileId,
            FileName = relatedFileId + ".txt",
            FullPath = "C:\\fixture\\" + relatedFileId,
            SourceName = "Fixture",
            Relationship = new FileRelationship
            {
                Id = "relationship-" + relatedFileId,
                FirstFileId = firstFileId,
                SecondFileId = relatedFileId,
                Type = RelationshipType.SameTopic,
                Confidence = RelationshipConfidence.High,
                Evidence = [new RelationshipEvidence(RelationshipEvidenceKind.ContentTopic, "raspberry-pi", explanation)],
                Algorithm = "fixture",
                AlgorithmVersion = "1",
                CreatedAtUtc = DateTimeOffset.Parse("2026-08-12T10:00:00Z"),
                LastValidatedAtUtc = DateTimeOffset.Parse("2026-08-12T10:00:00Z"),
            },
        };

    private static ExplorerSessionScope Scope(params string[] sources) => new(sources);

    private static async Task<ExplorerResponseEnvelope> SendAsync(
        ExplorerSessionGrant grant,
        ExplorerOperation operation,
        object payload,
        string token)
    {
        using var client = new NamedPipeClientStream(
            ".",
            grant.Endpoint,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(timeout.Token);
        var options = ExplorerProtocolJson.CreateOptions();
        var request = new ExplorerRequestEnvelope(
            grant.ProtocolMajor,
            Guid.NewGuid().ToString("N"),
            grant.SessionId,
            token,
            operation,
            JsonSerializer.SerializeToElement(payload, payload.GetType(), options));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, options);
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await client.WriteAsync(length, timeout.Token);
        await client.WriteAsync(bytes, timeout.Token);
        await client.FlushAsync(timeout.Token);

        await client.ReadExactlyAsync(length, timeout.Token);
        var responseLength = BinaryPrimitives.ReadInt32LittleEndian(length);
        var response = new byte[responseLength];
        await client.ReadExactlyAsync(response, timeout.Token);
        return JsonSerializer.Deserialize<ExplorerResponseEnvelope>(response, options)!;
    }

    private sealed record ExplorerFixture(
        FakeExplorerDataSource Source,
        ExplorerSessionManager Manager,
        ExplorerSessionContext Session,
        string Token,
        ExplorerReadService Reads) : IDisposable
    {
        public void Dispose() => Manager.Dispose();
    }

    private sealed class FakeExplorerDataSource : IExplorerDataSource
    {
        public IReadOnlyList<IndexingSource> Sources { get; set; } = [];
        public List<ProgressiveSearchDocument> Documents { get; set; } = [];
        public IReadOnlyList<SemanticSearchHit> SearchHits { get; set; } = [];
        public IReadOnlyList<RelatedFile> Related { get; set; } = [];
        public OpenSorSe.Application.Semantic.SearchRequest? LastSearchRequest { get; private set; }
        public TimeSpan SearchDelay { get; set; }
        public bool SearchWasCancelled { get; private set; }

        public Task<IReadOnlyList<IndexingSource>> GetSourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Sources);

        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>(Documents.Take(maximumCount).ToArray());

        public Task<IReadOnlyList<ProgressiveSearchDocument>> GetDocumentsByIdsAsync(
            IReadOnlyList<string> fileIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProgressiveSearchDocument>>(Documents
                .Where(document => fileIds.Contains(document.FileId, StringComparer.Ordinal))
                .ToArray());

        public async Task<SearchExecutionResult> SearchAsync(
            OpenSorSe.Application.Semantic.SearchRequest request,
            CancellationToken cancellationToken)
        {
            LastSearchRequest = request;
            try
            {
                if (SearchDelay > TimeSpan.Zero)
                {
                    await Task.Delay(SearchDelay, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                SearchWasCancelled = true;
                throw;
            }

            return new SearchExecutionResult(
                SemanticState.Ready,
                "Complete local coverage",
                SearchHits,
                new SearchInterpretation(request.QueryText, request.QueryText, [], []),
                new SearchCoverage(Documents.Count, Documents.Count, Documents.Count, 0, 0, Documents.Count));
        }

        public Task<IReadOnlyList<RelatedFile>> GetRelatedFilesAsync(
            string fileId,
            int maximumCount,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RelatedFile>>(Related.Take(maximumCount).ToArray());
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
