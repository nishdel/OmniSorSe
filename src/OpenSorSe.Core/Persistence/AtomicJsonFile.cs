using System.Text.Json;

namespace OpenSorSe.Core.Persistence;

/// <summary>
/// Writes a complete bounded JSON document to a unique sibling before
/// atomically replacing one application-owned destination.
/// </summary>
public static class AtomicJsonFile
{
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// Serializes a value to a durable temporary sibling and replaces the
    /// destination only after serialization, size validation, and flushing
    /// have completed successfully.
    /// </summary>
    /// <typeparam name="T">The JSON document type.</typeparam>
    /// <param name="destinationPath">The fully qualified application-owned destination.</param>
    /// <param name="value">The complete value to serialize.</param>
    /// <param name="serializerOptions">The owning store's compatibility options.</param>
    /// <param name="maximumBytes">The maximum encoded file size.</param>
    /// <param name="cancellationToken">Cancels before replacement and preserves the prior destination.</param>
    /// <param name="oversizedExceptionFactory">
    /// Optional factory for the owning store's established capacity exception.
    /// The first argument is the actual byte count and the second is the limit.
    /// </param>
    public static async Task WriteAsync<T>(
        string destinationPath,
        T value,
        JsonSerializerOptions serializerOptions,
        long maximumBytes,
        CancellationToken cancellationToken,
        Func<long, long, Exception>? oversizedExceptionFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(serializerOptions);
        if (!Path.IsPathFullyQualified(destinationPath))
        {
            throw new ArgumentException("A fully qualified application-owned destination is required.", nameof(destinationPath));
        }

        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidDataException("The application-owned destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             new FileStreamOptions
                             {
                                 Access = FileAccess.Write,
                                 Mode = FileMode.CreateNew,
                                 Share = FileShare.None,
                                 BufferSize = BufferSize,
                                 Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                             }))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    serializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                stream.Flush(flushToDisk: true);
            }

            var actualBytes = new FileInfo(temporaryPath).Length;
            if (actualBytes > maximumBytes)
            {
                throw oversizedExceptionFactory?.Invoke(actualBytes, maximumBytes)
                      ?? new InvalidDataException("The encoded application-data file exceeds its supported size.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Cleanup is best effort. Never replace the original write or
            // cancellation exception with a temporary-file cleanup failure.
        }
    }
}
