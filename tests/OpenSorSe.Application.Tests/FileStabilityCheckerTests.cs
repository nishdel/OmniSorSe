#pragma warning disable CS1591

using OpenSorSe.Application.Watching;

namespace OpenSorSe.Application.Tests;

public sealed class FileStabilityCheckerTests
{
    [Fact]
    public async Task TwoEqualObservations_AreStable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensorse-stability-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "0123456789");
        var probe = Probe(10, DateTimeOffset.UnixEpoch, path);
        try
        {
            var fileSystem = new SequenceFileSystem(probe, probe);
            var checker = new FileStabilityChecker(fileSystem);

            var result = await checker.WaitForStableAsync(
                probe.FullPath,
                TimeSpan.Zero,
                3,
                CancellationToken.None);

            Assert.True(result.IsStable);
            Assert.Equal(2, result.Attempts);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RepeatedSizeAndTimeChanges_AreDeferred()
    {
        var fileSystem = new SequenceFileSystem(
            Probe(1, DateTimeOffset.UnixEpoch),
            Probe(2, DateTimeOffset.UnixEpoch.AddSeconds(1)),
            Probe(3, DateTimeOffset.UnixEpoch.AddSeconds(2)));
        var checker = new FileStabilityChecker(fileSystem);

        var result = await checker.WaitForStableAsync(
            Path.Combine(Path.GetTempPath(), "changing.txt"),
            TimeSpan.Zero,
            3,
            CancellationToken.None);

        Assert.False(result.IsStable);
        Assert.Equal(3, result.Attempts);
        Assert.Contains("retried", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileDisappearsDuringObservation_IsDeferred()
    {
        var fileSystem = new SequenceFileSystem((WatchedFileProbe?)null);
        var checker = new FileStabilityChecker(fileSystem);

        var result = await checker.WaitForStableAsync(
            Path.Combine(Path.GetTempPath(), "gone.txt"),
            TimeSpan.Zero,
            3,
            CancellationToken.None);

        Assert.False(result.IsStable);
        Assert.Equal(1, result.Attempts);
        Assert.Contains("no longer available", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WatchedFileProbe Probe(
        long length,
        DateTimeOffset modified,
        string? path = null) => new(
        path ?? Path.Combine(Path.GetTempPath(), "changing.txt"),
        false,
        length,
        DateTimeOffset.UnixEpoch,
        modified,
        FileAttributes.Normal,
        "stable:1");

    private sealed class SequenceFileSystem(params WatchedFileProbe?[] probes) : IWatchedFileSystem
    {
        private int _index;
        public bool DirectoryExists(string path) => true;
        public Task<IReadOnlyList<WatchedFileProbe>> EnumerateAsync(
            WatchedFolderConfiguration configuration,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WatchedFileProbe>>([]);
        public Task<WatchedFileProbe?> ProbeAsync(string path, CancellationToken cancellationToken)
        {
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, probes.Length - 1);
            return Task.FromResult(probes[index]);
        }
    }
}
