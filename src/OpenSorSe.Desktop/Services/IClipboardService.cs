namespace OpenSorSe.Desktop.Services;

/// <summary>Writes explicitly requested bounded text to the system clipboard.</summary>
public interface IClipboardService
{
    /// <summary>Copies text without reading clipboard content.</summary>
    Task SetTextAsync(string text, CancellationToken cancellationToken);
}
