using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OpenSorSe.Application.Workflows;
using OpenSorSe.Executor;

#pragma warning disable CS1591

namespace OpenSorSe.Desktop.ViewModels;

public sealed record OrganizationStrategyOption(OrganizationStrategy Strategy, string Label, string Description)
{
    public override string ToString() => Label;
}

public sealed record OrganizationTreeNode(string Name, string RelativePath, string? FileId)
{
    public ObservableCollection<OrganizationTreeNode> Children { get; } = [];
}

public sealed partial class ReviewedOrganizationViewModel
{
    private readonly Dictionary<string, OrganizationProposalEdit> _edits = new(StringComparer.Ordinal);
    private readonly ObservableCollection<OrganizationProposalRowViewModel> _allRows = [];
    private bool _proposalDirty;
    private bool _editorDirty;
    private bool _preferencesRemembered;
    private bool _useLearnedPreferences = true;
    private OrganizationStrategyOption _selectedStrategy = StrategyOptions[1];
    private OrganizationTreeNode? _selectedTreeNode;
    private OrganizationProposalRowViewModel? _selectedProposalRow;
    private string _editedRelativePath = string.Empty;

    private static readonly OrganizationStrategyOption[] StrategyOptions =
    [
        new(OrganizationStrategy.Preserve, "Preserve my structure", "Keep containing folders and apply only the recipe's filename pattern."),
        new(OrganizationStrategy.Improve, "Improve my structure", "Keep the existing hierarchy and group files by the recommended folder."),
        new(OrganizationStrategy.Fresh, "Reorganize from scratch", "Use the recipe hierarchy from the library root; review every proposed change."),
    ];

    public IReadOnlyList<OrganizationStrategyOption> Strategies => StrategyOptions;
    public ObservableCollection<OrganizationTreeNode> CurrentTree { get; } = [];
    public ObservableCollection<OrganizationTreeNode> RecommendedTree { get; } = [];
    public ReadOnlyObservableCollection<OrganizationProposalRowViewModel> AllRows { get; private set; } = null!;
    public IRelayCommand BrowseFilesCommand { get; private set; } = null!;
    public IRelayCommand BrowseSearchCommand { get; private set; } = null!;
    public IAsyncRelayCommand ApplyEditCommand { get; private set; } = null!;
    public IAsyncRelayCommand RejectMoveCommand { get; private set; } = null!;
    public IAsyncRelayCommand RestoreMoveCommand { get; private set; } = null!;
    public IAsyncRelayCommand RememberPreferencesCommand { get; private set; } = null!;
    public event EventHandler? BrowseFilesRequested;
    public event EventHandler? BrowseSearchRequested;

    public OrganizationStrategyOption SelectedStrategy
    {
        get => _selectedStrategy;
        set
        {
            if (value is not null && SetProperty(ref _selectedStrategy, value))
            {
                OnPropertyChanged(nameof(StrategyDescription));
                InvalidateProposal("Strategy changed. Recommend the structure again before Review Changes.");
            }
        }
    }

    public string StrategyDescription => SelectedStrategy.Description;

    public bool UseLearnedPreferences
    {
        get => _useLearnedPreferences;
        set
        {
            if (SetProperty(ref _useLearnedPreferences, value))
            {
                InvalidateProposal("Preference setting changed. Recommend the structure again.");
            }
        }
    }

