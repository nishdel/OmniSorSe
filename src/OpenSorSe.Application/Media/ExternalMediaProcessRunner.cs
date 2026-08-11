using System.Diagnostics;
using System.Text;

namespace OpenSorSe.Application.Media;

/// <summary>Contains one bounded external media-tool response.</summary>
public sealed record MediaProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated);

/// <summary>Runs exact local media executables without shell interpretation.</summary>
public interface IMediaProcessRunner
{
    /// <summary>Executes one bounded cancellable process request.</summary>
    Task<MediaProcessResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        int maximumOutputCharacters,
        int maximumErrorCharacters,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>Runs ffprobe/ffmpeg-compatible tools with bounded streams, timeout, and process-tree cleanup.</summary>
public sealed class ExternalMediaProcessRunner : IMediaProcessRunner
{
    /// <inheritdoc />
    public async Task<MediaProcessResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        int maximumOutputCharacters,
        int maximumErrorCharacters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        if (maximumOutputCharacters < 1 || maximumErrorCharacters < 1 || timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumOutputCharacters));
        }

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
            if (argument.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("Media-tool arguments cannot contain null characters.", nameof(arguments));
            }

            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("The local media tool could not be started.");
        }

        using var timeoutOnlySource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutOnlySource.Token);
        var outputTask = ReadBoundedAndDrainAsync(process.StandardOutput, maximumOutputCharacters, linkedSource.Token);
        var errorTask = ReadBoundedAndDrainAsync(process.StandardError, maximumErrorCharacters, linkedSource.Token);
        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return new MediaProcessResult(
                process.ExitCode,
                output.Text,
                error.Text,
                output.WasTruncated,
                error.WasTruncated);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await ObserveReadersAsync(outputTask, errorTask).ConfigureAwait(false);
            if (timeoutOnlySource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The local media tool exceeded its configured timeout.");
            }

            throw;
        }
    }

    private static async Task<BoundedText> ReadBoundedAndDrainAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder(Math.Min(maximumCharacters, 4096));
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
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task ObserveReadersAsync(Task<BoundedText> output, Task<BoundedText> error)
    {
        try
        {
            await Task.WhenAll(output, error).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    private sealed record BoundedText(string Text, bool WasTruncated);
}
