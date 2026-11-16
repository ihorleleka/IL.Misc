using System.Diagnostics;
using System.Reflection;
using IL.Misc.Concurrency;
using Xunit;

namespace IL.Misc.Tests.Concurrency;

public class LockManagerTests
{
    private const int TestDefaultDelay = 110;
    private const int ExpectedElapsedAtLeast = 300;

    [Fact]
    public void ConcatenatedKeys_DoNotShareLocks()
    {
        var key = Guid.NewGuid().ToString();
        using var first = (LockManager.LockReleaser)LockManager.GetLock(key + "1", 2);
        using var second = (LockManager.LockReleaser)LockManager.GetLock(key, 12);

        Assert.NotSame(first.InternalLock, second.InternalLock);
        Assert.Equal(1, first.InternalLock.GetState());
        Assert.Equal(11, second.InternalLock.GetState());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidConcurrencyLimit_IsRejectedByAllEntryPoints(int limit)
    {
        var key = Guid.NewGuid().ToString();
        Assert.Throws<ArgumentOutOfRangeException>(() => LockManager.IsLockAvailable(key, limit));
        Assert.Throws<ArgumentOutOfRangeException>(() => LockManager.GetLock(key, limit));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => LockManager.GetLockAsync(key, limit));
        Assert.Null(LockManager.GetCachedLock(key, limit));
    }

    [Fact]
    public void SameKey_WithDifferentLimits_DoesNotShareLocks()
    {
        var key = Guid.NewGuid().ToString();
        using var first = (LockManager.LockReleaser)LockManager.GetLock(key);
        using var second = (LockManager.LockReleaser)LockManager.GetLock(key, 2);

        Assert.NotSame(first.InternalLock, second.InternalLock);
        Assert.False(LockManager.IsLockAvailable(key));
        Assert.True(LockManager.IsLockAvailable(key, 2));
    }

    [Fact]
    public void IdleEntry_IsReusedForOneMinute_ThenRetiredAndReplaced()
    {
        var key = Guid.NewGuid().ToString();
        var first = (LockManager.LockReleaser)LockManager.GetLock(key);
        first.Dispose();
        var entry = first.InternalLock;

        Assert.False(LockManager.TryEvict(key, 1, entry.IdleSince + 59_999));
        var reused = (LockManager.LockReleaser)LockManager.GetLock(key);
        Assert.Same(entry, reused.InternalLock);
        reused.Dispose();
        Assert.False(LockManager.TryEvict(key, 1, entry.IdleSince + 59_999));
        Assert.True(LockManager.TryEvict(key, 1, entry.IdleSince + 60_000));
        Assert.Null(LockManager.GetCachedLock(key));
        Assert.False(entry.TryReserve());
        Assert.True(LockManager.IsLockAvailable(key));

        using var replacement = (LockManager.LockReleaser)LockManager.GetLock(key);
        Assert.NotSame(entry, replacement.InternalLock);
        // A stale retirement attempt cannot remove the replacement.
        Assert.False(entry.TryRetire(new LockManager.LockKey(key, 1), new Lazy<LockManager.Lock>(() => entry), long.MaxValue));
        Assert.Same(replacement.InternalLock, LockManager.GetCachedLock(key));
    }

    [Fact]
    public void ReservationBeforeSemaphoreWait_PreventsRetirement()
    {
        var key = Guid.NewGuid().ToString();
        var first = (LockManager.LockReleaser)LockManager.GetLock(key);
        first.Dispose();
        var entry = first.InternalLock;
        var reserved = LockManager.AcquireLock(key, 1);
        try
        {
            Assert.Same(entry, reserved);
            Assert.False(LockManager.TryEvict(key, 1, entry.IdleSince + 60_000));
            Assert.Same(entry, LockManager.GetCachedLock(key));
            reserved.Wait(CancellationToken.None);
        }
        catch
        {
            reserved.CancelReservation();
            throw;
        }

        new LockManager.LockReleaser(reserved).Dispose();
        Assert.True(LockManager.TryEvict(key, 1, entry.IdleSince + 60_000));
    }

