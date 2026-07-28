using Microsoft.Extensions.Logging;
using OpenSorSe.Core.Logging;

namespace OpenSorSe.Core.Events;

/// <summary>
/// Provides in-memory publish-subscribe event delivery with isolated subscriber failures.
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly Dictionary<Type, List<Func<IApplicationEvent, CancellationToken, Task>>> _handlers = new();
    private readonly ILoggingService _loggingService;
    private readonly object _syncRoot = new();

    /// <summary>
    /// Initializes an event bus that reports subscriber failures through centralized logging.
    /// </summary>
    /// <param name="loggingService">The central logging service.</param>
    public EventBus(ILoggingService loggingService)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
    }

    /// <inheritdoc />
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : IApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        Func<IApplicationEvent, CancellationToken, Task> wrappedHandler =
            (applicationEvent, cancellationToken) => handler((TEvent)applicationEvent, cancellationToken);

        lock (_syncRoot)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            {
                handlers = new List<Func<IApplicationEvent, CancellationToken, Task>>();
                _handlers.Add(typeof(TEvent), handlers);
            }

            handlers.Add(wrappedHandler);
        }

        return new Subscription(() => RemoveHandler(typeof(TEvent), wrappedHandler));
    }

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(
        TEvent applicationEvent,
        CancellationToken cancellationToken = default)
        where TEvent : IApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(applicationEvent);

        List<Func<IApplicationEvent, CancellationToken, Task>> handlers;
        lock (_syncRoot)
        {
            handlers = _handlers.TryGetValue(typeof(TEvent), out var registeredHandlers)
                ? new List<Func<IApplicationEvent, CancellationToken, Task>>(registeredHandlers)
                : new List<Func<IApplicationEvent, CancellationToken, Task>>();
        }

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await handler(applicationEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _loggingService.CreateLogger(nameof(EventBus)).LogError(
                    exception,
                    "An event subscriber failed while handling {EventType}.",
                    typeof(TEvent).Name);
            }
        }
    }

    private void RemoveHandler(
        Type eventType,
        Func<IApplicationEvent, CancellationToken, Task> handler)
    {
        lock (_syncRoot)
        {
            if (!_handlers.TryGetValue(eventType, out var handlers))
            {
                return;
            }

            handlers.Remove(handler);
            if (handlers.Count == 0)
            {
                _handlers.Remove(eventType);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _unsubscribe;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }
}
