using OpenSorSe.Core;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Presents static application metadata and copyable external-resource addresses without launching processes.
/// </summary>
public sealed class AboutViewModel : ViewModelBase
{
    /// <summary>
    /// Gets the application name.
    /// </summary>
    public string ApplicationName => "OmniSorSe";

    /// <summary>
    /// Gets the declared application version.
    /// </summary>
    public string Version => ApplicationVersionInfo.Display;

    /// <summary>
    /// Gets the project license displayed by the current application.
    /// </summary>
    public string License => "MIT License";

    /// <summary>
    /// Gets a concise acknowledgement of the local-first project intent.
    /// </summary>
    public string Acknowledgements => "Built with .NET and Avalonia UI for local-first file organization.";

    /// <summary>
    /// Gets the copyable project repository address.
    /// </summary>
    public string RepositoryAddress => "https://github.com/nishdel/OpenSorSe";

    /// <summary>
    /// Gets the copyable public project documentation address.
    /// </summary>
    public string DocumentationAddress => "https://github.com/nishdel/OpenSorSe/tree/main/docs";
}
