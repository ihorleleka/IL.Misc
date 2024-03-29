[![NuGet version (IL.Misc)](https://img.shields.io/nuget/v/IL.Misc.svg?style=flat-square)](https://www.nuget.org/packages/IL.Misc/)
# Misc & helpers

## Concurrency - Lock Manager

Tiny lock manager implementation incapsulating SemaphoreSlim. Thread safe aquire of lock implementing double-checked locking.

Usage sample:

```
using (await LockManager.GetLockAsync("testKey"))
{
  //code inside is limited to single thread usage only
}
```
```
using (await LockManager.GetLockAsync("testKey", maxConcurrentCalls = 4))
{
  //code inside is limited to 4 threads, all other threads will be awaiting..
}
```

Also supports `cancellationToken` param.

Function `LockManager.IsLockAvailable(string key)` returns true if Lock has any available slots on semaphore or not created at all.

## Nuget
  https://www.nuget.org/packages/IL.Misc/
