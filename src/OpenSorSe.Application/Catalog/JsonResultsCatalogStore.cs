using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenSorSe.Application.Models;
using OpenSorSe.Application.Tags;
using OpenSorSe.Core.Logging;
using OpenSorSe.Core.Persistence;

namespace OpenSorSe.Application.Catalog;

/// <summary>
/// Stores bounded, display-safe completed result snapshots in a versioned JSON file owned by OmniSorSe.
/// </summary>
public sealed class JsonResultsCatalogStore : IResultsCatalogStore
{
    private const int MinimumReadableSchemaVersion = 1;
    private const int CurrentSchemaVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _catalogFilePath;
    private readonly ApplicationFileAccessCoordinator _fileAccess;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    /// <summary>
    /// Initializes a catalog store at an explicit rooted application-data path.
    /// </summary>
    /// <param name="catalogFilePath">The absolute path of the application-owned catalog file.</param>
    /// <param name="loggingService">The centralized redacted diagnostic logging service.</param>
    public JsonResultsCatalogStore(string catalogFilePath, ILoggingService loggingService)
    {
        if (string.IsNullOrWhiteSpace(catalogFilePath) || !Path.IsPathRooted(catalogFilePath))
        {
            throw new ArgumentException("An absolute catalog file path is required.", nameof(catalogFilePath));
        }

        _catalogFilePath = catalogFilePath;
        _fileAccess = new ApplicationFileAccessCoordinator(catalogFilePath);
        _logger = (loggingService ?? throw new ArgumentNullException(nameof(loggingService))).CreateLogger(nameof(JsonResultsCatalogStore));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogEntrySummary>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Array.AsReadOnly((await LoadCoreAsync(cancellationToken).ConfigureAwait(false))
                .OrderByDescending(entry => entry.SavedAtUtc)
                .ThenByDescending(entry => entry.Id, StringComparer.Ordinal)
                .Select(ToSummary)
                .ToArray());
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<CatalogEntry?> LoadAsync(string entryId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await LoadCoreAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(entry => string.Equals(entry.Id, entryId, StringComparison.Ordinal));
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<CatalogEntrySummary> SaveAsync(CatalogEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Snapshot?.Files?.Count > CatalogLimits.MaximumFilesPerEntry)
        {
            throw new CatalogCapacityExceededException(
                $"A local catalog entry may contain at most {CatalogLimits.MaximumFilesPerEntry} files.");
        }

        if (entry.SourceRoots?.Count > CatalogLimits.MaximumSourceRootCount)
        {
            throw new CatalogCapacityExceededException(
                $"A local catalog entry may contain at most {CatalogLimits.MaximumSourceRootCount} source roots.");
        }

        var sanitized = SanitizeForStorage(entry);
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = (await LoadCoreAsync(cancellationToken).ConfigureAwait(false))
                .Where(candidate => !string.Equals(candidate.Id, sanitized.Id, StringComparison.Ordinal))
                .Append(sanitized)
                .OrderByDescending(candidate => candidate.SavedAtUtc)
                .ThenByDescending(candidate => candidate.Id, StringComparer.Ordinal)
                .Take(CatalogLimits.MaximumEntryCount)
                .ToArray();
            await SaveCoreAsync(entries, cancellationToken).ConfigureAwait(false);
            return ToSummary(sanitized);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string entryId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            var remaining = entries.Where(entry => !string.Equals(entry.Id, entryId, StringComparison.Ordinal)).ToArray();
            if (remaining.Length == entries.Count)
            {
                return false;
            }

            await SaveCoreAsync(remaining, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var fileAccess = await _fileAccess.AcquireAsync(cancellationToken).ConfigureAwait(false);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _ = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_catalogFilePath))
            {
                File.Delete(_catalogFilePath);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<IReadOnlyList<CatalogEntry>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_catalogFilePath))
        {
            return Array.Empty<CatalogEntry>();
        }

