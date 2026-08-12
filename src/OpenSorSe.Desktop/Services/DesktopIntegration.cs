using System.Diagnostics;
using OpenSorSe.Core.Platform;

namespace OpenSorSe.Desktop.Services;

/// <summary>Opens one exact path through a platform desktop without constructing a shell command.</summary>
public interface IDesktopIntegration
{
    /// <summary>Gets the current adapter support state.</summary>
    PlatformSupportState SupportState { get; }

    /// <summary>Gets a user-facing support or limitation explanation.</summary>
    string Explanation { get; }

    /// <summary>Asks the desktop to open one existing, already-normalized path.</summary>
    ExternalLaunchResult OpenPath(string normalizedPath, string successMessage);
}

/// <summary>Creates the explicit desktop adapter for the current host.</summary>
public static class DesktopIntegrationFactory
{
    /// <summary>Creates a Windows, Linux, or unavailable desktop adapter.</summary>
    public static IDesktopIntegration Create() => PlatformServices.CurrentPlatform switch
    {
        HostPlatformKind.Windows => new WindowsDesktopIntegration(),
        HostPlatformKind.Linux => new LinuxDesktopIntegration(),
        _ => new UnavailableDesktopIntegration(),
    };
}

/// <summary>Uses Windows registered-file and Explorer integration.</summary>
public sealed class WindowsDesktopIntegration : IDesktopIntegration
{
    /// <inheritdoc />
    public PlatformSupportState SupportState => PlatformSupportState.Supported;

    /// <inheritdoc />
    public string Explanation => "Windows registered-file and Explorer integration is available.";

    /// <inheritdoc />
    public ExternalLaunchResult OpenPath(string normalizedPath, string successMessage) =>
        DesktopPathOpener.TryOpen(normalizedPath, successMessage);
}

/// <summary>Uses the Linux desktop's registered opener through .NET shell execution.</summary>
public sealed class LinuxDesktopIntegration : IDesktopIntegration
{
    /// <inheritdoc />
    public PlatformSupportState SupportState => PlatformSupportState.SupportedWithLimitations;

    /// <inheritdoc />
    public string Explanation =>
        "Linux desktop opening requires a graphical session and a configured default application.";

    /// <inheritdoc />
    public ExternalLaunchResult OpenPath(string normalizedPath, string successMessage) =>
        DesktopPathOpener.TryOpen(normalizedPath, successMessage);
}

/// <summary>Disables desktop opening when the host has no verified adapter.</summary>
public sealed class UnavailableDesktopIntegration : IDesktopIntegration
{
    /// <inheritdoc />
    public PlatformSupportState SupportState => PlatformSupportState.Unavailable;

    /// <inheritdoc />
    public string Explanation => "OmniSorSe has no verified desktop integration for this platform.";

    /// <inheritdoc />
    public ExternalLaunchResult OpenPath(string normalizedPath, string successMessage) =>
        ExternalLaunchResult.Failure(Explanation);
}

internal static class DesktopPathOpener
{
    internal static ExternalLaunchResult TryOpen(string target, string successMessage)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
            return process is null
                ? ExternalLaunchResult.Failure("The desktop did not open the selected item.")
                : ExternalLaunchResult.Success(successMessage);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return ExternalLaunchResult.Failure(
                "The selected item could not be opened by the desktop integration.");
        }
    }
}
