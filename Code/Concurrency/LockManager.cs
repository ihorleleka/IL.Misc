using System.Collections.Concurrent;

namespace IL.Misc.Concurrency;

public static class LockManager
{
    private static readonly ConcurrentDictionary<string, Lazy<Lock>> Locks = new();

    public static IDisposable GetLock(string key, int maxConcurrentCalls = 1, CancellationToken cancellationToken = default)
    {
        var concurrentLock = AcquireLock(key, maxConcurrentCalls);
        concurrentLock.Wait(cancellationToken);
        return concurrentLock;
    }

    public static async Task<IDisposable> GetLockAsync(string key, int maxConcurrentCalls = 1, CancellationToken cancellationToken = default)
    {
        var concurrentLock = AcquireLock(key, maxConcurrentCalls);
        await concurrentLock.WaitAsync(cancellationToken);
        return concurrentLock;
    }

    private static Lock AcquireLock(string key, int maxConcurrentCalls)
    {
        var lazyLock = Locks.GetOrAdd(key,
            new Lazy<Lock>(() => new Lock(maxConcurrentCalls,
                () => { Locks.TryRemove(key, out _); })
            )
        );
        var concurrentLock = lazyLock.Value;
        return concurrentLock;
    }

    internal sealed class Lock : IDisposable
    {
        private readonly SemaphoreSlim _semaphoreSlim;
        private readonly Action? _selfDeletionAction;
        private readonly int _maxConcurrentCalls;

        internal Lock(int maxConcurrentCalls = 1, Action? selfDeletionAction = null)
        {
            _semaphoreSlim = new SemaphoreSlim(maxConcurrentCalls, maxConcurrentCalls);
            _selfDeletionAction = selfDeletionAction;
            _maxConcurrentCalls = maxConcurrentCalls;
        }

        internal void Wait(CancellationToken cancellationToken)
        {
            _semaphoreSlim.Wait(cancellationToken);
        }

        internal async Task WaitAsync(CancellationToken cancellationToken)
        {
            await _semaphoreSlim.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            _semaphoreSlim.Release();
            if (GetState() == _maxConcurrentCalls)
            {
                _selfDeletionAction?.Invoke();
            }
        }

        internal int GetState()
        {
            return _semaphoreSlim.CurrentCount;
        }
    }
}