# Misc & helpers

## Supported frameworks

Targets .NET 10 and 11. The .NET 10 target uses
Microsoft.Extensions.DependencyInjection.Abstractions 10.0.15 or later within
major version 10. .NET 11 includes these abstractions in Microsoft.NETCore.App,
so that target needs no separate package dependency.

Building both targets requires the .NET 11 SDK. Running the full test suite also
requires the .NET 10 and 11 runtimes.

## Concurrency - Lock Manager

Process-local keyed concurrency control built on `SemaphoreSlim`. A key and concurrency limit together identify a lock; different limits are intentionally independent. Availability is advisory, not a reservation. Idle entries are retired after approximately one minute by a shared cleanup timer; queued waiters and in-progress acquisitions prevent retirement.

Usage sample:

```
using (await LockManager.GetLockAsync("testKey"))
{
  //code inside is limited to single thread usage only
}
```
```
using (await LockManager.GetLockAsync("testKey", maxConcurrentCalls: 4))
{
  //code inside is limited to 4 threads, all other threads will be awaiting..
}
```

Also supports `cancellationToken` param.

Function `LockManager.IsLockAvailable(string key)` returns true if Lock has any available slots on semaphore or not created at all.

## Ambient scopes

`AmbientScopeService.CreateToken(IServiceScope)` owns its scope. Enter a token with `using` (or `await using` for async-only scoped services). Default lease exit requests disposal and prevents reentry; cleanup runs exactly once after the final active lease exits. `Reusable` restores the previous ambient scope without requesting disposal. Token disposal while leases remain active is deferred and returns without waiting; the final lease determines sync versus async cleanup. Repeated disposal does not join pending asynchronous cleanup.

Dispose leases in reverse entry order in their originating logical flow. Each parallel user must enter independently: inherited ambient context does not extend scope lifetime. The `IServiceProvider` overload does not own the provider and cannot reliably distinguish root from scoped providers.

## Reflection and wildcard helpers

Assembly filters support case-sensitive `*` and `?`. References are filtered before loading, and unavailable references are skipped. Type discovery retains visible types after partial load failures; unexpected failures propagate instead of being hidden. Reflection discovery is annotated as unsafe for trimming; use explicit registration for trimmed/Native AOT applications.

Wildcard matching is allocation-free and ordinal over UTF-16 characters. Null inputs/patterns never match. Worst-case time is O(input length × pattern length), so bound untrusted input sizes.

## Nuget
  https://www.nuget.org/packages/IL.Misc/