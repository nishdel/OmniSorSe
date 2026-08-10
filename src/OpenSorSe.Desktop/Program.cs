using Avalonia;
using System;

namespace OpenSorSe.Desktop;

/// <summary>
/// Starts the OpenSorSe desktop application.
/// </summary>
sealed class Program
{
    /// <summary>
    /// Starts Avalonia with the classic desktop application lifetime.
    /// </summary>
    /// <param name="args">Command-line arguments supplied to the application.</param>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--package-smoke-test", var smokeDataRoot])
        {
            return PackageSmokeTest.RunAsync(smokeDataRoot).GetAwaiter().GetResult();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>
    /// Builds the Avalonia application configuration used at runtime and by design tools.
    /// </summary>
    /// <returns>A configured Avalonia application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
