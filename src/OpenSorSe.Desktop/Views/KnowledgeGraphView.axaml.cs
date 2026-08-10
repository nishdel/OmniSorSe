using Avalonia.Controls;
using Avalonia.Threading;
using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Views;

/// <summary>Hosts the bounded, list-and-detail Knowledge Graph surface.</summary>
public partial class KnowledgeGraphView : UserControl
{
    private KnowledgeGraphViewModel? _viewModel;

    /// <summary>Initializes the Knowledge Graph view.</summary>
    public KnowledgeGraphView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs eventArgs)
    {
        Unsubscribe();
        base.OnDataContextChanged(eventArgs);
        Subscribe();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        Subscribe();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs eventArgs)
    {
        Unsubscribe();
        base.OnDetachedFromVisualTree(eventArgs);
    }

    private void Unsubscribe()
    {
        if (_viewModel is not null)
        {
            _viewModel.FocusRequested -= OnFocusRequested;
            _viewModel = null;
        }
    }

    private void Subscribe()
    {
        var viewModel = DataContext as KnowledgeGraphViewModel;
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        Unsubscribe();
        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.FocusRequested += OnFocusRequested;
        }
    }

    private void OnFocusRequested(KnowledgeGraphFocusRequest request) =>
        Dispatcher.UIThread.Post(
            () => FocusRequestedTarget(request),
            DispatcherPriority.Loaded);

    private void FocusRequestedTarget(KnowledgeGraphFocusRequest request)
    {
        var target = ResolveFocusTarget(request.Target);
        if (request.ItemId is not null && target is ListBox list && ResolveRequestedItem(request) is { } item)
        {
            list.SelectedItem = item;
            list.ScrollIntoView(item);
            if (list.ContainerFromItem(item) is Control container && container.Focus())
            {
                return;
            }
        }

        target.Focus();
    }

    private object? ResolveRequestedItem(KnowledgeGraphFocusRequest request) => request.Target switch
    {
        KnowledgeGraphFocusTarget.NodeList =>
            _viewModel?.Nodes.FirstOrDefault(item => string.Equals(item.Id, request.ItemId, StringComparison.Ordinal)),
        KnowledgeGraphFocusTarget.NeighborList =>
            _viewModel?.Neighbors.FirstOrDefault(item => string.Equals(item.NodeId, request.ItemId, StringComparison.Ordinal)),
        _ => null,
    };

    private Control ResolveFocusTarget(KnowledgeGraphFocusTarget target) => target switch
    {
        KnowledgeGraphFocusTarget.EnableControl => GraphEnableButton,
        KnowledgeGraphFocusTarget.NodeList => GraphNodeList,
        KnowledgeGraphFocusTarget.NeighborList => GraphNeighborList,
        KnowledgeGraphFocusTarget.PrivacyHeading => GraphPrivacyHeading,
        KnowledgeGraphFocusTarget.RepairHeading => GraphRepairHeading,
        KnowledgeGraphFocusTarget.DecisionConfirmation => GraphDecisionConfirmButton,
        KnowledgeGraphFocusTarget.InitiatingControl => GraphRefreshButton,
        _ => GraphPageHeading,
    };
}
