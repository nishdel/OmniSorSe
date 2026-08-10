using System.Globalization;
using OpenSorSe.Core.Diagnostics;

namespace OpenSorSe.Application.Indexing;

public sealed partial class BackgroundIndexingService
{
    /// <inheritdoc />
    public Task<IndexPrivacyItem?> InspectFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        return PrivacyStore.InspectFileAsync(fileId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexPrivacyItem>> InspectSourceAsync(
        string sourceId,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (maximumCount is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        return PrivacyStore.InspectSourceAsync(sourceId, maximumCount, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IndexPrivacyOperationResult> ForgetFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        await StopFileInProcessWorkAsync(fileId, cancellationToken).ConfigureAwait(false);
        var result = await PrivacyStore
            .ForgetFileAsync(fileId, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        ReportPrivacyOperation("Forget indexed file", result);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async Task<IndexPrivacyOperationResult> ForgetSourceAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        await StopSourceInProcessWorkAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var result = await PrivacyStore
            .ForgetSourceAsync(sourceId, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        ReportPrivacyOperation("Forget indexed source", result);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async Task<IndexPrivacyOperationResult> SetFilePolicyAsync(
        string fileId,
        IndexPrivacyPolicyChange change,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        ArgumentNullException.ThrowIfNull(change);
        await StopFileInProcessWorkAsync(fileId, cancellationToken).ConfigureAwait(false);
        var result = await PrivacyStore
            .SetFilePolicyAsync(fileId, change, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        if (result.Applied && result.SourceId is not null)
        {
            await QueueExistingSourceAsync(result.SourceId, cancellationToken).ConfigureAwait(false);
        }

        ReportPrivacyOperation("Update indexed-file privacy policy", result);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async Task<IndexPrivacyOperationResult> ClearFileDataAsync(
        string fileId,
        IndexedDataKind data,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        if (data == IndexedDataKind.None || (data & ~IndexedDataKind.AllDerived) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        await StopFileInProcessWorkAsync(fileId, cancellationToken).ConfigureAwait(false);
        var result = await PrivacyStore
            .ClearFileDataAsync(fileId, data, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        ReportPrivacyOperation("Clear selected generated index data", result);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async Task<IndexPrivacyOperationResult> RepairFileAsync(
        string fileId,
        IndexRepairKind repair,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);
        if (!Enum.IsDefined(repair))
        {
            throw new ArgumentOutOfRangeException(nameof(repair));
        }

        await StopFileInProcessWorkAsync(fileId, cancellationToken).ConfigureAwait(false);
        if (repair == IndexRepairKind.Verify)
        {
            var item = await PrivacyStore.InspectFileAsync(fileId, cancellationToken).ConfigureAwait(false);
            if (item is null)
            {
                return MissingItemResult();
            }

            var sourcePath = _pathSemantics.NormalizeAbsolutePath(item.SourceRootPath);
            var fullPath = _pathSemantics.NormalizeAbsolutePath(
                Path.Combine(sourcePath, item.RelativePath));
            if (!_pathSemantics.IsWithinRoot(sourcePath, fullPath) || !File.Exists(fullPath))
            {
                return new IndexPrivacyOperationResult(
                    false,
                    item.SourceId,
                    0,
                    "The source file is unavailable. Its original location was not changed; refresh the source after it becomes accessible.");
            }
        }

        var result = await PrivacyStore
            .PrepareFileRepairAsync(fileId, repair, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        if (result.Applied && result.SourceId is not null)
        {
            await QueueExistingSourceAsync(result.SourceId, cancellationToken).ConfigureAwait(false);
        }

        ReportPrivacyOperation($"Prepare {repair} file repair", result);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async Task<IndexPrivacyOperationResult> RepairSourceAsync(
        string sourceId,
        IndexRepairKind repair,
        CancellationToken cancellationToken = default)
    {
        EnsurePrivacyAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (!Enum.IsDefined(repair))
        {
            throw new ArgumentOutOfRangeException(nameof(repair));
        }

        await StopSourceInProcessWorkAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var result = await PrivacyStore
            .PrepareSourceRepairAsync(sourceId, repair, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        if (result.Applied)
        {
            await QueueExistingSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        }

        ReportPrivacyOperation($"Prepare {repair} source repair", result);
        await PublishProgressAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private IIndexPrivacyStore PrivacyStore =>
        _deepIndexStore as IIndexPrivacyStore ??
        throw new NotSupportedException(
            "The configured background-index provider does not support indexed-data inspection and selective repair.");

    private void EnsurePrivacyAvailable()
    {
        EnsureInitialized();
        ThrowIfStorageUnavailable();
        _ = PrivacyStore;
    }

    private async Task StopFileInProcessWorkAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        var active = _activeStages.Values
            .Where(item => string.Equals(item.WorkItem.FileId, fileId, StringComparison.Ordinal))
            .ToArray();
        foreach (var item in active)
        {
            RequestCancellation(item.Cancellation);
        }

        await AwaitActiveStagesAsync(active, cancellationToken).ConfigureAwait(false);
    }

    private async Task QueueExistingSourceAsync(
        string sourceId,
        CancellationToken cancellationToken)
    {
        var sources = await _deepIndexStore.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        var source = sources.SingleOrDefault(item =>
            string.Equals(item.Id, sourceId, StringComparison.Ordinal));
        if (source is { Enabled: true } && Directory.Exists(source.RootPath))
        {
            _ = await QueueSourceAsync(source, cancellationToken).ConfigureAwait(false);
            Signal(_workers.Count);
        }
    }

    private void ReportPrivacyOperation(
        string operation,
        IndexPrivacyOperationResult result)
    {
        _diagnostics?.Publish(
            null,
            operation,
            result.Applied ? DiagnosticStatus.Succeeded : DiagnosticStatus.PartiallySucceeded,
            result.Applied ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
            DiagnosticSection.Overview,
            result.Message,
            [
                new DiagnosticField(
                    "Affected file count",
                    result.AffectedFileCount.ToString(CultureInfo.InvariantCulture)),
            ]);
    }

    private static IndexPrivacyOperationResult MissingItemResult() => new(
        false,
        null,
        0,
        "The selected indexed file no longer exists.");
}
