namespace OpenSorSe.Desktop.ViewModels;

/// <summary>
/// Presents static application metadata and copyable external-resource addresses without launching processes.
/// </summary>
public sealed class AboutViewModel : ViewModelBase
{
    /// <summary>
    /// Gets the application name.
    /// </summary>
    public string ApplicationName => "OpenSorSe";

    /// <summary>
    /// Gets the declared 1.4 application version.
    /// </summary>
    public string Version => "1.4";

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
    public string RepositoryAddress => "https://github.com/OpenSorSe/OpenSorSe";

    /// <summary>
    /// Gets the copyable public project documentation address.
    /// </summary>
    public string DocumentationAddress => "https://github.com/OpenSorSe/OpenSorSe/tree/main/docs";
}
