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
    public void GetLock_SameKey_ReturnsSameInstances_Of_Lock_Due_To_Self_Removal_Delay_When_ConcurrencyLevelIs_0()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;

        // Act
        var lock1 = LockManager.GetLock(key);
        lock1.Dispose();
        var lock2 = LockManager.GetLock(key);
        lock2.Dispose();

        // Assert
        Assert.Same(lock1, lock2);
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
    public async Task GetLockAsync_SameKey_ReturnsNotSameInstances_Of_Lock_Due_To_Self_Removal_When_ConcurrencyLevelIs_0()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;

        // Act
        var lock1 = await LockManager.GetLockAsync(key);
        lock1.Dispose();
        var lock2 = await LockManager.GetLockAsync(key);
        lock2.Dispose();

        // Assert
        Assert.Same(lock1, lock2);
    }

    [Fact]
    public void GetLock_DifferentKeys_ReturnsDifferentInstances()
    {
        // Arrange
        var key1 = MethodBase.GetCurrentMethod()!.Name + "1";
        var key2 = MethodBase.GetCurrentMethod()!.Name + "2";

        // Act
        var lock1 = LockManager.GetLock(key1);
        lock1.Dispose();
        var lock2 = LockManager.GetLock(key2);
        lock2.Dispose();

        // Assert
        Assert.NotSame(lock1, lock2);
    }

    [Fact]
    public async Task GetLockAsync_DifferentKeys_ReturnsDifferentInstances()
    {
        // Arrange
        var key1 = MethodBase.GetCurrentMethod()!.Name + "1";
        var key2 = MethodBase.GetCurrentMethod()!.Name + "2";

        // Act
        var lock1 = await LockManager.GetLockAsync(key1);
        lock1.Dispose();
        var lock2 = await LockManager.GetLockAsync(key2);
        lock2.Dispose();

        // Assert
        Assert.NotSame(lock1, lock2);
    }

    [Fact]
    public void GetLock_Dispose_Releases_Lock_But_Allows_To_Reuse_It()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;
        IDisposable lockObj1;
        IDisposable lockObj2;
        int countInsideUsing1;
        int countOutsideUsing1;
        int countInsideUsing2;
        int countOutsideUsing2;
        // Act
        using (lockObj1 = LockManager.GetLock(key, 2))
        {
            // Assert
            Assert.NotNull(lockObj1);
            countInsideUsing1 = ((LockManager.Lock)lockObj1).GetState();

            using (lockObj2 = LockManager.GetLock(key, 2))
            {
                // Assert
                Assert.NotNull(lockObj2);
                countInsideUsing2 = ((LockManager.Lock)lockObj2).GetState();
            }

            // Lock should be disposed at this point
            countOutsideUsing2 = ((LockManager.Lock)lockObj2).GetState();
        }

        // Lock should be disposed at this point
        countOutsideUsing1 = ((LockManager.Lock)lockObj1).GetState();


        // Act & Assert
        Assert.NotEqual(countInsideUsing1, countOutsideUsing1);
        Assert.NotEqual(countInsideUsing2, countOutsideUsing2);

        Assert.Equal(countInsideUsing1, countOutsideUsing2);

        Assert.Same(lockObj1, lockObj2);
    }

    [Fact]
    public async Task GetLockAsync_Dispose_Releases_Lock_But_Allows_To_Reuse_It()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;
        IDisposable lockObj1;
        IDisposable lockObj2;
        int countInsideUsing1;
        int countOutsideUsing1;
        int countInsideUsing2;
        int countOutsideUsing2;
        // Act
        using (lockObj1 = await LockManager.GetLockAsync(key, 2))
        {
            // Assert
            Assert.NotNull(lockObj1);
            countInsideUsing1 = ((LockManager.Lock)lockObj1).GetState();


            using (lockObj2 = await LockManager.GetLockAsync(key, maxConcurrentCalls: 2))
            {
                // Assert
                Assert.NotNull(lockObj2);
                countInsideUsing2 = ((LockManager.Lock)lockObj2).GetState();
            }

            // Lock should be disposed at this point
            countOutsideUsing2 = ((LockManager.Lock)lockObj2).GetState();
        }

        // Lock should be disposed at this point
        countOutsideUsing1 = ((LockManager.Lock)lockObj1).GetState();

        // Act & Assert
        Assert.NotEqual(countInsideUsing1, countOutsideUsing1);
        Assert.NotEqual(countInsideUsing2, countOutsideUsing2);

        Assert.Equal(countInsideUsing1, countOutsideUsing2);

        Assert.Same(lockObj1, lockObj2);
    }

    [Fact]
    public async Task GetLockAsync_SameKey_ReturnsSameInstances_Of_Lock_If_Other_Threads_Awaiting()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;
        IDisposable? lock1 = default;
        IDisposable? lock2 = default;
        IDisposable? lock3 = default;

        // Act
        var tasks = new[]
        {
            new Task(() =>
            {
                using (lock1 = LockManager.GetLock(key))
                {
                    Task.Delay(TestDefaultDelay).Wait();
                }
            }),
            new Task(() =>
            {
                using (lock2 = LockManager.GetLock(key))
                {
                    Task.Delay(TestDefaultDelay).Wait();
                }
            }),
            new Task(() =>
            {
                using (lock3 = LockManager.GetLock(key))
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
        var elapsed = Stopwatch.GetElapsedTime(start).Milliseconds;
#else
        var elapsed = start.ElapsedMilliseconds;
#endif

        // Assert
        Assert.Same(lock1, lock2);
        Assert.Same(lock1, lock3);
        Assert.Same(lock2, lock3);
        Assert.InRange(elapsed, ExpectedElapsedAtLeast, long.MaxValue);
    }

    [Fact]
    public async Task GetLock_SameKey_ReturnsSameInstances_Of_Lock_If_Other_Threads_Awaiting()
    {
        // Arrange
        var key = MethodBase.GetCurrentMethod()!.Name;
        IDisposable? lock1 = default;
        IDisposable? lock2 = default;
        IDisposable? lock3 = default;

        // Act
        var tasks = new[]
        {
            new Task(() =>
            {
                using (lock1 = LockManager.GetLock(key))
                {
                    Task.Delay(TestDefaultDelay).Wait();
                }
            }),
            new Task(() =>
            {
                using (lock2 = LockManager.GetLock(key))
                {
                    Task.Delay(TestDefaultDelay).Wait();
                }
            }),
            new Task(() =>
            {
                using (lock3 = LockManager.GetLock(key))
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
        var elapsed = Stopwatch.GetElapsedTime(start).Milliseconds;
#else
        var elapsed = start.ElapsedMilliseconds;
#endif

        // Assert
        Assert.Same(lock1, lock2);
        Assert.Same(lock1, lock3);
        Assert.Same(lock2, lock3);
        Assert.InRange(elapsed, ExpectedElapsedAtLeast, long.MaxValue);
    }
}