    public OrganizationTreeNode? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set
        {
            if (SetProperty(ref _selectedTreeNode, value))
            {
                _selectedProposalRow = _allRows.FirstOrDefault(row => row.Model.FileId == value?.FileId);
                OnPropertyChanged(nameof(SelectedProposalRow));
                SetEditorPath(value?.RelativePath ?? string.Empty);
            }
        }
    }

    public OrganizationProposalRowViewModel? SelectedProposalRow
    {
        get => _selectedProposalRow;
        set
        {
            if (SetProperty(ref _selectedProposalRow, value))
            {
                var relative = value is null || _proposal is null ? string.Empty
                    : RelativeDisplayPath(_proposal.OrganizationRoot, value.Model.TargetPath ?? value.CurrentPath);
                _selectedTreeNode = value is null ? null : new OrganizationTreeNode(value.FileName, relative, value.Model.FileId);
                OnPropertyChanged(nameof(SelectedTreeNode));
                SetEditorPath(relative);
            }
        }
    }

    public string EditedRelativePath
    {
        get => _editedRelativePath;
        set
        {
            if (SetProperty(ref _editedRelativePath, value ?? string.Empty))
            {
                _editorDirty = true;
                StatusText = "Proposal edit pending. Choose Update proposal to validate it before Review Changes.";
                NotifyCommands();
            }
        }
    }

    private void InitializeOrganizationEditing()
    {
        AllRows = new ReadOnlyObservableCollection<OrganizationProposalRowViewModel>(_allRows);
        BrowseFilesCommand = new RelayCommand(() => BrowseFilesRequested?.Invoke(this, EventArgs.Empty));
        BrowseSearchCommand = new RelayCommand(() => BrowseSearchRequested?.Invoke(this, EventArgs.Empty));
        ApplyEditCommand = new AsyncRelayCommand(ApplyEditAsync, () => _proposal is not null && SelectedTreeNode is not null && !IsBusy);
        RejectMoveCommand = new AsyncRelayCommand(RejectMoveAsync, () => SelectedProposalRow is not null && !IsBusy);
        RestoreMoveCommand = new AsyncRelayCommand(RestoreMoveAsync, () => SelectedProposalRow is not null && !IsBusy);
        RememberPreferencesCommand = new AsyncRelayCommand(RememberPreferencesAsync,
            () => CanReviewChanges() && !_preferencesRemembered && HasRememberableFolderEdit());
    }

    private bool HasRememberableFolderEdit() => _proposal is { } proposal && proposal.Rows.Any(row =>
        row.IsEligible && row.RecommendedRelativeDestination is { } recommended &&
        _edits.TryGetValue(row.FileId, out var edit) && edit.RelativeTargetPath is not null && !edit.IsRejected &&
        !string.Equals(recommended.Replace('\\', '/'),
            Path.GetDirectoryName(RelativeDisplayPath(proposal.OrganizationRoot, row.TargetPath!))?.Replace('\\', '/') is { Length: > 0 } folder
                ? folder : ".", ChangePlanFactory.PathComparison));

    private async Task ApplyEditAsync()
    {
        if (_proposal is null || SelectedTreeNode is not { } selected || IsBusy)
        {
            return;
        }

        if (selected.FileId is { } fileId)
        {
            _edits[fileId] = new OrganizationProposalEdit(fileId, EditedRelativePath);
        }
        else
        {
            var prefix = selected.RelativePath.TrimEnd('/') + "/";
            var replacement = EditedRelativePath.Replace('\\', '/').TrimEnd('/');
            foreach (var row in _proposal.Rows.Where(row => !row.IsRejected && row.TargetPath is not null))
            {
                var relative = RelativeDisplayPath(_proposal.OrganizationRoot, row.TargetPath!);
                if (relative.StartsWith(prefix, ChangePlanFactory.PathComparison))
                {
                    var suffix = relative[prefix.Length..];
                    _edits[row.FileId] = new OrganizationProposalEdit(row.FileId,
                        string.IsNullOrEmpty(replacement) ? suffix : $"{replacement}/{suffix}");
                }
            }
        }

        _preferencesRemembered = false;
        await PreviewAsync();
    }

    private async Task RejectMoveAsync()
    {
        if (SelectedProposalRow is not { } row || IsBusy)
        {
            return;
        }

        _edits[row.Model.FileId] = new OrganizationProposalEdit(row.Model.FileId, null, true);
        _preferencesRemembered = false;
        await PreviewAsync();
    }

    private async Task RestoreMoveAsync()
    {
        if (SelectedProposalRow is not { } row || IsBusy)
        {
            return;
        }

        _edits.Remove(row.Model.FileId);
        _preferencesRemembered = false;
        await PreviewAsync();
    }

    private async Task RememberPreferencesAsync()
    {
        if (_organization is null || _proposal is null || !CanReviewChanges())
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _organization.RememberPreferencesAsync(_proposal, CancellationToken.None);
            _preferencesRemembered = true;
            StatusText = "Valid folder edits were remembered for this library. No source files changed.";
        }
        catch (InvalidDataException exception)
        {
            _proposalDirty = true;
            StatusText = exception.Message;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _proposalDirty = true;
            StatusText = "Preferences could not be saved or the proposal became stale. Check local storage and recommend the structure again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshTrees()
    {
        var selectedFileId = SelectedProposalRow?.Model.FileId;
        CurrentTree.Clear();
        RecommendedTree.Clear();
        _allRows.Clear();
        if (_proposal is not null)
        {
            foreach (var row in _proposal.Rows.OrderBy(row => row.CurrentPath, ChangePlanFactory.PathComparer))
            {
                _allRows.Add(new OrganizationProposalRowViewModel(row));
                AddTreePath(CurrentTree, RelativeDisplayPath(_proposal.OrganizationRoot, row.CurrentPath), row.FileId);
                AddTreePath(RecommendedTree, RelativeDisplayPath(_proposal.OrganizationRoot,
                    row.IsRejected ? row.CurrentPath : row.TargetPath ?? row.CurrentPath), row.FileId);
            }
        }

        SelectedProposalRow = _allRows.FirstOrDefault(row => row.Model.FileId == selectedFileId);
        if (SelectedProposalRow is null)
        {
            SelectedTreeNode = null;
        }
    }

    private static void AddTreePath(ObservableCollection<OrganizationTreeNode> roots, string path, string fileId)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var children = roots;
        var relative = string.Empty;
        for (var index = 0; index < segments.Length; index++)
        {
            relative = string.IsNullOrEmpty(relative) ? segments[index] : $"{relative}/{segments[index]}";
            var leaf = index == segments.Length - 1;
            var existing = children.FirstOrDefault(node => node.FileId is null &&
                string.Equals(node.Name, segments[index], ChangePlanFactory.PathComparison));
            if (leaf || existing is null)
            {
                existing = new OrganizationTreeNode(segments[index], relative, leaf ? fileId : null);
                children.Add(existing);
            }

            children = existing.Children;
        }
    }

    private static string RelativeDisplayPath(string root, string path)
    {
        var prefix = root.Replace('\\', '/').TrimEnd('/') + "/";
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith(prefix, ChangePlanFactory.PathComparison) ? normalized[prefix.Length..] : normalized;
    }

    private void SetEditorPath(string value)
    {
        _editedRelativePath = value;
        _editorDirty = false;
        OnPropertyChanged(nameof(EditedRelativePath));
        NotifyCommands();
    }

    private void ResetEdits()
    {
        _edits.Clear();
        _preferencesRemembered = false;
        _proposalDirty = false;
        _editorDirty = false;
    }

    private void NotifyEditingCommands()
    {
        ApplyEditCommand?.NotifyCanExecuteChanged();
        RejectMoveCommand?.NotifyCanExecuteChanged();
        RestoreMoveCommand?.NotifyCanExecuteChanged();
        RememberPreferencesCommand?.NotifyCanExecuteChanged();
    }
}
