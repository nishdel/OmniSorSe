using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Relationships;

namespace OpenSorSe.Desktop.ViewModels;

/// <summary>Presents evidence-backed virtual collections and direct file relationships.</summary>
public sealed class CollectionsViewModel : ViewModelBase, IDisposable
{
    private readonly IRelationshipService? _service;
    private readonly ObservableCollection<SmartCollection> _collections = [];
    private readonly ObservableCollection<SmartCollectionMember> _members = [];
    private readonly ObservableCollection<FileRelationship> _relationships = [];
    private readonly ObservableCollection<CollectionTimelineEvent> _timeline = [];
    private readonly ObservableCollection<RelationshipFileDocument> _files = [];
    private readonly ObservableCollection<RelatedFile> _relatedFiles = [];
    private readonly ObservableCollection<RelationshipPairCorrection> _corrections = [];
    private CancellationTokenSource? _operation;
    private SmartCollection? _selectedCollection;
    private SmartCollection? _mergeCollection;
    private SmartCollectionMember? _selectedMember;
    private FileRelationship? _selectedRelationship;
    private RelationshipFileDocument? _selectedFile;
    private RelatedFile? _selectedRelatedFile;
    private RelationshipPairCorrection? _selectedCorrection;
    private RelationshipFileDocument? _firstLinkFile;
    private RelationshipFileDocument? _secondLinkFile;
    private RelationshipType _linkType = RelationshipType.Manual;
    private string? _customType;
    private bool _alwaysRelate;
    private string _renameText = string.Empty;
    private bool _isBusy;
    private PendingRelationshipMutation? _pendingDestructiveMutation;
    private string _statusText = "Relationship data has not been inspected.";
    private string _diagnosticsText = "Relationship diagnostics have not been inspected.";
    private RelationshipType? _relationshipFilter;
    private RelationshipConfidence? _minimumConfidence;
    private RelatedFileSort _relatedFileSort = RelatedFileSort.Confidence;
    private int _selectedSectionIndex = 1;

    /// <summary>Initializes a preview instance.</summary>
    public CollectionsViewModel()
        : this(null)
    {
    }

