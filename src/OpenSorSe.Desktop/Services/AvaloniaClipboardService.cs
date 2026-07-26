using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace OpenSorSe.Desktop.Services;

/// <summary>Uses the active Avalonia desktop window clipboard for explicit copy actions.</summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    /// <inheritdoc />
    public async Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = GetClipboard() ?? throw new InvalidOperationException("The system clipboard is unavailable.");
        await clipboard.SetTextAsync(text);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static IClipboard? GetClipboard()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
            ? lifetime.MainWindow?.Clipboard
            : null;
    }
}
