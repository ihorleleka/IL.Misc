using System.Collections.Concurrent;

namespace IL.Misc.Concurrency;

public static class LockManager
{
    private const int SelfDeletionDelayInMinutes = 1;
    private static readonly ConcurrentDictionary<string, Lazy<Lock>> Locks = new();

    /// <summary>
    /// By availability, it means Lock has any available slots on semaphore or not created at all.
    /// Locks with different concurrency level with be created as different locks
    /// </summary>
    /// <param name="key"></param>
    /// <param name="maxConcurrentCalls"></param>
    /// <returns></returns>
    public static bool IsLockAvailable(string key, int maxConcurrentCalls = 1)
    {
        var lockExists = Locks.TryGetValue($"{key}{maxConcurrentCalls}", out var concurrentLock);
        return !lockExists || concurrentLock?.Value.GetState() > 0;
    }

    public static IDisposable GetLock(string key, int maxConcurrentCalls = 1, CancellationToken cancellationToken = default)
    {
        var concurrentLock = AcquireLock(key, maxConcurrentCalls);
        concurrentLock.Wait(cancellationToken);
        return new LockReleaser(concurrentLock);
    }

    public static async Task<IDisposable> GetLockAsync(string key, int maxConcurrentCalls = 1, CancellationToken cancellationToken = default)
    {
        var concurrentLock = AcquireLock(key, maxConcurrentCalls);
        await concurrentLock.WaitAsync(cancellationToken);
        return new LockReleaser(concurrentLock);
    }

    private static Lock AcquireLock(string key, int maxConcurrentCalls)
    {
        var dictionaryKey = $"{key}{maxConcurrentCalls}";
        return Locks
            .GetOrAdd(dictionaryKey,
                k => new Lazy<Lock>(() => new Lock(maxConcurrentCalls, () => { Locks.TryRemove(k, out _); }), LazyThreadSafetyMode.ExecutionAndPublication)
            )
            .Value;
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
        private readonly SemaphoreSlim _semaphoreSlim;
        private readonly Action? _selfDeletionAction;
        private int _activeCount;
        private bool _scheduledForDeletion;

        internal Lock(int maxConcurrentCalls = 1, Action? selfDeletionAction = null)
        {
            _semaphoreSlim = new SemaphoreSlim(maxConcurrentCalls, maxConcurrentCalls);
            _selfDeletionAction = selfDeletionAction;
        }

        internal void Wait(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _activeCount);
            try
            {
                _semaphoreSlim.Wait(cancellationToken);
            }
            catch
            {
                Interlocked.Decrement(ref _activeCount);
                throw;
            }
        }

        internal async Task WaitAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _activeCount);
            try
            {
                await _semaphoreSlim.WaitAsync(cancellationToken);
            }
            catch
            {
                Interlocked.Decrement(ref _activeCount);
                throw;
            }
        }

        internal void Release()
        {
            _semaphoreSlim.Release();
            if (Interlocked.Decrement(ref _activeCount) == 0)
            {
                ScheduleEviction();
            }
        }

        private void ScheduleEviction()
        {
            lock (this)
            {
                if (_scheduledForDeletion)
                {
                    return;
                }
                _scheduledForDeletion = true;
            }

            Task
                .Delay(TimeSpan.FromMinutes(SelfDeletionDelayInMinutes))
                .ContinueWith(_ =>
                {
                    var shouldEvict = false;
                    lock (this)
                    {
                        if (_scheduledForDeletion && Volatile.Read(ref _activeCount) == 0)
                        {
                            shouldEvict = true;
                        }
                        else
                        {
                            _scheduledForDeletion = false;
                        }
                    }

                    if (!shouldEvict)
                    {
                        return;
                    }

                    _selfDeletionAction?.Invoke();
                    _semaphoreSlim.Dispose();
                });
        }

        internal int GetState() => _semaphoreSlim.CurrentCount;
    }
}