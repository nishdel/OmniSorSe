using System.Diagnostics;
using System.Text;

namespace OpenSorSe.Application.Content;

/// <summary>Contains one bounded Tesseract CLI process outcome.</summary>
public sealed record TesseractProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated);

/// <summary>Abstracts asynchronous bounded Tesseract process execution for deterministic testing.</summary>
public interface ITesseractProcessRunner
{
    /// <summary>Executes one exact executable and argument list without invoking a shell.</summary>
    Task<TesseractProcessResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        int maximumOutputCharacters,
        int maximumErrorCharacters,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>Runs local Tesseract with bounded streams, finite timeout, cancellation, and process-tree cleanup.</summary>
public sealed class TesseractProcessRunner : ITesseractProcessRunner
{
    /// <inheritdoc />
    public async Task<TesseractProcessResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        int maximumOutputCharacters,
        int maximumErrorCharacters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("The local process could not be started.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var outputTask = ReadBoundedAndDrainAsync(
            process.StandardOutput,
            maximumOutputCharacters,
            timeoutSource.Token);
        var errorTask = ReadBoundedAndDrainAsync(
            process.StandardError,
            maximumErrorCharacters,
            timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return new TesseractProcessResult(
                process.ExitCode,
                output.Text,
                error.Text,
                output.WasTruncated,
                error.WasTruncated);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await ObserveReaderCompletionAsync(outputTask, errorTask).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<BoundedText> ReadBoundedAndDrainAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder(Math.Min(4096, maximumCharacters));
        var buffer = new char[4096];
        var truncated = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            var remaining = maximumCharacters - output.Length;
            if (remaining > 0)
            {
                output.Append(buffer, 0, Math.Min(remaining, count));
            }

            truncated |= count > remaining;
        }

        return new BoundedText(output.ToString(), truncated);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task ObserveReaderCompletionAsync(
        Task<BoundedText> outputTask,
        Task<BoundedText> errorTask)
    {
        try
        {
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Both redirected-stream tasks are observed after cancellation so no fault is left unobserved.
        }
    }

    private sealed record BoundedText(string Text, bool WasTruncated);
}