    /// <summary>Initializes the relationship and Smart Collection presentation.</summary>
    public CollectionsViewModel(IRelationshipService? service)
    {
        _service = service;
        Collections = new ReadOnlyObservableCollection<SmartCollection>(_collections);
        Members = new ReadOnlyObservableCollection<SmartCollectionMember>(_members);
        Relationships = new ReadOnlyObservableCollection<FileRelationship>(_relationships);
        Timeline = new ReadOnlyObservableCollection<CollectionTimelineEvent>(_timeline);
        Files = new ReadOnlyObservableCollection<RelationshipFileDocument>(_files);
        RelatedFiles = new ReadOnlyObservableCollection<RelatedFile>(_relatedFiles);
        Corrections = new ReadOnlyObservableCollection<RelationshipPairCorrection>(_corrections);
        RelationshipTypes = Enum.GetValues<RelationshipType>();
        ConfidenceLevels = Enum.GetValues<RelationshipConfidence>();
        RelatedFileSorts = Enum.GetValues<RelatedFileSort>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => _service is not null && !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        LinkFilesCommand = new AsyncRelayCommand(LinkFilesAsync, CanLinkFiles);
        UnlinkCommand = new AsyncRelayCommand(UnlinkAsync, () => SelectedRelationship is not null && !IsBusy && !IsDestructiveConfirmationPending);
        ConfirmRelationshipCommand = new AsyncRelayCommand(
            () => SetDecisionAsync(RelationshipDecision.Confirmed),
            () => SelectedRelationship is not null && !IsBusy && !IsDestructiveConfirmationPending);
        AlwaysRelateRelationshipCommand = new AsyncRelayCommand(
            () => SetDecisionAsync(RelationshipDecision.AlwaysRelate),
            () => SelectedRelationship is not null && !IsBusy && !IsDestructiveConfirmationPending);
        RejectRelationshipCommand = new AsyncRelayCommand(
            () => SetDecisionAsync(RelationshipDecision.Rejected),
            () => SelectedRelationship is not null && !IsBusy && !IsDestructiveConfirmationPending);
        NeverRelateCommand = new AsyncRelayCommand(
            () => SetDecisionAsync(RelationshipDecision.NeverRelate),
            () => SelectedRelationship is not null && !IsBusy && !IsDestructiveConfirmationPending);
        RenameCollectionCommand = new AsyncRelayCommand(RenameCollectionAsync, CanRenameCollection);
        TogglePinCommand = new AsyncRelayCommand(TogglePinAsync, () => SelectedCollection is not null && !IsBusy && !IsDestructiveConfirmationPending);
        MergeCollectionCommand = new AsyncRelayCommand(MergeCollectionAsync, CanMergeCollection);
        SplitMemberCommand = new AsyncRelayCommand(SplitMemberAsync, () => SelectedCollection is not null && SelectedMember is not null && !IsBusy && !IsDestructiveConfirmationPending);
        RequestForgetCollectionCommand = new RelayCommand(RequestForgetCollection, () => SelectedCollection is not null && !IsBusy && !IsDestructiveConfirmationPending);
        ConfirmForgetCollectionCommand = new AsyncRelayCommand(ConfirmDestructiveMutationAsync, () => IsForgetCollectionPending && !IsBusy);
        CancelForgetCollectionCommand = new RelayCommand(CancelDestructiveConfirmation, () => IsForgetCollectionPending && !IsBusy);
        RefreshRelatedFilesCommand = new AsyncRelayCommand(RefreshRelatedFilesAsync, () => SelectedFile is not null && !IsBusy);
        ForgetFileRelationshipsCommand = new AsyncRelayCommand(
            () => ForgetFileRelationshipsAsync(excludeFuture: true),
            () => SelectedFile is not null && !IsBusy && !IsDestructiveConfirmationPending);
        ForgetSourceRelationshipsCommand = new AsyncRelayCommand(
            ForgetSourceRelationshipsAsync,
            () => SelectedFile is not null && !IsBusy && !IsDestructiveConfirmationPending);
        RebuildFileRelationshipsCommand = new AsyncRelayCommand(RebuildFileRelationshipsAsync, () => SelectedFile is not null && !IsBusy && !IsDestructiveConfirmationPending);
        MarkRelatedCommand = new AsyncRelayCommand(
            () => SetRelatedFileDecisionAsync(RelationshipDecision.AlwaysRelate),
            () => SelectedRelatedFile is not null && !IsBusy && !IsDestructiveConfirmationPending);
        MarkNotRelatedCommand = new AsyncRelayCommand(
            () => SetRelatedFileDecisionAsync(RelationshipDecision.NeverRelate),
            () => SelectedRelatedFile is not null && !IsBusy && !IsDestructiveConfirmationPending);
        UseAutomaticCommand = new AsyncRelayCommand(
            UseAutomaticAsync,
            () => SelectedFile is not null && SelectedRelatedFile is not null && !IsBusy && !IsDestructiveConfirmationPending);
        UseAutomaticCorrectionCommand = new AsyncRelayCommand(
            UseAutomaticCorrectionAsync,
            () => SelectedCorrection is not null && !IsBusy && !IsDestructiveConfirmationPending);
        RepairCommand = new AsyncRelayCommand(RepairAsync, () => _service is not null && !IsBusy && !IsDestructiveConfirmationPending);
        ConfirmDestructiveActionCommand = new AsyncRelayCommand(
            ConfirmDestructiveMutationAsync,
            () => IsDestructiveConfirmationPending && !IsBusy);
        CancelDestructiveActionCommand = new RelayCommand(
            CancelDestructiveConfirmation,
            () => IsDestructiveConfirmationPending && !IsBusy);
    }

    /// <summary>Gets bounded virtual collections.</summary>
    public ReadOnlyObservableCollection<SmartCollection> Collections { get; }

    /// <summary>Gets members of the selected virtual collection.</summary>
    public ReadOnlyObservableCollection<SmartCollectionMember> Members { get; }

    /// <summary>Gets evidence-backed relationships within the selected collection.</summary>
    public ReadOnlyObservableCollection<FileRelationship> Relationships { get; }

    /// <summary>Gets indexed-timestamp timeline events for the selected collection.</summary>
    public ReadOnlyObservableCollection<CollectionTimelineEvent> Timeline { get; }

    /// <summary>Gets bounded indexed files available for explicit user controls.</summary>
    public ReadOnlyObservableCollection<RelationshipFileDocument> Files { get; }

    /// <summary>Gets direct related files for the selected file.</summary>
    public ReadOnlyObservableCollection<RelatedFile> RelatedFiles { get; }

    /// <summary>Gets bounded explicit pair corrections involving the selected file.</summary>
    public ReadOnlyObservableCollection<RelationshipPairCorrection> Corrections { get; }

    /// <summary>Gets available relationship categories.</summary>
    public IReadOnlyList<RelationshipType> RelationshipTypes { get; }

    /// <summary>Gets available minimum confidence levels.</summary>
    public IReadOnlyList<RelationshipConfidence> ConfidenceLevels { get; }