    [Fact]
    public async Task RetirementAfterLookup_BeforeReservation_RetriesWithReplacement()
    {
        var key = Guid.NewGuid().ToString();
        var first = (LockManager.LockReleaser)LockManager.GetLock(key);
        first.Dispose();
        var oldEntry = first.InternalLock;
        var lookedUp = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var acquisition = Task.Run(() => LockManager.AcquireLock(key, 1, candidate =>
        {
            if (ReferenceEquals(candidate, oldEntry))
            {
                lookedUp.TrySetResult(true);
                if (!resume.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Acquisition was not resumed.");
                }
            }
        }));

        LockManager.Lock? replacement = null;
        try
        {
            await lookedUp.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(LockManager.TryEvict(key, 1, oldEntry.IdleSince + 60_000));
            resume.Set();
            replacement = await acquisition.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotSame(oldEntry, replacement);
            Assert.Same(replacement, LockManager.GetCachedLock(key));
            // Reservation also protects the retry result before its semaphore wait.
            Assert.False(LockManager.TryEvict(key, 1, replacement.IdleSince + 60_000));
        }
        finally
        {
            resume.Set();
            replacement ??= await acquisition.WaitAsync(TimeSpan.FromSeconds(10));
            replacement.CancelReservation();
        }
        using var lease = (LockManager.LockReleaser)await LockManager.GetLockAsync(key);
        Assert.Same(replacement, lease.InternalLock);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOnlyEntry_IsEventuallyRetired(bool asynchronous)
    {
        var key = Guid.NewGuid().ToString();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        if (asynchronous)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LockManager.GetLockAsync(key, cancellationToken: cancellation.Token));
        }
        else
        {
            Assert.ThrowsAny<OperationCanceledException>(() => LockManager.GetLock(key, cancellationToken: cancellation.Token));
        }

