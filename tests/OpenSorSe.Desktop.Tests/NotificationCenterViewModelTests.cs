using OpenSorSe.Desktop.ViewModels;

namespace OpenSorSe.Desktop.Tests;

/// <summary>
/// Verifies bounded, non-blocking notification queue behavior.
/// </summary>
public sealed class NotificationCenterViewModelTests
{
    /// <summary>
    /// Verifies notifications receive deterministic process-local identifiers and retain insertion order.
    /// </summary>
    [Fact]
    public void Publish_QueuesAllSeverityTypesInInsertionOrder()
    {
        using var viewModel = new NotificationCenterViewModel();

        var information = viewModel.Publish(new NotificationRequest(NotificationSeverity.Information, "Information"));
        var success = viewModel.Publish(new NotificationRequest(NotificationSeverity.Success, "Success"));
        var warning = viewModel.Publish(new NotificationRequest(NotificationSeverity.Warning, "Warning"));
        var error = viewModel.Publish(new NotificationRequest(NotificationSeverity.Error, "Error"));

        Assert.Equal([information, success, warning, error], viewModel.Notifications);
        Assert.Equal("notification:1", information.Id);
        Assert.Equal("notification:4", error.Id);
    }

    /// <summary>
    /// Verifies temporary notifications expire deterministically while persistent warnings remain displayed.
    /// </summary>
    [Fact]
    public void DismissExpired_RemovesOnlyExpiredTemporaryNotifications()
    {
        using var viewModel = new NotificationCenterViewModel();
        var temporary = viewModel.Publish(new NotificationRequest(NotificationSeverity.Information, "Temporary", TimeSpan.FromMinutes(1)));
        var warning = viewModel.Publish(new NotificationRequest(NotificationSeverity.Warning, "Warning"));
        var expiry = Assert.IsType<DateTimeOffset>(temporary.ExpiresAtUtc);

        viewModel.DismissExpired(expiry);

        Assert.DoesNotContain(temporary, viewModel.Notifications);
        Assert.Contains(warning, viewModel.Notifications);
    }

    /// <summary>
    /// Verifies manual dismissal removes only the selected message.
    /// </summary>
    [Fact]
    public void DismissSelected_RemovesOnlySelectedNotification()
    {
        using var viewModel = new NotificationCenterViewModel();
        var first = viewModel.Publish(new NotificationRequest(NotificationSeverity.Warning, "First"));
        var second = viewModel.Publish(new NotificationRequest(NotificationSeverity.Error, "Second"));
        viewModel.SelectedNotification = first;

        viewModel.DismissSelectedCommand.Execute(null);

        Assert.DoesNotContain(first, viewModel.Notifications);
        Assert.Contains(second, viewModel.Notifications);
        Assert.Null(viewModel.SelectedNotification);
    }

    /// <summary>Verifies the compact badge opens and closes without deleting transient evidence.</summary>
    [Fact]
    public void Drawer_OpenCloseAndEscapeCommandPreserveNotifications()
    {
        using var viewModel = new NotificationCenterViewModel();
        viewModel.Publish(new NotificationRequest(NotificationSeverity.Warning, "Warning"));
        viewModel.Publish(new NotificationRequest(NotificationSeverity.Error, "Error"));

        Assert.Equal("⚠ 2", viewModel.BadgeText);
        viewModel.ToggleCommand.Execute(null);
        Assert.True(viewModel.IsOpen);

        viewModel.CloseCommand.Execute(null);

        Assert.False(viewModel.IsOpen);
        Assert.Equal(2, viewModel.Notifications.Count);
    }

    /// <summary>Verifies individual and clear-all dismissal affect only the transient queue.</summary>
    [Fact]
    public void DismissAndClearAll_UpdateCountsAndCloseDrawer()
    {
        using var viewModel = new NotificationCenterViewModel();
        var information = viewModel.Publish(new NotificationRequest(NotificationSeverity.Information, "Information"));
        viewModel.Publish(new NotificationRequest(NotificationSeverity.Warning, "Warning"));
        viewModel.ToggleCommand.Execute(null);

        viewModel.DismissCommand.Execute(information);

        Assert.Equal(1, viewModel.NotificationCount);
        Assert.Equal(1, viewModel.AttentionCount);
        viewModel.ClearAllCommand.Execute(null);
        Assert.Empty(viewModel.Notifications);
        Assert.False(viewModel.IsOpen);
    }

    /// <summary>
    /// Verifies invalid notification requests are rejected before they enter the queue.
    /// </summary>
    [Fact]
    public void Publish_InvalidRequest_ThrowsWithoutQueueMutation()
    {
        using var viewModel = new NotificationCenterViewModel();

        Assert.Throws<ArgumentException>(() => viewModel.Publish(new NotificationRequest((NotificationSeverity)999, "Message")));
        Assert.Throws<ArgumentException>(() => viewModel.Publish(new NotificationRequest(NotificationSeverity.Information, " ")));
        Assert.Empty(viewModel.Notifications);
    }
}
