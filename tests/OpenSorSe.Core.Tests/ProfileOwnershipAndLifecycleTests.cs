using OpenSorSe.Core.Lifecycle;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Core.Tests;

/// <summary>Protects profile one-writer ownership and abnormal-shutdown detection.</summary>
public sealed class ProfileOwnershipAndLifecycleTests
{
    /// <summary>Verifies a concurrent owner is rejected and clean disposal releases the profile.</summary>
    [Fact]
    public async Task ProfileOwnership_RejectsSecondThreadAndReleasesCleanly()
    {
        using var directory = new TemporaryDirectory();
        using (var first = ProfileOwnershipLease.Acquire(directory.Path))
        {
            Assert.Equal(ProfileOwnershipStatus.Owned, first.Status);
            var exception = await Assert.ThrowsAsync<ProfileOwnershipException>(
                () => Task.Run(() => ProfileOwnershipLease.Acquire(directory.Path + Path.DirectorySeparatorChar)));
            Assert.Equal(first.ProfileFingerprint, exception.ProfileFingerprint);
        }

        using var reacquired = await Task.Run(() => ProfileOwnershipLease.Acquire(directory.Path));
        Assert.Equal(ProfileOwnershipStatus.Owned, reacquired.Status);
    }

    /// <summary>Verifies independent profiles can be owned concurrently.</summary>
    [Fact]
    public async Task ProfileOwnership_SeparateProfilesDoNotConflict()
    {
        using var firstDirectory = new TemporaryDirectory();
        using var secondDirectory = new TemporaryDirectory();
        using var first = ProfileOwnershipLease.Acquire(firstDirectory.Path);
        using var second = await Task.Run(() => ProfileOwnershipLease.Acquire(secondDirectory.Path));
        Assert.NotEqual(first.ProfileFingerprint, second.ProfileFingerprint);
    }

    /// <summary>Verifies profile-lock setup errors are explicit rather than ignored.</summary>
    [Fact]
    public void ProfileOwnership_LockCreationFailureIsExplicit()
    {
        using var directory = new TemporaryDirectory();
        var filePath = System.IO.Path.Combine(directory.Path, "not-a-directory");
        File.WriteAllText(filePath, "occupied");
        Assert.Throws<ProfileOwnershipException>(() => ProfileOwnershipLease.Acquire(filePath));
    }

    /// <summary>Verifies first, abnormal, and clean lifecycle states are distinguished.</summary>
    [Fact]
    public async Task RunMarker_DistinguishesFirstAbnormalAndCleanRuns()
    {
        using var directory = new TemporaryDirectory();
        var first = ApplicationRunStateMarker.Begin(directory.Path);
        Assert.False(first.HadPreviousRun);
        Assert.False(first.PreviousShutdownWasAbnormal);

        var afterUnclean = ApplicationRunStateMarker.Begin(directory.Path);
        Assert.True(afterUnclean.HadPreviousRun);
        Assert.True(afterUnclean.PreviousShutdownWasAbnormal);
        await afterUnclean.MarkCleanAsync();

        var afterClean = ApplicationRunStateMarker.Begin(directory.Path);
        Assert.True(afterClean.HadPreviousRun);
        Assert.False(afterClean.PreviousShutdownWasAbnormal);
        await afterClean.MarkCleanAsync();

        await File.WriteAllTextAsync(Path.Combine(directory.Path, "application-run-state.json"), "{truncated");
        var afterCorruptMarker = ApplicationRunStateMarker.Begin(directory.Path);
        Assert.True(afterCorruptMarker.HadPreviousRun);
        Assert.True(afterCorruptMarker.PreviousShutdownWasAbnormal);
        await afterCorruptMarker.MarkCleanAsync();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OmniSorSe-profile-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
