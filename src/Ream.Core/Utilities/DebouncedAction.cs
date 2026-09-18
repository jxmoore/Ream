namespace Ream.Core.Utilities;

/// <summary>
/// Coalesces bursts of <see cref="Trigger"/> calls into a single run after a quiet period.
/// </summary>
public sealed class DebouncedAction : IDisposable
{
    private readonly Action _action;
    private readonly TimeSpan _delay;
    private readonly Action<Action>? _invoker;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private bool _pending;
    private bool _disposed;

    /// <param name="invoker">Marshals the action onto another thread (e.g. the UI thread); null runs it on the timer thread.</param>
    public DebouncedAction(Action action, TimeSpan delay, Action<Action>? invoker = null)
    {
        _action = action;
        _delay = delay;
        _invoker = invoker;
        _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Trigger()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending = true;
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Runs the action now on the calling thread if a run is pending.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_pending) return;
            _pending = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        _action();
    }

    private void OnTimer(object? state)
    {
        lock (_gate)
        {
            if (!_pending || _disposed) return;
            _pending = false;
        }

        if (_invoker is null) _action();
        else _invoker(_action);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending = false;
        }

        _timer.Dispose();
    }
}
