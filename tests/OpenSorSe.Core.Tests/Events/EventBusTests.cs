using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Events;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Core.Tests.Events;

/// <summary>
/// Tests in-memory event delivery behavior.
/// </summary>
public sealed class EventBusTests
{
    /// <summary>
    /// Verifies that a failing subscriber does not prevent subsequent subscribers from receiving an event.
    /// </summary>
    [Fact]
    public async Task PublishAsync_ContinuesAfterSubscriberFailure()
    {
        using var loggingService = new LoggingService();
        loggingService.Initialize(new LoggingOptions(LogLevel.Trace, FileLoggingEnabled: false));
        var eventBus = new EventBus(loggingService);
        var handled = false;
        eventBus.Subscribe<TestEvent>((_, _) => throw new InvalidOperationException("Expected test failure."));
        eventBus.Subscribe<TestEvent>((_, _) =>
        {
            handled = true;
            return Task.CompletedTask;
        });

        await eventBus.PublishAsync(new TestEvent());

        Assert.True(handled);
    }

    /// <summary>Verifies an unrelated cancellation exception is isolated like any other subscriber failure.</summary>
    [Fact]
    public async Task PublishAsync_UnrelatedSubscriberCancellation_ContinuesDelivery()
    {
        using var loggingService = new LoggingService();
        loggingService.Initialize(new LoggingOptions(LogLevel.Trace, FileLoggingEnabled: false));
        var eventBus = new EventBus(loggingService);
        var handled = false;
        eventBus.Subscribe<TestEvent>((_, _) =>
            Task.FromException(new OperationCanceledException("Subscriber-local cancellation.")));
        eventBus.Subscribe<TestEvent>((_, _) =>
        {
            handled = true;
            return Task.CompletedTask;
        });

        await eventBus.PublishAsync(new TestEvent());

        Assert.True(handled);
        Assert.Equal(1L, loggingService.GetStatistics().ErrorEntries);
    }

    /// <summary>Verifies cancellation requested by the publisher propagates and stops later subscribers.</summary>
    [Fact]
    public async Task PublishAsync_PublisherCancellation_PropagatesAndStopsDelivery()
    {
        using var loggingService = new LoggingService();
        var eventBus = new EventBus(loggingService);
        using var cancellation = new CancellationTokenSource();
        var laterHandled = false;
        eventBus.Subscribe<TestEvent>((_, _) =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        eventBus.Subscribe<TestEvent>((_, _) =>
        {
            laterHandled = true;
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            eventBus.PublishAsync(new TestEvent(), cancellation.Token));

        Assert.False(laterHandled);
        Assert.Equal(0L, loggingService.GetStatistics().ErrorEntries);
    }

    /// <summary>Verifies disposal is idempotent and removes only the associated subscription.</summary>
    [Fact]
    public async Task Subscription_DisposeTwice_RemovesOnlyDisposedHandler()
    {
        using var loggingService = new LoggingService();
        var eventBus = new EventBus(loggingService);
        var disposedCalls = 0;
        var retainedCalls = 0;
        var subscription = eventBus.Subscribe<TestEvent>((_, _) =>
        {
            disposedCalls++;
            return Task.CompletedTask;
        });
        eventBus.Subscribe<TestEvent>((_, _) =>
        {
            retainedCalls++;
            return Task.CompletedTask;
        });

        subscription.Dispose();
        subscription.Dispose();
        await eventBus.PublishAsync(new TestEvent());

        Assert.Equal(0, disposedCalls);
        Assert.Equal(1, retainedCalls);
    }

    private sealed class TestEvent : IApplicationEvent
    {
    }
}
