using System.Collections.Concurrent;

namespace IL.Misc.Concurrency;

public static class LockManager
{
    private static readonly ConcurrentDictionary<string, Lazy<Lock>> Locks = new();

    public static IDisposable GetLock(string key, int initialConcurrentCalls = 1, int maxConcurrentCalls = 1, CancellationToken cancellationToken = default)
    {
        var concurrentLock = AcquireLock(key, initialConcurrentCalls, maxConcurrentCalls);
        concurrentLock.Wait(cancellationToken);
        return concurrentLock;
    }

    public static async Task<IDisposable> GetLockAsync(string key, int initialConcurrentCalls = 1, int maxConcurrentCalls = 1, CancellationToken cancellationToken = default)
    {
        var concurrentLock = AcquireLock(key, initialConcurrentCalls, maxConcurrentCalls);
        await concurrentLock.WaitAsync(cancellationToken);
        return concurrentLock;
    }

    private static Lock AcquireLock(string key, int initialConcurrentCalls, int maxConcurrentCalls)
    {
        var lazyLock = Locks.GetOrAdd(key,
            new Lazy<Lock>(() => new Lock(initialConcurrentCalls,
                maxConcurrentCalls,
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
        private int _currentConcurrencyLevel;

        internal Lock(int initialConcurrentCalls = 1, int maxConcurrentCalls = 1, Action? selfDeletionAction = null)
        {
            _semaphoreSlim = new SemaphoreSlim(initialConcurrentCalls, maxConcurrentCalls);
            _selfDeletionAction = selfDeletionAction;
            _currentConcurrencyLevel = 0;
        }

        public void Wait(CancellationToken cancellationToken)
        {
            _currentConcurrencyLevel++;
            _semaphoreSlim.Wait(cancellationToken);
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            _currentConcurrencyLevel++;
            await _semaphoreSlim.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            _semaphoreSlim.Release();
            _currentConcurrencyLevel--;
            if (_currentConcurrencyLevel == 0)
            {
                _selfDeletionAction?.Invoke();
            }
        }

        public int GetState()
        {
            return _semaphoreSlim.CurrentCount;
        }

        public bool IsInUse()
        {
            return _currentConcurrencyLevel == 0;
        }
    }
}