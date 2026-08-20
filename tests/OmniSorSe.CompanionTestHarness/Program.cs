using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using OmniSorSe.ExplorerProtocol;
using OpenSorSe.Application.Explorer;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    var handoffEndpoint = Value(args, ExplorerCompanionBootstrapContract.HandoffArgument);
    if (string.IsNullOrWhiteSpace(handoffEndpoint))
    {
        return 2;
    }

    try
    {
        using var handoff = new NamedPipeClientStream(
            ".",
            handoffEndpoint,
            PipeDirection.In,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            TokenImpersonationLevel.Identification,
            HandleInheritability.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await handoff.ConnectAsync(timeout.Token);
        var grant = await ReadBoundedFrameAsync<ExplorerSessionGrant>(
            handoff,
            ExplorerCompanionBootstrapContract.CreateJsonOptions(),
            ExplorerCompanionBootstrapContract.MaximumFrameBytes,
            timeout.Token);
        return await NegotiateAsync(grant, timeout.Token) ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}

static async Task<bool> NegotiateAsync(
    ExplorerSessionGrant grant,
    CancellationToken cancellationToken)
{
    if (!string.Equals(grant.Transport, "named-pipe", StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(grant.Endpoint) ||
        string.IsNullOrWhiteSpace(grant.SessionId) ||
        string.IsNullOrWhiteSpace(grant.AuthorizationToken) ||
        DateTimeOffset.UtcNow >= grant.ExpiresAtUtc ||
        grant.ProtocolMajor != ExplorerProtocolVersion.Major)
    {
        return false;
    }

    using var pipe = new NamedPipeClientStream(
        ".",
        grant.Endpoint,
        PipeDirection.InOut,
        PipeOptions.Asynchronous,
        TokenImpersonationLevel.Identification,
        HandleInheritability.None);
    await pipe.ConnectAsync(cancellationToken);
    var protocolJson = ExplorerProtocolJson.CreateOptions();
    var request = new ExplorerRequestEnvelope(
        ExplorerProtocolVersion.Major,
        "companion-bootstrap",
        grant.SessionId,
        grant.AuthorizationToken,
        ExplorerOperation.GetProtocolInfo,
        JsonSerializer.SerializeToElement(new { }, protocolJson));
    await WriteBoundedFrameAsync(pipe, request, protocolJson, ExplorerProtocolDefaults.MaximumRequestBytes, cancellationToken);
    var response = await ReadBoundedFrameAsync<ExplorerResponseEnvelope>(
        pipe,
        protocolJson,
        ExplorerProtocolDefaults.MaximumResponseBytes,
        cancellationToken);
    return response.Success;
}

static string? Value(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static async Task<T> ReadBoundedFrameAsync<T>(
    Stream stream,
    JsonSerializerOptions json,
    int maximum,
    CancellationToken cancellationToken)
{
    var lengthBytes = new byte[sizeof(int)];
    await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
    var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
    if (length is <= 0 || length > maximum)
    {
        throw new InvalidDataException("Invalid frame length.");
    }

    var bytes = new byte[length];
    await stream.ReadExactlyAsync(bytes, cancellationToken);
    return JsonSerializer.Deserialize<T>(bytes, json) ?? throw new JsonException("Missing frame.");
}

static async Task WriteBoundedFrameAsync<T>(
    Stream stream,
    T value,
    JsonSerializerOptions json,
    int maximum,
    CancellationToken cancellationToken)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(value, json);
    if (bytes.Length is 0 || bytes.Length > maximum)
    {
        throw new InvalidDataException("Invalid frame size.");
    }

    var length = new byte[sizeof(int)];
    BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
    await stream.WriteAsync(length, cancellationToken);
    await stream.WriteAsync(bytes, cancellationToken);
    await stream.FlushAsync(cancellationToken);
}
