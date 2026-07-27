using OpenSorSe.Core.Platform;

namespace OpenSorSe.Desktop.Services;

/// <summary>
/// Uses shell execution for explicit, validated paths without passing a command line through a shell interpreter.
/// </summary>
public sealed class ExternalFileLauncher : IExternalFileLauncher
{
    private readonly IDesktopIntegration _desktopIntegration;
    private readonly IPathSemantics _pathSemantics;

    /// <summary>Creates a launcher using the current platform adapters.</summary>
    public ExternalFileLauncher()
        : this(DesktopIntegrationFactory.Create(), PlatformServices.CurrentPathSemantics)
    {
    }

    /// <summary>Creates a launcher with explicit desktop and path adapters.</summary>
    public ExternalFileLauncher(
        IDesktopIntegration desktopIntegration,
        IPathSemantics pathSemantics)
    {
        _desktopIntegration = desktopIntegration ??
                              throw new ArgumentNullException(nameof(desktopIntegration));
        _pathSemantics = pathSemantics ?? throw new ArgumentNullException(nameof(pathSemantics));
    }

    /// <inheritdoc />
    public Task<ExternalLaunchResult> OpenFileAsync(string fullPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryNormalizeAbsolutePath(fullPath, out var normalizedPath))
        {
            return Task.FromResult(ExternalLaunchResult.Failure("The selected file path is invalid."));
        }

        if (!File.Exists(normalizedPath))
        {
            return Task.FromResult(ExternalLaunchResult.Failure("The selected file is no longer available."));
        }

        return Task.FromResult(
            _desktopIntegration.OpenPath(normalizedPath, "The selected file was opened."));
    }

    /// <inheritdoc />
    public Task<ExternalLaunchResult> OpenContainingFolderAsync(string fullPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryNormalizeAbsolutePath(fullPath, out var normalizedPath))
        {
            return Task.FromResult(ExternalLaunchResult.Failure("The selected file path is invalid."));
        }

        var directory = Path.GetDirectoryName(normalizedPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Task.FromResult(ExternalLaunchResult.Failure("The containing folder is no longer available."));
        }

        return Task.FromResult(
            _desktopIntegration.OpenPath(directory, "The containing folder was opened."));
    }

    /// <inheritdoc />
    public Task<ExternalLaunchResult> OpenFolderAsync(string fullPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryNormalizeAbsolutePath(fullPath, out var normalizedPath) ||
            !Directory.Exists(normalizedPath))
        {
            return Task.FromResult(ExternalLaunchResult.Failure("The selected folder is unavailable."));
        }

        return Task.FromResult(
            _desktopIntegration.OpenPath(normalizedPath, "The selected folder was opened."));
    }

    private bool TryNormalizeAbsolutePath(string? fullPath, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath))
        {
            return false;
        }

        try
        {
            normalizedPath = _pathSemantics.NormalizeAbsolutePath(fullPath);
            return Path.IsPathFullyQualified(normalizedPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }
}
