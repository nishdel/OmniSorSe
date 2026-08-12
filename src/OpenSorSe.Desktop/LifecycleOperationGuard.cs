namespace OpenSorSe.Desktop;

/// <summary>
/// Contains expected desktop-lifetime failures at process boundaries while
/// requiring the caller to record the original exception.
/// </summary>
/// <remarks>
/// This boundary is intentionally narrow. It is for startup rollback and
/// shutdown cleanup, where allowing one disposal failure to escape would hide
/// later cleanup and can surface as a native managed-exception dialog. It must
/// not be used to suppress failures inside ordinary application operations.
/// </remarks>
internal static class LifecycleOperationGuard
{
    /// <summary>Runs one lifecycle step and reports, rather than propagates, a failure.</summary>
    /// <param name="operation">A stable, content-free operation category.</param>
    /// <param name="action">The startup, rollback, or shutdown step.</param>
    /// <param name="reportFailure">The mandatory diagnostic callback.</param>
    /// <returns><see langword="true"/> when the operation completed; otherwise <see langword="false"/>.</returns>
    internal static bool TryExecute(
        string operation,
        Action action,
        Action<string, Exception> reportFailure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(reportFailure);

        try
        {
            action();
            return true;
        }
        catch (Exception exception)
        {
            try
            {
                reportFailure(operation, exception);
            }
            catch (Exception reportingException)
            {
                System.Diagnostics.Trace.TraceError(
                    "OmniSorSe could not record lifecycle failure {0} ({1}); reporter failed ({2}).",
                    operation,
                    exception.GetType().Name,
                    reportingException.GetType().Name);
            }

            return false;
        }
    }
}