        var entry = Assert.IsType<LockManager.Lock>(LockManager.GetCachedLock(key));
        Assert.Equal(1, entry.GetState());
        Assert.False(LockManager.TryEvict(key, 1, entry.IdleSince + 59_999));
        Assert.True(LockManager.TryEvict(key, 1, entry.IdleSince + 60_000));
        Assert.Null(LockManager.GetCachedLock(key));
    }

    [Fact]
    public async Task CanceledQueuedWaiter_ReleasesOnlyItsReservation()
    {
        var key = Guid.NewGuid().ToString();
        using var holder = (LockManager.LockReleaser)await LockManager.GetLockAsync(key);
        using var cancellation = new CancellationTokenSource();
        var waiting = LockManager.GetLockAsync(key, cancellationToken: cancellation.Token);
        Assert.False(waiting.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        var entry = holder.InternalLock;
        Assert.False(LockManager.IsLockAvailable(key));
        Assert.False(LockManager.TryEvict(key, 1, entry.IdleSince + 60_000));
        holder.Dispose();
        Assert.Equal(1, entry.GetState());
        Assert.True(LockManager.TryEvict(key, 1, entry.IdleSince + 60_000));
    }

    [Fact]
    public async Task QueuedWaiter_PreventsRetirement_AcrossHolderRelease()
    {
        var key = Guid.NewGuid().ToString();
        using var holder = (LockManager.LockReleaser)await LockManager.GetLockAsync(key);
        var entry = holder.InternalLock;
        var waiting = LockManager.GetLockAsync(key);
        Assert.False(waiting.IsCompleted);
        Assert.False(LockManager.TryEvict(key, 1, entry.IdleSince + 60_000));
        holder.Dispose();
        // Whether the continuation has run or not, the waiter's reservation protects this entry.
        Assert.False(LockManager.TryEvict(key, 1, entry.IdleSince + 60_000));
        using var next = (LockManager.LockReleaser)await waiting;
        Assert.Same(entry, next.InternalLock);
        next.Dispose();
        Assert.True(LockManager.TryEvict(key, 1, entry.IdleSince + 60_000));
    }

    [Fact]
    public void ConcurrentRepeatedDisposal_ReleasesExactlyOneSlot()
    {
        var key = Guid.NewGuid().ToString();
        var lease = (LockManager.LockReleaser)LockManager.GetLock(key, 2);
        Parallel.For(0, 32, _ => lease.Dispose());
        Assert.Equal(2, lease.InternalLock.GetState());
        Assert.True(LockManager.TryEvict(key, 2, lease.InternalLock.IdleSince + 60_000));
        lease.Dispose(); // Including disposal after the underlying semaphore has retired.
    }

    [Fact]
    public async Task AsyncWaiters_RespectConcurrencyLimit()
    {
        const int limit = 3;
        var key = Guid.NewGuid().ToString();
        var initial = Enumerable.Range(0, limit).Select(_ => LockManager.GetLock(key, limit)).ToArray();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var entered = 0;
        var workers = Enumerable.Range(0, 20).Select(async _ =>
        {
            using var lease = await LockManager.GetLockAsync(key, limit);
            var concurrent = Interlocked.Increment(ref active);
            try
            {
                Assert.InRange(concurrent, 1, limit);
                if (Interlocked.Increment(ref entered) == limit)
                {
                    firstWave.TrySetResult(true);
                }
                await release.Task;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }).ToArray();

        try
        {
            Assert.All(workers, task => Assert.False(task.IsCompleted));
            foreach (var lease in initial)
            {
                lease.Dispose();
            }
            await firstWave.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(limit, Volatile.Read(ref active));
        }
        finally
        {
            foreach (var lease in initial)
            {
                lease.Dispose();
            }
            release.TrySetResult(true);
            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(0, active);
        Assert.Equal(20, entered);
    }

    [Fact]
    public void GetLock_SameKey_ReturnsSameInstances_Of_Lock_Due_To_Self_Removal_Delay_When_ConcurrencyLevelIs_0()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;

        // Act
        var lock1 = (LockManager.LockReleaser)LockManager.GetLock(key);
        lock1.Dispose();
        var lock2 = (LockManager.LockReleaser)LockManager.GetLock(key);
        lock2.Dispose();

        // Assert
        Assert.Same(lock1.InternalLock, lock2.InternalLock);
    }

    [Fact]
    public void IsAvailable_ReturnsTrue_If_Lock_Is_Missing()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;

        // Act
        var available = LockManager.IsLockAvailable(key);

        // Assert
        Assert.True(available);
    }

    [Fact]
    public void IsAvailable_ReturnsTrue_If_Lock_Has_Free_Slots()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;

        // Act
        var lock1 = LockManager.GetLock(key, 3);
        var lock2 = LockManager.GetLock(key, 3);

        lock1.Dispose();
        var available = LockManager.IsLockAvailable(key);
        lock2.Dispose();

        // Assert
        Assert.True(available);
    }

    [Fact]
    public void IsAvailable_Returns_False_If_Lock_Does_Not_Have_Free_Slots()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;

        // Act
        var lock1 = LockManager.GetLock(key, 2);
        var lock2 = LockManager.GetLock(key, 2);

        var available = LockManager.IsLockAvailable(key, 2);

        lock1.Dispose();
        lock2.Dispose();

        // Assert
        Assert.False(available);
    }

    [Fact]
    public async Task GetLockAsync_SameKey_ReturnsSameInstances_Of_Lock_Due_To_Self_Removal_Delay_When_ConcurrencyLevelIs_0()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;

        // Act
        var lock1 = (LockManager.LockReleaser)await LockManager.GetLockAsync(key);
        lock1.Dispose();
        var lock2 = (LockManager.LockReleaser)await LockManager.GetLockAsync(key);
        lock2.Dispose();

        // Assert
        Assert.Same(lock1.InternalLock, lock2.InternalLock);
    }

    [Fact]
    public void GetLock_DifferentKeys_ReturnsDifferentInstances()
    {
        // Arrange
        var key1 = MethodBase.GetCurrentMethod()!.Name + "1";
        var key2 = MethodBase.GetCurrentMethod()!.Name + "2";

        // Act
        var lock1 = (LockManager.LockReleaser)LockManager.GetLock(key1);
        lock1.Dispose();
        var lock2 = (LockManager.LockReleaser)LockManager.GetLock(key2);
        lock2.Dispose();

        // Assert
        Assert.NotSame(lock1.InternalLock, lock2.InternalLock);
    }

    [Fact]
    public async Task GetLockAsync_DifferentKeys_ReturnsDifferentInstances()
    {
        // Arrange
        var key1 = MethodBase.GetCurrentMethod()!.Name + "1";
        var key2 = MethodBase.GetCurrentMethod()!.Name + "2";

        // Act
        var lock1 = (LockManager.LockReleaser)await LockManager.GetLockAsync(key1);
        lock1.Dispose();
        var lock2 = (LockManager.LockReleaser)await LockManager.GetLockAsync(key2);
        lock2.Dispose();

        // Assert
        Assert.NotSame(lock1.InternalLock, lock2.InternalLock);
    }

    [Fact]
    public void GetLock_Dispose_Releases_Lock_But_Allows_To_Reuse_It()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;
        LockManager.LockReleaser lockObj1;
        LockManager.LockReleaser lockObj2;
        int countInsideUsing1;
        int countOutsideUsing1;
        int countInsideUsing2;
        int countOutsideUsing2;
        // Act
        using (lockObj1 = (LockManager.LockReleaser)LockManager.GetLock(key, 2))
        {
            // Assert
            Assert.NotNull(lockObj1);
            countInsideUsing1 = lockObj1.InternalLock.GetState();

            using (lockObj2 = (LockManager.LockReleaser)LockManager.GetLock(key, 2))
            {
                // Assert
                Assert.NotNull(lockObj2);
                countInsideUsing2 = lockObj2.InternalLock.GetState();
            }

            // Lock should be disposed at this point
            countOutsideUsing2 = lockObj2.InternalLock.GetState();
        }

        // Lock should be disposed at this point
        countOutsideUsing1 = lockObj1.InternalLock.GetState();


        // Act & Assert
        Assert.NotEqual(countInsideUsing1, countOutsideUsing1);
        Assert.NotEqual(countInsideUsing2, countOutsideUsing2);

        Assert.Equal(countInsideUsing1, countOutsideUsing2);

        Assert.Same(lockObj1.InternalLock, lockObj2.InternalLock);
    }

    [Fact]
    public async Task GetLockAsync_Dispose_Releases_Lock_But_Allows_To_Reuse_It()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;
        LockManager.LockReleaser lockObj1;
        LockManager.LockReleaser lockObj2;
        int countInsideUsing1;
        int countOutsideUsing1;
        int countInsideUsing2;
        int countOutsideUsing2;
        // Act
        using (lockObj1 = (LockManager.LockReleaser)await LockManager.GetLockAsync(key, 2))
        {
            // Assert
            Assert.NotNull(lockObj1);
            countInsideUsing1 = lockObj1.InternalLock.GetState();


            using (lockObj2 = (LockManager.LockReleaser)await LockManager.GetLockAsync(key, maxConcurrentCalls: 2))
            {
                // Assert
                Assert.NotNull(lockObj2);
                countInsideUsing2 = lockObj2.InternalLock.GetState();
            }

            // Lock should be disposed at this point
            countOutsideUsing2 = lockObj2.InternalLock.GetState();
        }

        // Lock should be disposed at this point
        countOutsideUsing1 = lockObj1.InternalLock.GetState();

        // Act & Assert
        Assert.NotEqual(countInsideUsing1, countOutsideUsing1);
        Assert.NotEqual(countInsideUsing2, countOutsideUsing2);

        Assert.Equal(countInsideUsing1, countOutsideUsing2);

        Assert.Same(lockObj1.InternalLock, lockObj2.InternalLock);
    }

    [Fact]
    public async Task GetLockAsync_SameKey_ReturnsSameInstances_Of_Lock_If_Other_Threads_Awaiting()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;
        LockManager.LockReleaser? lock1 = default;
        LockManager.LockReleaser? lock2 = default;
        LockManager.LockReleaser? lock3 = default;

        // Act
        var tasks = new[]
        {
            new Task(() =>
            {
                using (lock1 = (LockManager.LockReleaser)LockManager.GetLock(key))
                {
                    Task.Delay(TestDefaultDelay).Wait();
                }
            }),
            new Task(() =>
            {
                using (lock2 = (LockManager.LockReleaser)LockManager.GetLock(key))
                {
                    Task.Delay(TestDefaultDelay).Wait();
                }
            }),
            new Task(() =>
            {
                using (lock3 = (LockManager.LockReleaser)LockManager.GetLock(key))
                {
                    Task.Delay(TestDefaultDelay).Wait();
                }
            })
        };