    /// <summary>Gets available Related Files sort orders.</summary>
    public IReadOnlyList<RelatedFileSort> RelatedFileSorts { get; }

    /// <summary>Gets or sets the visible section; direct navigation defaults to Related Files.</summary>
    public int SelectedSectionIndex
    {
        get => _selectedSectionIndex;
        set => SetProperty(ref _selectedSectionIndex, value is 0 or 1 ? value : 1);
    }

    /// <summary>Gets or sets the inspected collection.</summary>
    public SmartCollection? SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            if (SetProperty(ref _selectedCollection, value))
            {
                RenameText = value?.Title ?? string.Empty;
                CancelDestructiveConfirmation();
                NotifyCommands();
                _ = LoadCollectionAsync();
            }
        }
    }

    /// <summary>Gets or sets the collection merged into the selected target.</summary>
    public SmartCollection? MergeCollection
    {
        get => _mergeCollection;
        set
        {
            if (SetProperty(ref _mergeCollection, value))
            {
                MergeCollectionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the selected collection member.</summary>
    public SmartCollectionMember? SelectedMember
    {
        get => _selectedMember;
        set
        {
            if (SetProperty(ref _selectedMember, value))
            {
                SplitMemberCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the inspected relationship.</summary>
    public FileRelationship? SelectedRelationship
    {
        get => _selectedRelationship;
        set
        {
            if (SetProperty(ref _selectedRelationship, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets or sets the file whose direct relationships are listed.</summary>
    public RelationshipFileDocument? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
            {
                NotifyCommands();
                _ = RefreshRelatedFilesAsync();
            }
        }
    }

    /// <summary>Gets or sets the direct pair selected for user authority.</summary>
    public RelatedFile? SelectedRelatedFile
    {
        get => _selectedRelatedFile;
        set
        {
            if (SetProperty(ref _selectedRelatedFile, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets or sets a visible explicit correction, including hidden negative authority.</summary>
    public RelationshipPairCorrection? SelectedCorrection
    {
        get => _selectedCorrection;
        set
        {
            if (SetProperty(ref _selectedCorrection, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets or sets the first file for an explicit link.</summary>
    public RelationshipFileDocument? FirstLinkFile
    {
        get => _firstLinkFile;
        set
        {
            if (SetProperty(ref _firstLinkFile, value))
            {
                LinkFilesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the second file for an explicit link.</summary>
    public RelationshipFileDocument? SecondLinkFile
    {
        get => _secondLinkFile;
        set
        {
            if (SetProperty(ref _secondLinkFile, value))
            {
                LinkFilesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets the explicit link category.</summary>
    public RelationshipType LinkType
    {
        get => _linkType;
        set
        {
            if (SetProperty(ref _linkType, value))
            {
                OnPropertyChanged(nameof(IsCustomLinkType));
                LinkFilesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets whether a bounded custom category name is required.</summary>
    public bool IsCustomLinkType => LinkType == RelationshipType.Custom;

    /// <summary>Gets or sets a bounded custom relationship name.</summary>
    public string? CustomType
    {
        get => _customType;
        set
        {
            if (SetProperty(ref _customType, value))
            {
                LinkFilesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets whether a manual link persists when automatic evidence changes.</summary>
    public bool AlwaysRelate
    {
        get => _alwaysRelate;
        set => SetProperty(ref _alwaysRelate, value);
    }

    /// <summary>Gets or sets a collection title edit.</summary>
    public string RenameText
    {
        get => _renameText;
        set
        {
            if (SetProperty(ref _renameText, value ?? string.Empty))
            {
                RenameCollectionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>Gets or sets a Related Files category filter.</summary>
    public RelationshipType? RelationshipFilter
    {
        get => _relationshipFilter;
        set => SetProperty(ref _relationshipFilter, value);
    }

    /// <summary>Gets or sets a Related Files confidence filter.</summary>
    public RelationshipConfidence? MinimumConfidence
    {
        get => _minimumConfidence;
        set => SetProperty(ref _minimumConfidence, value);
    }

    /// <summary>Gets or sets the Related Files sort.</summary>
    public RelatedFileSort RelatedFileSort
    {
        get => _relatedFileSort;
        set => SetProperty(ref _relatedFileSort, value);
    }

    /// <summary>Gets whether an operation is active.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    /// <summary>Gets whether collection forgetting awaits confirmation.</summary>
    public bool IsForgetCollectionPending =>
        _pendingDestructiveMutation?.Kind == RelationshipMutationKind.ForgetCollection;

    /// <summary>Gets whether any authority-removing relationship operation awaits confirmation.</summary>
    public bool IsDestructiveConfirmationPending => _pendingDestructiveMutation is not null;

    /// <summary>Gets an immutable-target description of the pending relationship operation.</summary>
    public string DestructiveConfirmationText => _pendingDestructiveMutation?.Description ?? string.Empty;

    /// <summary>Gets the latest plain-language operation status.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Gets privacy-safe aggregate relationship diagnostics.</summary>
    public string DiagnosticsText
    {
        get => _diagnosticsText;
        private set => SetProperty(ref _diagnosticsText, value);
    }

    /// <summary>Gets the refresh command.</summary>
    public IAsyncRelayCommand RefreshCommand { get; }
    /// <summary>Gets the cooperative cancellation command.</summary>
    public IRelayCommand CancelCommand { get; }
    /// <summary>Gets the explicit manual-link command.</summary>
    public IAsyncRelayCommand LinkFilesCommand { get; }
    /// <summary>Gets the selected-relationship unlink command.</summary>
    public IAsyncRelayCommand UnlinkCommand { get; }
    /// <summary>Gets the selected-relationship confirmation command.</summary>
    public IAsyncRelayCommand ConfirmRelationshipCommand { get; }
    /// <summary>Gets the persistent always-relate correction command.</summary>
    public IAsyncRelayCommand AlwaysRelateRelationshipCommand { get; }
    /// <summary>Gets the selected-suggestion rejection command.</summary>
    public IAsyncRelayCommand RejectRelationshipCommand { get; }
    /// <summary>Gets the persistent never-relate command.</summary>
    public IAsyncRelayCommand NeverRelateCommand { get; }
    /// <summary>Gets the virtual collection rename command.</summary>
    public IAsyncRelayCommand RenameCollectionCommand { get; }
    /// <summary>Gets the virtual collection pin toggle command.</summary>
    public IAsyncRelayCommand TogglePinCommand { get; }
    /// <summary>Gets the virtual collection merge command.</summary>
    public IAsyncRelayCommand MergeCollectionCommand { get; }
    /// <summary>Gets the selected-member split command.</summary>
    public IAsyncRelayCommand SplitMemberCommand { get; }
    /// <summary>Gets the collection-forget confirmation request.</summary>
    public IRelayCommand RequestForgetCollectionCommand { get; }
    /// <summary>Gets the confirmed collection-forget command.</summary>
    public IAsyncRelayCommand ConfirmForgetCollectionCommand { get; }
    /// <summary>Gets the collection-forget cancellation command.</summary>
    public IRelayCommand CancelForgetCollectionCommand { get; }
    /// <summary>Gets the Related Files refresh command.</summary>
    public IAsyncRelayCommand RefreshRelatedFilesCommand { get; }
    /// <summary>Gets the index-only file relationship forget and exclusion command.</summary>
    public IAsyncRelayCommand ForgetFileRelationshipsCommand { get; }
    /// <summary>Gets the index-only selected-source relationship forget and exclusion command.</summary>
    public IAsyncRelayCommand ForgetSourceRelationshipsCommand { get; }
    /// <summary>Gets the targeted file relationship rebuild command.</summary>
    public IAsyncRelayCommand RebuildFileRelationshipsCommand { get; }
    /// <summary>Gets the explicit Related pair-authority command.</summary>
    public IAsyncRelayCommand MarkRelatedCommand { get; }
    /// <summary>Gets the explicit Not Related pair-authority command.</summary>
    public IAsyncRelayCommand MarkNotRelatedCommand { get; }
    /// <summary>Gets the selected pair automatic-evidence reset command.</summary>
    public IAsyncRelayCommand UseAutomaticCommand { get; }
    /// <summary>Gets the hidden/visible correction automatic-evidence reset command.</summary>
    public IAsyncRelayCommand UseAutomaticCorrectionCommand { get; }
    /// <summary>Gets the relationship storage repair command.</summary>
    public IAsyncRelayCommand RepairCommand { get; }
    /// <summary>Gets the universal confirmation command for authority-removing relationship operations.</summary>
    public IAsyncRelayCommand ConfirmDestructiveActionCommand { get; }
    /// <summary>Gets the universal cancellation command for authority-removing relationship operations.</summary>
    public IRelayCommand CancelDestructiveActionCommand { get; }

    /// <summary>Refreshes files, collections, diagnostics, and the current inspection.</summary>
    public async Task RefreshAsync()
    {
        if (_service is null || IsBusy)
        {
            StatusText = "Relationship analysis is unavailable in this application configuration.";
            return;
        }

        using var operation = BeginOperation();
        try
        {
            var collections = await _service.GetCollectionsAsync(cancellationToken: operation.Token);
            var files = await _service.GetFilesAsync(cancellationToken: operation.Token);
            var diagnostics = await _service.GetDiagnosticsAsync(operation.Token);
            Replace(_collections, collections);
            Replace(_files, files);
            SelectedCollection = PreserveSelection(_collections, SelectedCollection?.Id);
            SelectedFile = PreserveFileSelection(_files, SelectedFile?.FileId);
            DiagnosticsText =
                $"{diagnostics.RelationshipCount:N0} relationships, {diagnostics.CollectionCount:N0} collections, " +
                $"{diagnostics.EvidenceCount:N0} evidence records, {diagnostics.ManualOverrideCount:N0} user corrections. " +
                $"Excluded files: {diagnostics.ExcludedFileCount:N0}. Last pass: {diagnostics.LastCandidateCount:N0} candidates, " +
                $"{diagnostics.LastGeneratedRelationshipCount:N0} relationships, {diagnostics.LastGeneratedCollectionCount:N0} collections" +
                (diagnostics.LastAnalysisDuration is { } duration ? $" in {duration.TotalMilliseconds:N0} ms" : string.Empty) +
                $". Algorithm {diagnostics.AlgorithmVersion}; stale files {diagnostics.StaleRelationshipFileCount:N0}; " +
                $"repairs {diagnostics.RepairOperationCount:N0}.";
            StatusText = collections.Count == 0
                ? "No evidence-backed Smart Collections are available yet. Background analysis is incremental."
                : $"Loaded {collections.Count:N0} virtual collections. Original files have not been moved.";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            StatusText = "Relationship refresh was cancelled.";
        }
        catch (Exception)
        {
            StatusText = "Relationship data could not be refreshed. Original files were not affected.";
        }
        finally
        {
            EndOperation(operation);
        }

        if (SelectedCollection is not null)
        {
            await LoadCollectionAsync();
        }

        if (SelectedFile is not null)
        {
            await RefreshRelatedFilesAsync();
        }
    }

    private async Task LoadCollectionAsync()
    {
        if (_service is null || SelectedCollection is null || IsBusy)
        {
            Replace(_members, []);
            Replace(_relationships, []);
            Replace(_timeline, []);
            return;
        }

        var selectedId = SelectedCollection.Id;
        using var operation = BeginOperation();
        try
        {
            var details = await _service.GetCollectionAsync(selectedId, operation.Token);
            if (details is null || !string.Equals(SelectedCollection?.Id, selectedId, StringComparison.Ordinal))
            {
                return;
            }

            Replace(_members, details.Members);
            Replace(_relationships, details.Relationships);
            Replace(_timeline, details.Timeline);
            StatusText = $"{details.Collection.Title}: {details.Members.Count:N0} members, {details.Relationships.Count:N0} evidence-backed relationships.";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            StatusText = "Collection inspection was cancelled.";
        }
        catch (Exception)
        {
            StatusText = "The selected virtual collection could not be inspected safely.";
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private async Task RefreshRelatedFilesAsync()
    {
        if (_service is null || SelectedFile is null || IsBusy)
        {
            Replace(_relatedFiles, []);
            Replace(_corrections, []);
            return;
        }

        var fileId = SelectedFile.FileId;
        using var operation = BeginOperation();
        try
        {
            var related = await _service.GetRelatedFilesAsync(
                fileId,
                RelationshipFilter,
                MinimumConfidence,
                RelatedFileSort,
                cancellationToken: operation.Token);
            var corrections = await _service.GetCorrectionsAsync(fileId, cancellationToken: operation.Token);
            if (string.Equals(SelectedFile?.FileId, fileId, StringComparison.Ordinal))
            {
                Replace(_relatedFiles, related);
                Replace(_corrections, corrections);
                SelectedRelatedFile = _relatedFiles.FirstOrDefault(item =>
                    string.Equals(item.FileId, SelectedRelatedFile?.FileId, StringComparison.Ordinal));
                SelectedCorrection = _corrections.FirstOrDefault(item =>
                    string.Equals(item.FirstFileId, SelectedCorrection?.FirstFileId, StringComparison.Ordinal) &&
                    string.Equals(item.SecondFileId, SelectedCorrection?.SecondFileId, StringComparison.Ordinal));
                StatusText = related.Count == 0
                    ? "No retained direct relationships match the current filters."
                    : $"Loaded {related.Count:N0} direct related files with retained evidence.";
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            StatusText = "Related Files refresh was cancelled.";
        }
        catch (Exception)
        {
            StatusText = "Related Files could not be inspected safely.";
        }
        finally
        {
            EndOperation(operation);
        }
    }

    private bool CanLinkFiles() =>
        !IsBusy &&
        !IsDestructiveConfirmationPending &&
        FirstLinkFile is not null &&
        SecondLinkFile is not null &&
        !string.Equals(FirstLinkFile.FileId, SecondLinkFile.FileId, StringComparison.Ordinal) &&
        (!IsCustomLinkType || !string.IsNullOrWhiteSpace(CustomType));

    private async Task LinkFilesAsync()
    {
        if (_service is null || !CanLinkFiles())
        {
            return;
        }

        await RunOperationAsync(
            token => _service.LinkFilesAsync(
                FirstLinkFile!.FileId,
                SecondLinkFile!.FileId,
                LinkType,
                CustomType,
                AlwaysRelate,
                token));
    }

    private async Task UnlinkAsync()
    {
        if (_service is null || SelectedRelationship is null)
        {
            return;
        }

        RequestDestructiveConfirmation(new PendingRelationshipMutation(
            RelationshipMutationKind.Unlink,
            SelectedRelationship.Id,
            null,
            "Unlink this virtual relationship? Retained relationship authority/evidence for this exact record will be removed. Original files are unchanged."));
        await Task.CompletedTask;
    }

    private async Task SetDecisionAsync(RelationshipDecision decision)
    {
        if (_service is null || SelectedRelationship is null)
        {
            return;
        }

        await RunOperationAsync(token => _service.SetDecisionAsync(SelectedRelationship.Id, decision, token));
    }

    private async Task SetRelatedFileDecisionAsync(RelationshipDecision decision)
    {
        if (_service is null || SelectedRelatedFile is null)
        {
            return;
        }

        await RunOperationAsync(token =>
            _service.SetDecisionAsync(SelectedRelatedFile.Relationship.Id, decision, token));
    }

    private async Task UseAutomaticAsync()
    {
        if (_service is null || SelectedFile is null || SelectedRelatedFile is null)
        {
            return;
        }

        RequestDestructiveConfirmation(new PendingRelationshipMutation(
            RelationshipMutationKind.UseAutomatic,
            SelectedFile.FileId,
            SelectedRelatedFile.FileId,
            "Use the automatic relationship result for this pair? The explicit correction will be removed and future results will again be derived from indexed evidence. Original files are unchanged."));
        await Task.CompletedTask;
    }

    private async Task UseAutomaticCorrectionAsync()
    {
        if (_service is null || SelectedCorrection is null)
        {
            return;
        }

        RequestDestructiveConfirmation(new PendingRelationshipMutation(
            RelationshipMutationKind.UseAutomatic,
            SelectedCorrection.FirstFileId,
            SelectedCorrection.SecondFileId,
            "Use the automatic relationship result for this corrected pair? The explicit correction will be removed and future results will again be derived from indexed evidence. Original files are unchanged."));
        await Task.CompletedTask;
    }

    /// <summary>Selects one exact stable file identity as a direct Related Files entry point.</summary>
    public async Task SelectFileAsync(string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return;
        }

        SelectedSectionIndex = 1;
        if (_files.Count == 0)
        {
            await RefreshAsync();
        }

        SelectedFile = _files.FirstOrDefault(item => string.Equals(item.FileId, fileId, StringComparison.Ordinal));
    }

    private bool CanRenameCollection() =>
        SelectedCollection is not null && !IsBusy && !IsDestructiveConfirmationPending &&
        !string.IsNullOrWhiteSpace(RenameText) && RenameText.Length <= RelationshipLimits.MaximumCollectionTitleCharacters;

    private async Task RenameCollectionAsync()
    {
        if (_service is null || SelectedCollection is null || !CanRenameCollection())
        {
            return;
        }

        await RunOperationAsync(token => _service.RenameCollectionAsync(SelectedCollection.Id, RenameText, token));
    }

    private async Task TogglePinAsync()
    {
        if (_service is null || SelectedCollection is null)
        {
            return;
        }

        await RunOperationAsync(token => _service.SetCollectionPinnedAsync(SelectedCollection.Id, !SelectedCollection.IsPinned, token));
    }

    private bool CanMergeCollection() =>
        SelectedCollection is not null && MergeCollection is not null && !IsBusy && !IsDestructiveConfirmationPending &&
        !string.Equals(SelectedCollection.Id, MergeCollection.Id, StringComparison.Ordinal);

    private async Task MergeCollectionAsync()
    {
        if (_service is null || !CanMergeCollection())
        {
            return;
        }

        RequestDestructiveConfirmation(new PendingRelationshipMutation(
            RelationshipMutationKind.MergeCollection,
            SelectedCollection!.Id,
            MergeCollection!.Id,
            $"Merge '{MergeCollection.Title}' into '{SelectedCollection.Title}'? The source virtual collection record will no longer remain separate. Original files are unchanged."));
        await Task.CompletedTask;
    }

    private async Task SplitMemberAsync()
    {
        if (_service is null || SelectedCollection is null || SelectedMember is null)
        {
            return;
        }

        RequestDestructiveConfirmation(new PendingRelationshipMutation(
            RelationshipMutationKind.SplitMember,
            SelectedCollection.Id,
            SelectedMember.FileId,
            $"Remove '{SelectedMember.FileName}' from '{SelectedCollection.Title}'? This changes retained virtual membership only; the original file is unchanged."));
        await Task.CompletedTask;
    }

    private void RequestForgetCollection()
    {
        if (_service is null || SelectedCollection is null)
        {
            return;
        }

        RequestDestructiveConfirmation(new PendingRelationshipMutation(
            RelationshipMutationKind.ForgetCollection,
            SelectedCollection.Id,
            null,
            $"Forget '{SelectedCollection.Title}'? Its virtual collection record will be removed. Original files are unchanged."));
    }

    private async Task ForgetFileRelationshipsAsync(bool excludeFuture)
    {
        if (_service is null || SelectedFile is null)
        {
            return;
        }

        RequestDestructiveConfirmation(new PendingRelationshipMutation(
            RelationshipMutationKind.ForgetFile,
            SelectedFile.FileId,
            excludeFuture.ToString(),
            $"Forget relationship data for '{SelectedFile.FileName}' and exclude it from future relationship analysis? User corrections and derived relationships for this file will be removed; the original file is unchanged."));
        await Task.CompletedTask;
    }

    private async Task RebuildFileRelationshipsAsync()
    {
        if (_service is null || SelectedFile is null)
        {
            return;
        }

        await RunOperationAsync(token => _service.RebuildFileAsync(SelectedFile.FileId, token));
    }

    private async Task ForgetSourceRelationshipsAsync()
    {
        if (_service is null || SelectedFile is null)
        {
            return;
        }

        RequestDestructiveConfirmation(new PendingRelationshipMutation(
            RelationshipMutationKind.ForgetSource,
            SelectedFile.SourceId,
            null,
            $"Forget relationship data for source '{SelectedFile.SourceName}' and exclude it from future relationship analysis? Original files and source ownership are unchanged."));
        await Task.CompletedTask;
    }

    private void RequestDestructiveConfirmation(PendingRelationshipMutation mutation)
    {
        _pendingDestructiveMutation = mutation;
        OnPropertyChanged(nameof(IsForgetCollectionPending));
        OnPropertyChanged(nameof(IsDestructiveConfirmationPending));
        OnPropertyChanged(nameof(DestructiveConfirmationText));
        StatusText = "Review the pending relationship-data change, then confirm or cancel it.";
        NotifyCommands();
    }

    private void CancelDestructiveConfirmation()
    {
        if (_pendingDestructiveMutation is null)
        {
            return;
        }

        _pendingDestructiveMutation = null;
        OnPropertyChanged(nameof(IsForgetCollectionPending));
        OnPropertyChanged(nameof(IsDestructiveConfirmationPending));
        OnPropertyChanged(nameof(DestructiveConfirmationText));
        StatusText = "The relationship-data change was cancelled. Nothing was changed.";
        NotifyCommands();
    }

    private async Task ConfirmDestructiveMutationAsync()
    {
        if (_service is null || _pendingDestructiveMutation is not { } pending)
        {
            return;
        }

        _pendingDestructiveMutation = null;
        OnPropertyChanged(nameof(IsForgetCollectionPending));
        OnPropertyChanged(nameof(IsDestructiveConfirmationPending));
        OnPropertyChanged(nameof(DestructiveConfirmationText));
        NotifyCommands();
        await RunOperationAsync(token => pending.Kind switch
        {
            RelationshipMutationKind.Unlink => _service.UnlinkAsync(pending.PrimaryId, cancellationToken: token),
            RelationshipMutationKind.MergeCollection => _service.MergeCollectionsAsync(pending.PrimaryId, pending.SecondaryId!, token),
            RelationshipMutationKind.SplitMember => _service.SplitCollectionMemberAsync(pending.PrimaryId, pending.SecondaryId!, token),
            RelationshipMutationKind.ForgetCollection => _service.ForgetCollectionAsync(pending.PrimaryId, token),
            RelationshipMutationKind.ForgetFile => _service.ForgetFileAsync(
                pending.PrimaryId,
                bool.TryParse(pending.SecondaryId, out var excludeFuture) && excludeFuture,
                token),
            RelationshipMutationKind.ForgetSource => _service.ForgetSourceAsync(pending.PrimaryId, true, token),
            RelationshipMutationKind.UseAutomatic => _service.UseAutomaticAsync(pending.PrimaryId, pending.SecondaryId!, token),
            RelationshipMutationKind.Repair => _service.RepairAsync(token),
            _ => Task.FromResult(new RelationshipOperationResult(false, 0, 0, "The pending relationship operation is no longer valid.")),
        });
    }

    private async Task RepairAsync()
    {
        if (_service is null)
        {
            return;
        }

        RequestDestructiveConfirmation(new PendingRelationshipMutation(
            RelationshipMutationKind.Repair,
            "relationship-store",
            null,
            "Repair retained relationship data? Invalid relationship, evidence, collection, and membership records may be removed so consistent projections can be rebuilt. Valid user authority and original files are unchanged."));
        await Task.CompletedTask;
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task<RelationshipOperationResult>> operation)
    {
        using var cancellation = BeginOperation();
        var finalMessage = string.Empty;
        try
        {
            var result = await operation(cancellation.Token);
            finalMessage = result.Message;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            finalMessage = "The relationship operation was cancelled safely.";
        }
        catch (Exception)
        {
            finalMessage = "The relationship operation failed safely. Original files were not affected.";
        }
        finally
        {
            EndOperation(cancellation);
        }

        await RefreshAsync();
        StatusText = finalMessage;
    }

    private CancellationTokenSource BeginOperation()
    {
        var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        IsBusy = true;
        return cancellation;
    }

    private void EndOperation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(_operation, cancellation))
        {
            _operation = null;
        }

        IsBusy = false;
    }

    private void Cancel()
    {
        _operation?.Cancel();
        StatusText = "Cancelling relationship operation...";
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        LinkFilesCommand.NotifyCanExecuteChanged();
        UnlinkCommand.NotifyCanExecuteChanged();
        ConfirmRelationshipCommand.NotifyCanExecuteChanged();
        AlwaysRelateRelationshipCommand.NotifyCanExecuteChanged();
        RejectRelationshipCommand.NotifyCanExecuteChanged();
        NeverRelateCommand.NotifyCanExecuteChanged();
        RenameCollectionCommand.NotifyCanExecuteChanged();
        TogglePinCommand.NotifyCanExecuteChanged();
        MergeCollectionCommand.NotifyCanExecuteChanged();
        SplitMemberCommand.NotifyCanExecuteChanged();
        RequestForgetCollectionCommand.NotifyCanExecuteChanged();
        ConfirmForgetCollectionCommand.NotifyCanExecuteChanged();
        CancelForgetCollectionCommand.NotifyCanExecuteChanged();
        ConfirmDestructiveActionCommand.NotifyCanExecuteChanged();
        CancelDestructiveActionCommand.NotifyCanExecuteChanged();
        RefreshRelatedFilesCommand.NotifyCanExecuteChanged();
        ForgetFileRelationshipsCommand.NotifyCanExecuteChanged();
        ForgetSourceRelationshipsCommand.NotifyCanExecuteChanged();
        RebuildFileRelationshipsCommand.NotifyCanExecuteChanged();
        MarkRelatedCommand.NotifyCanExecuteChanged();
        MarkNotRelatedCommand.NotifyCanExecuteChanged();
        UseAutomaticCommand.NotifyCanExecuteChanged();
        UseAutomaticCorrectionCommand.NotifyCanExecuteChanged();
        RepairCommand.NotifyCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private enum RelationshipMutationKind
    {
        Unlink,
        MergeCollection,
        SplitMember,
        ForgetCollection,
        ForgetFile,
        ForgetSource,
        UseAutomatic,
        Repair,
    }

    private sealed record PendingRelationshipMutation(
        RelationshipMutationKind Kind,
        string PrimaryId,
        string? SecondaryId,
        string Description);

    private static SmartCollection? PreserveSelection(IEnumerable<SmartCollection> values, string? id) =>
        id is null ? null : values.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    private static RelationshipFileDocument? PreserveFileSelection(IEnumerable<RelationshipFileDocument> values, string? id) =>
        id is null ? null : values.FirstOrDefault(item => string.Equals(item.FileId, id, StringComparison.Ordinal));

    /// <inheritdoc />
    public void Dispose()
    {
        var operation = Interlocked.Exchange(ref _operation, null);
        operation?.Cancel();
        operation?.Dispose();
    }
}
