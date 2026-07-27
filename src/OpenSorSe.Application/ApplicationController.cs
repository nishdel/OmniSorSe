using OpenSorSe.Application.Models;

namespace OpenSorSe.Application;

/// <summary>Provides the narrow UI-to-processing-session routing boundary without filesystem behavior.</summary>
public sealed class ApplicationController : IApplicationController
{
    private readonly IProcessingSessionManager _sessionManager;

    /// <summary>Initializes the controller with session management.</summary>
    /// <param name="sessionManager">The process-lifetime session service.</param>
    public ApplicationController(IProcessingSessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    /// <inheritdoc />
    public Task<ProcessingSessionResult> StartProcessingAsync(ProcessingRequest request, IProgress<ProcessingProgress>? progress = null, CancellationToken cancellationToken = default) =>
        _sessionManager.RunAsync(request, progress, cancellationToken);
}