#if NET7_0_OR_GREATER
        var start = Stopwatch.GetTimestamp();
#else
        var start = Stopwatch.StartNew();
#endif
        foreach (var task in tasks)
        {
            task.Start();
        }
        await Task.WhenAll(tasks);
#if NET7_0_OR_GREATER
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
#else
        var elapsed = start.ElapsedMilliseconds;
#endif

        // Assert
        Assert.Same(lock1!.InternalLock, lock2!.InternalLock);
        Assert.Same(lock1.InternalLock, lock3!.InternalLock);
        Assert.Same(lock2.InternalLock, lock3.InternalLock);
        Assert.InRange(elapsed, ExpectedElapsedAtLeast, long.MaxValue);
    }

    [Fact]
    public async Task GetLock_SameKey_ReturnsSameInstances_Of_Lock_If_Other_Threads_Awaiting()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;
        LockManager.LockReleaser? lock1 = default;
        LockManager.LockReleaser? lock2 = default;
        LockManager.LockReleaser? lock3 = default;

        // Act
        var tasks = new[]
        {
            new Task(() =>
            {
                using (lock1 = (LockManager.LockReleaser)LockManager.GetLock(key))
                {
                    Task.Delay(TestDefaultDelay).Wait();
                }
            }),
            new Task(() =>
            {
                using (lock2 = (LockManager.LockReleaser)LockManager.GetLock(key))
                {
                    Task.Delay(TestDefaultDelay).Wait();
                }
            }),
            new Task(() =>
            {
                using (lock3 = (LockManager.LockReleaser)LockManager.GetLock(key))
                {
                    Task.Delay(TestDefaultDelay).Wait();
                }
            })
        };

#if NET7_0_OR_GREATER
        var start = Stopwatch.GetTimestamp();
#else
        var start = Stopwatch.StartNew();
#endif
        foreach (var task in tasks)
        {
            task.Start();
        }
        await Task.WhenAll(tasks);
#if NET7_0_OR_GREATER
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
#else
        var elapsed = start.ElapsedMilliseconds;
#endif

        // Assert
        Assert.Same(lock1!.InternalLock, lock2!.InternalLock);
        Assert.Same(lock1.InternalLock, lock3!.InternalLock);
        Assert.Same(lock2.InternalLock, lock3.InternalLock);
        Assert.InRange(elapsed, ExpectedElapsedAtLeast, long.MaxValue);
    }
}