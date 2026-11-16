using System.Collections.Concurrent;

namespace IL.Misc.Concurrency;

/// <summary>Provides reusable keyed locks with configurable concurrency limits.</summary>
public static class LockManager
{
    private const long IdleTimeoutMilliseconds = 60_000;
    // Sweep at modest frequency: entries stay reusable for at least one minute and normally
    // retire within the next ten seconds (timer scheduling may delay cleanup further).
    private const int SweepIntervalMilliseconds = 10_000;
    private static readonly ConcurrentDictionary<LockKey, Lazy<Lock>> Locks = new();
    private static readonly Timer EvictionTimer = CreateEvictionTimer();

    internal readonly record struct LockKey(string Key, int MaxConcurrentCalls);

    private static Timer CreateEvictionTimer()
    {
        // A static callback alone does not prevent Timer from retaining the first caller's context.
        if (ExecutionContext.IsFlowSuppressed())
        {
            return new Timer(static _ => Sweep(Environment.TickCount64), null, SweepIntervalMilliseconds, SweepIntervalMilliseconds);
        }

        using (ExecutionContext.SuppressFlow())
        {
            return new Timer(static _ => Sweep(Environment.TickCount64), null, SweepIntervalMilliseconds, SweepIntervalMilliseconds);
        }
    }

    private static void Sweep(long timestamp)
    {
        foreach (var pair in Locks)
        {
            if (pair.Value.IsValueCreated)
            {
                pair.Value.Value.TryRetire(pair.Key, pair.Value, timestamp);
            }
        }
    }

    /// <summary>
    /// Reports whether the lock has an available semaphore slot or has not been created.
    /// Different concurrency limits identify different locks.
    /// </summary>
    public static bool IsLockAvailable(string key, int maxConcurrentCalls = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentCalls);
        var dictionaryKey = new LockKey(key ?? string.Empty, maxConcurrentCalls);
        while (Locks.TryGetValue(dictionaryKey, out var entry))
        {
            if (entry.Value.TryGetState(out var state))
            {
                return state > 0;
            }
        }

        return true;
    }

    /// <summary>Waits for a slot in the keyed lock and returns an idempotent release handle.</summary>
    public static IDisposable GetLock(string key, int maxConcurrentCalls = 1, CancellationToken cancellationToken = default)
    {
        var concurrentLock = AcquireLock(key, maxConcurrentCalls);
        concurrentLock.Wait(cancellationToken);
        return new LockReleaser(concurrentLock);
    }

    /// <summary>Asynchronously waits for a slot in the keyed lock and returns an idempotent release handle.</summary>
    public static async Task<IDisposable> GetLockAsync(string key, int maxConcurrentCalls = 1, CancellationToken cancellationToken = default)
    {
        var concurrentLock = AcquireLock(key, maxConcurrentCalls);
        await concurrentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new LockReleaser(concurrentLock);
    }

    // Also a deterministic test seam for the interval between reservation and semaphore wait.
    internal static Lock AcquireLock(string key, int maxConcurrentCalls, Action<Lock>? beforeReservation = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrentCalls);
        var dictionaryKey = new LockKey(key ?? string.Empty, maxConcurrentCalls);
        while (true)
        {
            var entry = Locks.GetOrAdd(dictionaryKey, static k =>
                new Lazy<Lock>(() => new Lock(k.MaxConcurrentCalls), LazyThreadSafetyMode.ExecutionAndPublication));
            var concurrentLock = entry.Value;
            beforeReservation?.Invoke(concurrentLock);
            if (concurrentLock.TryReserve())
            {
                return concurrentLock;
            }
            // Retirement won the gate after lookup. Never wait on that semaphore; retry the cache.
        }
    }

    // Targeted test hooks use the production retirement path, without altering the global clock/timer.
    internal static Lock? GetCachedLock(string key, int maxConcurrentCalls = 1) =>
        Locks.TryGetValue(new LockKey(key ?? string.Empty, maxConcurrentCalls), out var entry) ? entry.Value : null;

    internal static bool TryEvict(string key, int maxConcurrentCalls, long timestamp)
    {
        var dictionaryKey = new LockKey(key ?? string.Empty, maxConcurrentCalls);
        return Locks.TryGetValue(dictionaryKey, out var entry) && entry.Value.TryRetire(dictionaryKey, entry, timestamp);
    }

    internal sealed class LockReleaser : IDisposable
    {
        private Lock? _lock;

        internal LockReleaser(Lock l)
        {
            _lock = l;
            InternalLock = l;
        }

        internal Lock InternalLock { get; }

        public void Dispose()
        {
            var l = Interlocked.Exchange(ref _lock, null);
            l?.Release();
        }
    }

    internal sealed class Lock
    {
        private readonly object _gate = new();
        private readonly SemaphoreSlim _semaphoreSlim;
        // Includes holders, waiters, and callers that have not reached Wait yet.
        private int _reservations;
        private bool _retired;
        private long _idleSince = Environment.TickCount64;

        internal Lock(int maxConcurrentCalls = 1)
        {
            _semaphoreSlim = new SemaphoreSlim(maxConcurrentCalls, maxConcurrentCalls);
        }

        internal long IdleSince
        {
            get { lock (_gate) { return _idleSince; } }
        }

        internal bool TryReserve()
        {
            lock (_gate)
            {
                if (_retired)
                {
                    return false;
                }

                ++_reservations;
                return true;
            }
        }

        internal void Wait(CancellationToken cancellationToken)
        {
            try
            {
                _semaphoreSlim.Wait(cancellationToken);
            }
            catch
            {
                CancelReservation();
                throw;
            }
        }

        internal async Task WaitAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                CancelReservation();
                throw;
            }
        }

        internal void Release()
        {
            // Keep the reservation until Release has finished touching the semaphore.
            _semaphoreSlim.Release();
            CancelReservation();
        }

        internal void CancelReservation()
        {
            lock (_gate)
            {
                if (--_reservations == 0)
                {
                    _idleSince = Environment.TickCount64;
                }
            }
        }

        internal bool TryRetire(LockKey key, Lazy<Lock> entry, long timestamp)
        {
            lock (_gate)
            {
                if (_retired || _reservations != 0 || timestamp - _idleSince < IdleTimeoutMilliseconds)
                {
                    return false;
                }

                // Reservation, retirement, identity-checked removal and disposal share the gate.
                // A caller holding an old dictionary value must fail TryReserve and retry.
                _retired = true;
                ((ICollection<KeyValuePair<LockKey, Lazy<Lock>>>)Locks).Remove(new(key, entry));
                _semaphoreSlim.Dispose();
                return true;
            }
        }

        internal bool TryGetState(out int state)
        {
            lock (_gate)
            {
                state = _retired ? 0 : _semaphoreSlim.CurrentCount;
                return !_retired;
            }
        }

        internal int GetState()
        {
            lock (_gate)
            {
                return _semaphoreSlim.CurrentCount;
            }
        }
    }
}
