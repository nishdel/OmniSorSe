using Avalonia.Controls;
using Avalonia.Media;

namespace OpenSorSe.Desktop.Views;

/// <summary>Provides a reusable selectable monospaced diagnostic text surface.</summary>
public sealed class DiagnosticTextBox : TextBox
{
    /// <summary>Initializes a read-only monospaced text surface.</summary>
    public DiagnosticTextBox()
    {
        IsReadOnly = true;
        AcceptsReturn = true;
        FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, monospace");
        TextWrapping = TextWrapping.Wrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(this, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(this, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
    }
}
