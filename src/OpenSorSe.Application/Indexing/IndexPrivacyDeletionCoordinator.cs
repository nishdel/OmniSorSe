using OpenSorSe.Application.Content;
using OpenSorSe.Application.Media;
using OpenSorSe.Application.Semantic;

namespace OpenSorSe.Application.Indexing;

/// <summary>
/// Coordinates authoritative schema-6 privacy deletion with rebuildable compatibility caches.
/// Source files are never changed. Legacy path-keyed stores are cleared wholesale because they are
/// rebuildable and cannot prove complete stable-ID ownership after a rename or source removal.
/// </summary>
public sealed class IndexPrivacyDeletionCoordinator : IIndexPrivacyService
{
    private readonly IContentStore _contentStore;
    private readonly IIndexPrivacyService _inner;
    private readonly IMediaThumbnailProvider _thumbnailProvider;
    private readonly ISemanticIndexStore _semanticIndexStore;

    /// <summary>Initializes the privacy-deletion coordinator.</summary>
    public IndexPrivacyDeletionCoordinator(
        IIndexPrivacyService inner,
        IContentStore contentStore,
        ISemanticIndexStore semanticIndexStore,
        IMediaThumbnailProvider thumbnailProvider)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
        _semanticIndexStore = semanticIndexStore ?? throw new ArgumentNullException(nameof(semanticIndexStore));
        _thumbnailProvider = thumbnailProvider ?? throw new ArgumentNullException(nameof(thumbnailProvider));
    }

    /// <inheritdoc />
    public Task<IndexPrivacyItem?> InspectFileAsync(string fileId, CancellationToken cancellationToken = default) =>
        _inner.InspectFileAsync(fileId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<IndexPrivacyItem>> InspectSourceAsync(
        string sourceId,
        int maximumCount,
        CancellationToken cancellationToken = default) =>
        _inner.InspectSourceAsync(sourceId, maximumCount, cancellationToken);

    /// <inheritdoc />
    public async Task<IndexPrivacyOperationResult> ForgetFileAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.ForgetFileAsync(fileId, cancellationToken).ConfigureAwait(false);
        // The schema-6 deletion can already have completed during an attempt whose
        // compatibility cleanup later failed. Always retry the rebuildable cleanup
        // so Forget remains complete and idempotent.
        await ClearRebuildableCompatibilityStateAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    public async Task<IndexPrivacyOperationResult> ForgetSourceAsync(
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        var result = await _inner.ForgetSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        await ClearRebuildableCompatibilityStateAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    public Task<IndexPrivacyOperationResult> SetFilePolicyAsync(
        string fileId,
        IndexPrivacyPolicyChange change,
        CancellationToken cancellationToken = default) =>
        _inner.SetFilePolicyAsync(fileId, change, cancellationToken);

    /// <inheritdoc />
    public Task<IndexPrivacyOperationResult> ClearFileDataAsync(
        string fileId,
        IndexedDataKind data,
        CancellationToken cancellationToken = default) =>
        _inner.ClearFileDataAsync(fileId, data, cancellationToken);

    /// <inheritdoc />
    public Task<IndexPrivacyOperationResult> RepairFileAsync(
        string fileId,
        IndexRepairKind repair,
        CancellationToken cancellationToken = default) =>
        _inner.RepairFileAsync(fileId, repair, cancellationToken);

    /// <inheritdoc />
    public Task<IndexPrivacyOperationResult> RepairSourceAsync(
        string sourceId,
        IndexRepairKind repair,
        CancellationToken cancellationToken = default) =>
        _inner.RepairSourceAsync(sourceId, repair, cancellationToken);

    private async Task ClearRebuildableCompatibilityStateAsync(CancellationToken cancellationToken)
    {
        await _contentStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        await _semanticIndexStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        await _thumbnailProvider.ClearAsync(cancellationToken).ConfigureAwait(false);
    }
}
