namespace OpenSorSe.Desktop.Services;

/// <summary>Represents one controlled desktop-shell launch result.</summary>
public sealed record ExternalLaunchResult(bool Succeeded, string Message)
{
    /// <summary>Creates a successful result.</summary>
    public static ExternalLaunchResult Success(string message) => new(true, message);

    /// <summary>Creates a controlled failure result.</summary>
    public static ExternalLaunchResult Failure(string message) => new(false, message);
}

/// <summary>
/// Opens explicitly selected, already-known files or their containing folders without constructing shell commands.
/// </summary>
public interface IExternalFileLauncher
{
    /// <summary>Asks the operating system to open one existing file with its registered application.</summary>
    Task<ExternalLaunchResult> OpenFileAsync(string fullPath, CancellationToken cancellationToken);

    /// <summary>Asks the operating system to open the existing folder containing one known file.</summary>
    Task<ExternalLaunchResult> OpenContainingFolderAsync(string fullPath, CancellationToken cancellationToken);
}
