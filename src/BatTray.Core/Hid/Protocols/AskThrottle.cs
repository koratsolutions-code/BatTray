using System.Collections.Concurrent;

namespace BatTray.Core.Hid.Protocols;

/// <summary>
/// Limits how often a receiver is asked for its battery. Some keyboard receivers relay every query to the
/// keyboard, which may keep it from going to sleep; for those the app asks only every few minutes and lets
/// the provider's grace period keep the last level visible in between.
/// </summary>
internal sealed class AskThrottle
{
    private readonly TimeSpan _interval;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAsk = new();

    public AskThrottle(TimeSpan interval, TimeProvider? timeProvider = null)
    {
        _interval = interval;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>True (and the time is recorded) when <paramref name="key"/> may be asked now.</summary>
    public bool TryAsk(string key)
    {
        var now = _time.GetUtcNow();
        while (true)
        {
            if (!_lastAsk.TryGetValue(key, out var last))
            {
                if (_lastAsk.TryAdd(key, now))
                    return true;
                continue;
            }
            if (now - last < _interval)
                return false;
            if (_lastAsk.TryUpdate(key, now, last))
                return true;
        }
    }
}
