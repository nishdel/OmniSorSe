namespace OpenSorSe.Core.Platform;

/// <summary>
/// Resolves application-owned storage while preserving the existing Windows
/// location and following XDG conventions on Linux.
/// </summary>
public sealed class ApplicationPathProvider : IApplicationPathProvider
{
    private readonly IPathSemantics _pathSemantics;

    /// <summary>Creates paths for the current process environment.</summary>
    public ApplicationPathProvider()
        : this(
            PlatformServices.CurrentPlatform,
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    /// <summary>Creates deterministic paths for an explicit platform and environment.</summary>
    /// <param name="platform">Platform whose storage conventions should be applied.</param>
    /// <param name="environmentVariableReader">Environment reader, used for XDG variables.</param>
    /// <param name="userProfilePath">Absolute user profile path.</param>
    /// <param name="localApplicationDataPath">Absolute Windows local application-data path.</param>
    public ApplicationPathProvider(
        HostPlatformKind platform,
        Func<string, string?> environmentVariableReader,
        string userProfilePath,
        string? localApplicationDataPath = null)
    {
        ArgumentNullException.ThrowIfNull(environmentVariableReader);
        _pathSemantics = PlatformServices.CreatePathSemantics(platform);
        var userProfile = RequireAbsolute(userProfilePath, nameof(userProfilePath));

        Paths = platform switch
        {
            HostPlatformKind.Windows => WindowsPaths(userProfile, localApplicationDataPath),
            HostPlatformKind.Linux => LinuxPaths(userProfile, environmentVariableReader),
            HostPlatformKind.MacOS => MacPaths(userProfile),
            _ => ConservativePaths(userProfile),
        };
        SettingsFilePath = Path.Combine(Paths.ConfigurationDirectory, "settings.json");
    }

    /// <inheritdoc />
    public ApplicationPathSet Paths { get; }

    /// <inheritdoc />
    public string SettingsFilePath { get; }

    /// <inheritdoc />
    public void EnsureOwnedDirectories()
    {
        var directories = new[]
        {
            Paths.ConfigurationDirectory,
            Paths.DataDirectory,
            Paths.StateDirectory,
            Paths.CacheDirectory,
            Paths.DiagnosticsDirectory,
            Paths.PluginDirectory,
        };
        foreach (var directory in directories.Distinct(_pathSemantics.Comparer))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private ApplicationPathSet WindowsPaths(string userProfile, string? localApplicationDataPath)
    {
        var local = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Path.Combine(userProfile, "AppData", "Local")
            : RequireAbsolute(localApplicationDataPath, nameof(localApplicationDataPath));
        var existingRoot = Path.Combine(local, "OpenSorSe");
        return NewSet(
            existingRoot,
            existingRoot,
            existingRoot,
            existingRoot,
            Path.Combine(existingRoot, "Logs"),
            Path.Combine(existingRoot, "plugins"));
    }

    private ApplicationPathSet LinuxPaths(
        string userProfile,
        Func<string, string?> environmentVariableReader)
    {
        var configuration = Xdg(
            "XDG_CONFIG_HOME",
            Path.Combine(userProfile, ".config"),
            environmentVariableReader);
        var data = Xdg(
            "XDG_DATA_HOME",
            Path.Combine(userProfile, ".local", "share"),
            environmentVariableReader);
        var state = Xdg(
            "XDG_STATE_HOME",
            Path.Combine(userProfile, ".local", "state"),
            environmentVariableReader);
        var cache = Xdg(
            "XDG_CACHE_HOME",
            Path.Combine(userProfile, ".cache"),
            environmentVariableReader);
        return NewSet(
            Path.Combine(configuration, "opensorse"),
            Path.Combine(data, "opensorse"),
            Path.Combine(state, "opensorse"),
            Path.Combine(cache, "opensorse"),
            Path.Combine(state, "opensorse", "logs"),
            Path.Combine(data, "opensorse", "plugins"));
    }

    private ApplicationPathSet MacPaths(string userProfile)
    {
        var support = Path.Combine(userProfile, "Library", "Application Support", "OpenSorSe");
        var cache = Path.Combine(userProfile, "Library", "Caches", "OpenSorSe");
        var logs = Path.Combine(userProfile, "Library", "Logs", "OpenSorSe");
        return NewSet(support, support, support, cache, logs, Path.Combine(support, "plugins"));
    }

    private ApplicationPathSet ConservativePaths(string userProfile)
    {
        var root = Path.Combine(userProfile, ".opensorse");
        return NewSet(root, root, root, Path.Combine(root, "cache"), Path.Combine(root, "logs"), Path.Combine(root, "plugins"));
    }

    private ApplicationPathSet NewSet(
        string configuration,
        string data,
        string state,
        string cache,
        string diagnostics,
        string plugins) =>
        new(
            RequireAbsolute(configuration, nameof(configuration)),
            RequireAbsolute(data, nameof(data)),
            RequireAbsolute(state, nameof(state)),
            RequireAbsolute(cache, nameof(cache)),
            RequireAbsolute(diagnostics, nameof(diagnostics)),
            RequireAbsolute(plugins, nameof(plugins)));

    private string Xdg(
        string variable,
        string fallback,
        Func<string, string?> environmentVariableReader)
    {
        var value = environmentVariableReader(variable);
        return string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value)
            ? RequireAbsolute(fallback, variable)
            : RequireAbsolute(value, variable);
    }

    private string RequireAbsolute(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("An absolute application path is required.", parameterName);
        }

        return _pathSemantics.NormalizeAbsolutePath(path);
    }
}
