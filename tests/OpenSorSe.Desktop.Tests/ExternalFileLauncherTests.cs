using OpenSorSe.Desktop.Services;

namespace OpenSorSe.Desktop.Tests;

/// <summary>Verifies launcher validation paths without opening an external process.</summary>
public sealed class ExternalFileLauncherTests
{
    /// <summary>Verifies relative and missing file targets fail before shell execution.</summary>
    [Fact]
    public async Task OpenFile_InvalidOrMissingTarget_ReturnsControlledFailure()
    {
        var launcher = new ExternalFileLauncher();
        var missingPath = Path.Combine(Path.GetTempPath(), $"opensorse-missing-{Guid.NewGuid():N}.txt");

        var relative = await launcher.OpenFileAsync("relative.txt", CancellationToken.None);
        var missing = await launcher.OpenFileAsync(missingPath, CancellationToken.None);

        Assert.False(relative.Succeeded);
        Assert.False(missing.Succeeded);
        Assert.Contains("invalid", relative.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no longer available", missing.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies a missing containing folder fails safely without invoking the shell.</summary>
    [Fact]
    public async Task OpenContainingFolder_MissingTarget_ReturnsControlledFailure()
    {
        var launcher = new ExternalFileLauncher();
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            $"opensorse-missing-{Guid.NewGuid():N}",
            "file.txt");

        var result = await launcher.OpenContainingFolderAsync(missingPath, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("no longer available", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies caller cancellation is observed before validation or shell execution.</summary>
    [Fact]
    public async Task OpenFile_PreCancelled_ThrowsCancellation()
    {
        var launcher = new ExternalFileLauncher();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => launcher.OpenFileAsync(Path.GetTempPath(), cancellation.Token));
    }
}
