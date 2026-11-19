namespace BatTray.Core.Providers;

/// <summary>
/// Runs a slow read (RFCOMM sessions, an external tool) in the background so a poll waits at most
/// <c>maxWait</c>: the poll gets the fresh result when it is ready in time, otherwise the previous one, and
/// <see cref="Completed"/> fires when a late result arrives so the monitor can refresh again.
/// </summary>
internal sealed class BackgroundRefresh<T>(Func<CancellationToken, Task<T>> read, T initial, TimeSpan minInterval, TimeProvider time)
    : IDisposable
{
    private readonly object _lock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task<T>? _running;
    private T _last = initial;
    private DateTimeOffset _lastStart = DateTimeOffset.MinValue;

    /// <summary>Raised when a read that the poll did not wait for has finished.</summary>
    public event EventHandler? Completed;

    public T Last
    {
        get
        {
            lock (_lock)
                return _last;
        }
    }

    public async Task<T> GetAsync(TimeSpan maxWait, CancellationToken cancellationToken)
    {
        Task<T>? running;
        lock (_lock)
        {
            if (_running is null && !_shutdown.IsCancellationRequested && time.GetUtcNow() - _lastStart >= minInterval)
            {
                _lastStart = time.GetUtcNow();
                _running = RunAsync();
            }
            running = _running;
        }

        if (running is not null)
        {
            var finished = await Task.WhenAny(running, Task.Delay(maxWait, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            if (finished == running)
                return await running;

            // Too slow for this poll: ask for another one when the result is in.
            _ = running.ContinueWith(_ =>
            {
                if (!_shutdown.IsCancellationRequested)
                    Completed?.Invoke(this, EventArgs.Empty);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return Last;
    }

    /// <summary>Allows the next poll to start a read immediately (e.g. after a device connected).</summary>
    public void Invalidate()
    {
        lock (_lock)
            _lastStart = DateTimeOffset.MinValue;
    }

    private async Task<T> RunAsync()
    {
        try
        {
            var value = await Task.Run(() => read(_shutdown.Token));
            lock (_lock)
                _last = value;
            return value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !_shutdown.IsCancellationRequested)
        {
            CoreDiagnostics.Write($"Background battery read failed: {ex.Message}");
            return Last;
        }
        finally
        {
            lock (_lock)
                _running = null;
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
