using OpenSorSe.Core.Platform;

namespace OpenSorSe.Core.Tests;

/// <summary>Verifies native temporary-directory aliases without admitting user-created storage redirects.</summary>
public sealed class ApplicationStorageFilesTests
{
    /// <summary>Native macOS root aliases are accepted only with their standard immediate targets.</summary>
    [Theory]
    [InlineData("/var", "/private/var")]
    [InlineData("/tmp", "/private/tmp")]
    public void RequireUnlinkedPath_AllowsNativeMacOsTemporaryAliases(string alias, string expectedTarget)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.Equal(expectedTarget, new DirectoryInfo(alias).ResolveLinkTarget(returnFinalTarget: false)?.FullName);
        ApplicationStorageFiles.RequireUnlinkedPath(alias);
        ApplicationStorageFiles.RequireUnlinkedPath(alias + "/");
        ApplicationStorageFiles.RequireUnlinkedPath(Path.Combine(alias, $"omnisorse-missing-{Guid.NewGuid():N}", "future-cache"));
    }

    /// <summary>The platform's real temporary directory permits inventory, including through macOS /var.</summary>
    [Fact]
    public void Enumerate_AllowsPlatformTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omnisorse-storage-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "content.json");
        try
        {
            File.WriteAllText(file, "retained content");
            Assert.Equal(file, Assert.Single(ApplicationStorageFiles.Enumerate(root)).FullName);
            ApplicationStorageFiles.RequireUnlinkedPath(Path.Combine(root, "future-directory", "content.json"));
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(root);
        }
    }

    /// <summary>A user-owned lookalike alias is rejected at the root, as an ancestor, and during inventory, even when dangling.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequireUnlinkedPath_RejectsUserCreatedAliasesWhenSupported(bool dangling)
    {
        var root = Path.Combine(Path.GetTempPath(), $"omnisorse-storage-link-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "private", "var");
        var link = Path.Combine(root, "var");
        Directory.CreateDirectory(root);
        if (!dangling)
        {
            Directory.CreateDirectory(target);
        }

        var linkCreated = false;
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
                linkCreated = true;
            }
            catch (Exception exception) when (OperatingSystem.IsWindows() &&
                exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                // Windows hosts can lack link-creation privilege. Unix CI must execute these assertions.
                return;
            }

            Assert.Throws<IOException>(() => ApplicationStorageFiles.RequireUnlinkedPath(link));
            Assert.Throws<IOException>(() => ApplicationStorageFiles.RequireUnlinkedPath(Path.Combine(link, "future-cache")));
            Assert.Throws<IOException>(() => ApplicationStorageFiles.Enumerate(root));
        }
        finally
        {
            if (linkCreated)
            {
                if (OperatingSystem.IsWindows())
                {
                    Directory.Delete(link);
                }
                else
                {
                    // Unix directory deletion cannot remove a dangling symlink; unlink the entry itself.
                    File.Delete(link);
                }
            }

            if (!dangling)
            {
                Directory.Delete(target);
                Directory.Delete(Path.GetDirectoryName(target)!);
            }

            Directory.Delete(root);
        }
    }
}
