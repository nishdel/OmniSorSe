using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Errors;
using OpenSorSe.Core.Logging;
using OpenSorSe.Scanner.Models;

namespace OpenSorSe.Scanner;

/// <summary>
/// Performs deterministic, read-free exact duplicate detection using normalized SHA-256 hashes.
/// </summary>
public sealed class DuplicateDetector : IDuplicateDetector
{
    private const string Algorithm = "SHA-256";
    private const string LoggerCategory = "Scanner";
    private readonly IErrorHandler _errorHandler;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a duplicate detector using shared diagnostics infrastructure.
    /// </summary>
    /// <param name="loggingService">The centralized logging service.</param>
    /// <param name="errorHandler">The handler for unexpected operation failures.</param>
    public DuplicateDetector(ILoggingService loggingService, IErrorHandler errorHandler)
    {
        ArgumentNullException.ThrowIfNull(loggingService);
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _logger = loggingService.CreateLogger(LoggerCategory);
    }

    /// <inheritdoc />
    public Task<DuplicateDetectionResult> DetectAsync(
        IReadOnlyCollection<FileEntry> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEntries(files);

        try
        {
            var result = Detect(files, cancellationToken);
            return Task.FromResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Duplicate detection could not be completed due to an unexpected error.");
            _errorHandler.Report(new ApplicationError(
                LoggerCategory,
                "Duplicate detection could not be completed due to an unexpected error.",
                ApplicationErrorSeverity.Error,
                exception));
            throw;
        }
    }

    private DuplicateDetectionResult Detect(IReadOnlyCollection<FileEntry> files, CancellationToken cancellationToken)
    {
        var preparedEntries = new PreparedEntry[files.Count];
        var issues = new List<DuplicateDetectionIssue>();
        var countsByHash = new Dictionary<string, int>(StringComparer.Ordinal);
        var preparedIndex = 0;

        foreach (var entry in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryNormalizeHash(entry, out var normalizedHash, out var issue))
            {
                issues.Add(issue!);
                _logger.LogWarning("Duplicate-detection issue {IssueKind}: {Message}", issue!.Kind, issue.Message);
                preparedEntries[preparedIndex++] = new PreparedEntry(entry, null);
                continue;
            }

            countsByHash.TryGetValue(normalizedHash!, out var count);
            countsByHash[normalizedHash!] = count + 1;
            preparedEntries[preparedIndex++] = new PreparedEntry(entry, normalizedHash);
        }

        var output = new FileEntry[preparedEntries.Length];
        var duplicateMembers = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);
        var duplicateHashesInInputOrder = new List<string>();
        long filesUnique = 0;
        long filesDuplicate = 0;
        long filesUnknown = 0;

        for (var index = 0; index < preparedEntries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = preparedEntries[index];
            if (prepared.NormalizedHash is null)
            {
                filesUnknown++;
                output[index] = prepared.Entry with
                {
                    Duplicate = new DuplicateClassification(DuplicateStatus.Unknown),
                };
                continue;
            }

            if (countsByHash[prepared.NormalizedHash] > 1)
            {
                filesDuplicate++;
                output[index] = prepared.Entry with
                {
                    Duplicate = new DuplicateClassification(
                        DuplicateStatus.Duplicate,
                        GroupId(prepared.NormalizedHash)),
                };
                if (!duplicateMembers.TryGetValue(prepared.NormalizedHash, out var members))
                {
                    members = new List<FileEntry>(countsByHash[prepared.NormalizedHash]);
                    duplicateMembers.Add(prepared.NormalizedHash, members);
                    duplicateHashesInInputOrder.Add(prepared.NormalizedHash);
                }

                members.Add(output[index]);
            }
            else
            {
                filesUnique++;
                output[index] = prepared.Entry with
                {
                    Duplicate = new DuplicateClassification(DuplicateStatus.Unique),
                };
            }
        }

        var groups = new List<DuplicateGroup>(duplicateHashesInInputOrder.Count);
        foreach (var hash in duplicateHashesInInputOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            groups.Add(new DuplicateGroup(
                GroupId(hash),
                Algorithm,
                hash,
                duplicateMembers[hash].ToArray()));
        }

        return new DuplicateDetectionResult(
            output,
            groups.ToArray(),
            new DuplicateDetectionStatistics(files.Count, filesUnique, filesDuplicate, filesUnknown, groups.Count, issues.Count),
            issues.ToArray());
    }

    private static void ValidateEntries(IReadOnlyCollection<FileEntry> files)
    {
        if (files.Any(entry => entry is null))
        {
            throw new ArgumentException("The input collection cannot contain null entries.", nameof(files));
        }
    }

    private static bool TryNormalizeHash(
        FileEntry entry,
        out string? normalizedHash,
        out DuplicateDetectionIssue? issue)
    {
        normalizedHash = null;
        issue = null;
        if (entry.Hash is null)
        {
            issue = new DuplicateDetectionIssue(entry.FullPath, DuplicateDetectionIssueKind.HashUnavailable, "No hash is available for this file.");
            return false;
        }

        if (!string.Equals(entry.Hash.Algorithm, Algorithm, StringComparison.OrdinalIgnoreCase))
        {
            issue = new DuplicateDetectionIssue(entry.FullPath, DuplicateDetectionIssueKind.UnsupportedHashAlgorithm, "The file hash algorithm is unsupported.");
            return false;
        }

        var value = entry.Hash.Value;
        if (string.IsNullOrEmpty(value) || value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            issue = new DuplicateDetectionIssue(entry.FullPath, DuplicateDetectionIssueKind.InvalidHashValue, "The SHA-256 hash value is invalid.");
            return false;
        }

        normalizedHash = value.ToLowerInvariant();
        return true;
    }

    private static string GroupId(string normalizedHash) => $"sha256:{normalizedHash}";

    private readonly record struct PreparedEntry(FileEntry Entry, string? NormalizedHash);
}
