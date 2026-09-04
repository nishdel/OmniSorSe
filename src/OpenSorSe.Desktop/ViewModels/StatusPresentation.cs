namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Identifies the accessible meaning of a user-facing status.</summary>
public enum StatusKind
{
    /// <summary>Neutral information.</summary>
    Information,
    /// <summary>An operation is active.</summary>
    Progress,
    /// <summary>An operation completed successfully.</summary>
    Success,
    /// <summary>A recoverable concern requires attention.</summary>
    Warning,
    /// <summary>An operation failed safely.</summary>
    Error,
}

/// <summary>
/// Provides consistent, text-labelled status metadata without exposing raw exceptions.
/// </summary>
public sealed record StatusPresentation(StatusKind Kind, string Message, string? TechnicalDetails = null)
{
    /// <summary>Gets the permanent textual severity label.</summary>
    public string Label => Kind switch
    {
        StatusKind.Information => "Information",
        StatusKind.Progress => "In progress",
        StatusKind.Success => "Success",
        StatusKind.Warning => "Warning",
        StatusKind.Error => "Error",
        _ => "Status",
    };

    /// <summary>Gets a text-only accessible rendering that never relies on color.</summary>
    public string AccessibleText => $"{Label}: {Message}";

    /// <summary>Gets a compact, non-color severity symbol.</summary>
    public string Symbol => Kind switch
    {
        StatusKind.Information => "i",
        StatusKind.Progress => "…",
        StatusKind.Success => "✓",
        StatusKind.Warning => "!",
        StatusKind.Error => "×",
        _ => "•",
    };

    /// <summary>Gets whether warning styling should be applied.</summary>
    public bool IsWarning => Kind == StatusKind.Warning;

    /// <summary>Gets whether error styling should be applied.</summary>
    public bool IsError => Kind == StatusKind.Error;

    /// <summary>Gets whether success styling should be applied.</summary>
    public bool IsSuccess => Kind == StatusKind.Success;

    /// <summary>Gets whether progress styling should be applied.</summary>
    public bool IsProgress => Kind == StatusKind.Progress;

    /// <summary>Gets whether optional separately bounded technical details exist.</summary>
    public bool HasTechnicalDetails => !string.IsNullOrWhiteSpace(TechnicalDetails);

    /// <summary>Creates neutral information.</summary>
    public static StatusPresentation Information(string message) => new(StatusKind.Information, message);

    /// <summary>Creates an active-progress status.</summary>
    public static StatusPresentation Progress(string message) => new(StatusKind.Progress, message);

    /// <summary>Creates a successful status.</summary>
    public static StatusPresentation Success(string message) => new(StatusKind.Success, message);

    /// <summary>Creates a warning status.</summary>
    public static StatusPresentation Warning(string message, string? technicalDetails = null) =>
        new(StatusKind.Warning, message, technicalDetails);

    /// <summary>Creates an error status.</summary>
    public static StatusPresentation Error(string message, string? technicalDetails = null) =>
        new(StatusKind.Error, message, technicalDetails);
}
