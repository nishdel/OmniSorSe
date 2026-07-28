using System.Collections.Concurrent;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Core.Persistence;

/// <summary>
/// Serializes process-local access to one application-owned file across all
/// service instances that use the same normalized path.
/// </summary>
/// <remarks>
/// Store classes are normally singletons, but recovery, diagnostics, and tests
/// can construct a second instance. A path-scoped gate prevents those instances
/// from interleaving a load-modify-write sequence. This is not an inter-process
/// lock and grants no access to user files.
/// </remarks>
public sealed class ApplicationFileAccessCoordinator
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(PlatformServices.CurrentPathSemantics.Comparer);

    private readonly SemaphoreSlim _gate;

    /// <summary>
    /// Creates a coordinator for one fully qualified application-owned file.
    /// </summary>
    /// <param name="filePath">The exact file whose process-local access is serialized.</param>
    public ApplicationFileAccessCoordinator(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new ArgumentException("A fully qualified application-owned file path is required.", nameof(filePath));
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(filePath));
        _gate = Gates.GetOrAdd(normalized, static _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Waits asynchronously for exclusive process-local access.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait without acquiring the gate.</param>
    /// <returns>A lease that releases the gate when disposed.</returns>
    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_gate);
    }

    private sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Lease(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}