        try
        {
            if (new FileInfo(_catalogFilePath).Length > CatalogLimits.MaximumCatalogFileBytes)
            {
                throw new InvalidDataException("The local results catalog exceeds its supported size.");
            }

            await using var stream = File.OpenRead(_catalogFilePath);
            var envelope = await JsonSerializer.DeserializeAsync<CatalogEnvelope>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (envelope is null ||
                envelope.SchemaVersion < MinimumReadableSchemaVersion ||
                envelope.SchemaVersion > CurrentSchemaVersion ||
                envelope.Entries is null)
            {
                throw new InvalidDataException("The local results catalog has an unsupported format.");
            }

            var entries = envelope.Entries.Select(SanitizeForStorage).ToArray();
            if (entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length ||
                entries.Length > CatalogLimits.MaximumEntryCount)
            {
                throw new InvalidDataException("The local results catalog contains invalid entries.");
            }

            return Array.AsReadOnly(entries);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The local results catalog is malformed and will not be used.");
            throw new InvalidDataException("The local results catalog is malformed.", exception);
        }
        catch (InvalidDataException exception)
        {
            _logger.LogWarning(exception, "The local results catalog has an unsupported format and will not be used.");
            throw;
        }
    }

    private async Task SaveCoreAsync(IReadOnlyList<CatalogEntry> entries, CancellationToken cancellationToken)
    {
        await AtomicJsonFile.WriteAsync(
            _catalogFilePath,
            new CatalogEnvelope(CurrentSchemaVersion, entries.ToArray()),
            JsonOptions,
            CatalogLimits.MaximumCatalogFileBytes,
            cancellationToken,
            static (_, _) => new CatalogCapacityExceededException(
                "The local results catalog exceeds its supported encoded size.")).ConfigureAwait(false);
    }

    private static CatalogEntry SanitizeForStorage(CatalogEntry entry)
    {
        if (entry is null ||
            !IsBoundedText(entry.Id, CatalogLimits.MaximumIdentifierLength) ||
            entry.SavedAtUtc.Offset != TimeSpan.Zero || entry.Snapshot is null)
        {
            throw new InvalidDataException("The catalog entry is invalid.");
        }

        var snapshot = entry.Snapshot;
        if (snapshot.SessionStartedAtUtc.Offset != TimeSpan.Zero || snapshot.ProjectedAtUtc.Offset != TimeSpan.Zero ||
            snapshot.Files is null || snapshot.Directories is null || snapshot.DuplicateGroups is null || snapshot.PlannedOperations is null ||
            snapshot.Issues is null || snapshot.Statistics is null || snapshot.Files.Count > CatalogLimits.MaximumFilesPerEntry)
        {
            throw new InvalidDataException("The catalog snapshot is invalid or exceeds the v0.4 file limit.");
        }

        if (snapshot.Files.Any(file => file is null) ||
            snapshot.Directories.Any(directory => directory is null) ||
            snapshot.DuplicateGroups.Any(group => group is null) ||
            snapshot.PlannedOperations.Any(operation => operation is null) ||
            snapshot.Issues.Any(issue => issue is null))
        {
            throw new InvalidDataException("The catalog snapshot contains invalid files.");
        }

        var fileIds = snapshot.Files.Select(file => file.Id).ToHashSet(StringComparer.Ordinal);
        if (fileIds.Count != snapshot.Files.Count ||
            !IsBoundedText(snapshot.SessionId, CatalogLimits.MaximumIdentifierLength) ||
            snapshot.Files.Any(file => !IsValidFile(file)) ||
            snapshot.Directories.Any(directory =>
                !IsBoundedText(directory.FullPath, CatalogLimits.MaximumStoredPathLength) ||
                !IsBoundedText(directory.DisplayName, CatalogLimits.MaximumDisplayTextLength)) ||
            snapshot.DuplicateGroups.Any(group => !IsValidDuplicateGroup(group, fileIds)) ||
            snapshot.DuplicateGroups.Select(group => group.GroupId).Distinct(StringComparer.Ordinal).Count() != snapshot.DuplicateGroups.Count ||
            snapshot.PlannedOperations.Any(operation => !IsValidPlannedOperation(operation, fileIds)) ||
            snapshot.Issues.Any(issue => !IsValidIssue(issue, fileIds)) ||
            !IsValidStatistics(snapshot.Statistics))
        {
            throw new InvalidDataException("The catalog snapshot contains invalid display data.");
        }

        var tags = (entry.AcceptedTags ?? Array.Empty<TagAssociation>())
            .Where(tag => tag is not null &&
                          tag.AcceptanceState == TagAcceptanceState.Accepted &&
                          tag.Source != TagSource.Deterministic &&
                          Enum.IsDefined(tag.Source) &&
                          Enum.IsDefined(tag.AcceptanceState) &&
                          fileIds.Contains(tag.FileId) &&
                          !string.IsNullOrWhiteSpace(tag.TagId) &&
                          !string.IsNullOrWhiteSpace(tag.DisplayName) &&
                          !string.IsNullOrWhiteSpace(tag.NormalizedValue) &&
                          !string.IsNullOrWhiteSpace(tag.Category) &&
                          tag.CreatedAtUtc.Offset == TimeSpan.Zero)
            .GroupBy(tag => tag.TagId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .OrderBy(tag => tag.FileId, StringComparer.Ordinal)
            .ThenBy(tag => tag.NormalizedValue, StringComparer.Ordinal)
            .ToArray();
        if (tags.Any(tag => tag.TagId.Length > 512 || tag.TagId.Any(char.IsControl) ||
                            tag.DisplayName.Length > UserTagLimits.MaximumTagLength || tag.DisplayName.Any(char.IsControl) ||
                            tag.NormalizedValue.Length > UserTagLimits.MaximumTagLength || tag.NormalizedValue.Any(char.IsControl) ||
                            tag.Category.Length > UserTagLimits.MaximumTagLength || tag.Category.Any(char.IsControl)) ||
            tags.GroupBy(tag => tag.FileId, StringComparer.Ordinal)
                .Any(group => group.Count() > UserTagLimits.MaximumAcceptedTagsPerFile))
        {
            throw new InvalidDataException("The catalog snapshot contains invalid or excessive accepted tags.");
        }

        var displayName = SanitizeDisplayName(entry.DisplayName);
        var sourceRoots = SanitizeSourceRoots(entry.SourceRoots);
        return entry with
        {
            Id = entry.Id.Trim(),
            Snapshot = CloneSnapshot(snapshot),
            AcceptedTags = Array.AsReadOnly(tags),
            DisplayName = displayName,
            SourceRoots = sourceRoots,
        };
    }

    private static string? SanitizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var normalized = displayName.Trim();
        if (normalized.Length > CatalogLimits.MaximumDisplayNameLength || normalized.Any(char.IsControl))
        {
            throw new InvalidDataException("The catalog display name is invalid.");
        }

        return normalized;
    }

    private static IReadOnlyList<string> SanitizeSourceRoots(IReadOnlyList<string>? sourceRoots)
    {
        sourceRoots ??= Array.Empty<string>();
        if (sourceRoots.Count > CatalogLimits.MaximumSourceRootCount)
        {
            throw new InvalidDataException("The catalog source scope exceeds its supported limit.");
        }

        var identities = new HashSet<string>(StringComparer.Ordinal);
        var sanitized = new List<string>(sourceRoots.Count);
        foreach (var sourceRoot in sourceRoots)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot))
            {
                throw new InvalidDataException("A catalog source root is invalid.");
            }

            var normalized = sourceRoot.Trim();
            if (normalized.Length > CatalogLimits.MaximumSourceRootLength ||
                normalized.Any(char.IsControl) ||
                !CatalogPathIdentity.IsAbsolute(normalized))
            {
                throw new InvalidDataException("A catalog source root is invalid.");
            }

            if (identities.Add(CatalogPathIdentity.Normalize(normalized)))
            {
                sanitized.Add(normalized);
            }
        }

        return Array.AsReadOnly(sanitized.ToArray());
    }

    private static bool IsValidFile(ResultFile file) =>
        IsBoundedText(file.Id, CatalogLimits.MaximumIdentifierLength) &&
        IsBoundedText(file.FullPath, CatalogLimits.MaximumStoredPathLength) &&
        IsBoundedText(file.DisplayFileName, CatalogLimits.MaximumDisplayTextLength) &&
        file.NormalizedExtension is not null &&
        file.NormalizedExtension.Length <= 64 &&
        !file.NormalizedExtension.Any(char.IsControl) &&
        file.SizeInBytes is null or >= 0 &&
        (file.LastWriteTimeUtc is not { } lastWriteTimeUtc || lastWriteTimeUtc.Offset == TimeSpan.Zero) &&
        (file.CreationTimeUtc is not { } creationTimeUtc || creationTimeUtc.Offset == TimeSpan.Zero) &&
        (file.Category is null || Enum.IsDefined(file.Category.Value)) &&
        IsBoundedText(file.ClassificationDisplay, CatalogLimits.MaximumDisplayTextLength) &&
        Enum.IsDefined(file.DuplicateStatus) &&
        IsOptionalBoundedText(file.DuplicateGroupId, CatalogLimits.MaximumIdentifierLength);

    private static bool IsValidDuplicateGroup(ResultDuplicateGroup group, IReadOnlySet<string> fileIds) =>
        IsBoundedText(group.GroupId, CatalogLimits.MaximumIdentifierLength) &&
        group.Ordinal > 0 &&
        group.MemberFileIds is not null &&
        group.MemberFileIds.Count >= 2 &&
        group.MemberCount == group.MemberFileIds.Count &&
        group.MemberFileIds.All(fileId => IsBoundedText(fileId, CatalogLimits.MaximumIdentifierLength) && fileIds.Contains(fileId)) &&
        group.MemberFileIds.Distinct(StringComparer.Ordinal).Count() == group.MemberFileIds.Count &&
        group.CommonFileSizeInBytes is null or >= 0 &&
        group.PotentialReclaimableBytes is null or >= 0;

    private static bool IsValidPlannedOperation(ResultPlannedOperation operation, IReadOnlySet<string> fileIds) =>
        IsBoundedText(operation.OperationId, CatalogLimits.MaximumIdentifierLength) &&
        Enum.IsDefined(operation.Kind) &&
        (operation.SourceFileId is null ||
         IsBoundedText(operation.SourceFileId, CatalogLimits.MaximumIdentifierLength) && fileIds.Contains(operation.SourceFileId)) &&
        IsOptionalBoundedText(operation.DestinationPath, CatalogLimits.MaximumStoredPathLength) &&
        IsOptionalBoundedText(operation.RuleDisplayName, CatalogLimits.MaximumDisplayTextLength);

    private static bool IsValidIssue(ResultIssue issue, IReadOnlySet<string> fileIds) =>
        IsBoundedText(issue.SourceStage, CatalogLimits.MaximumDisplayTextLength) &&
        Enum.IsDefined(issue.Severity) &&
        IsBoundedText(issue.Message, CatalogLimits.MaximumDisplayTextLength) &&
        (issue.AssociatedFileId is null ||
         IsBoundedText(issue.AssociatedFileId, CatalogLimits.MaximumIdentifierLength) && fileIds.Contains(issue.AssociatedFileId));

    private static bool IsValidStatistics(ResultsSnapshotStatistics statistics) =>
        statistics.FilesDiscovered >= 0 &&
        statistics.DirectoriesDiscovered >= 0 &&
        statistics.ExactDuplicateGroupCount >= 0 &&
        statistics.ExactDuplicateFileCount >= 0 &&
        statistics.PlannedOperationCount >= 0 &&
        statistics.WarningCount >= 0 &&
        statistics.ErrorCount >= 0;

    private static bool IsBoundedText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsControl);

    private static bool IsOptionalBoundedText(string? value, int maximumLength) =>
        value is null || IsBoundedText(value, maximumLength);

    private static ResultsSnapshot CloneSnapshot(ResultsSnapshot snapshot) => new(
        snapshot.SessionId,
        snapshot.SessionStartedAtUtc,
        snapshot.ProjectedAtUtc,
        Array.AsReadOnly(snapshot.Files.ToArray()),
        Array.AsReadOnly(snapshot.Directories.ToArray()),
        Array.AsReadOnly(snapshot.DuplicateGroups.ToArray()),
        Array.AsReadOnly(snapshot.PlannedOperations.ToArray()),
        Array.AsReadOnly(snapshot.Issues.ToArray()),
        snapshot.Statistics,
        snapshot.IsDuplicateDataAvailable)
    {
        Workflow = snapshot.Workflow,
    };

    private static CatalogEntrySummary ToSummary(CatalogEntry entry) => new(
        entry.Id,
        entry.SavedAtUtc,
        entry.Snapshot.Statistics.FilesDiscovered,
        entry.Snapshot.Statistics.DirectoriesDiscovered,
        entry.Snapshot.Statistics.WarningCount + entry.Snapshot.Statistics.ErrorCount,
        entry.Snapshot.Statistics.ExactDuplicateGroupCount)
    {
        DisplayName = entry.DisplayName,
        SourceRoots = Array.AsReadOnly(entry.SourceRoots.ToArray()),
    };

    private sealed record CatalogEnvelope(int SchemaVersion, IReadOnlyList<CatalogEntry>? Entries);
}
