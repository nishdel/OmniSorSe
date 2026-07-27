namespace OpenSorSe.Application.Models;

/// <summary>Describes the terminal state of one sequential processing pipeline run.</summary>
public enum ProcessingStatus
{
    /// <summary>All requested processing pipeline stages completed.</summary>
    Completed,
    /// <summary>Cancellation stopped later pipeline stages.</summary>
    Cancelled,
}
