using System.Diagnostics;

namespace OpenSorSe.Desktop.Services;

/// <summary>
/// Uses shell execution for explicit, validated paths without passing a command line through a shell interpreter.
/// </summary>
public sealed class ExternalFileLauncher : IExternalFileLauncher
{
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

        return Task.FromResult(TryStart(normalizedPath, "The selected file was opened."));
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

        return Task.FromResult(TryStart(directory, "The containing folder was opened."));
    }

    private static ExternalLaunchResult TryStart(string target, string successMessage)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
            return process is null
                ? ExternalLaunchResult.Failure("The operating system did not open the selected item.")
                : ExternalLaunchResult.Success(successMessage);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return ExternalLaunchResult.Failure("The selected item could not be opened with the operating system.");
        }
    }

    private static bool TryNormalizeAbsolutePath(string? fullPath, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(fullPath);
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
