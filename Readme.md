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

## Scopes - Ambient Scope Service

`AmbientScopeService` provides ambient access to the current DI scope through `AsyncLocal`.

It is intended for cases where the current scoped `IServiceProvider` must be available deep in the call chain without passing it through every method signature.

### Core API

- `AmbientScopeService.CurrentServiceProvider`
- `AmbientScopeService.CreateToken(IServiceScope scope)`
- `AmbientScopeService.CreateToken(IServiceProvider scopedServiceProvider)`
- `IAmbientScopeToken.Enter(AmbientScopeEnterMode mode = AmbientScopeEnterMode.DisposeOnExit)`

### Ownership Modes

- `AmbientScopeEnterMode.DisposeOnExit`
  The entered scope is restored on exit and the underlying scope is disposed.
- `AmbientScopeEnterMode.Reusable`
  The previous ambient scope is restored on exit, but the underlying scope is kept alive for reuse.

### Recommended Usage

Prefer creating tokens from `IServiceScope`:

```csharp
using var scope = serviceProvider.CreateScope();
var token = AmbientScopeService.CreateToken(scope);

using (token.Enter())
{
    var currentProvider = AmbientScopeService.CurrentServiceProvider;
    var myService = currentProvider.GetRequiredService<MyScopedService>();
}
```

Reusable mode is useful when the scope lifetime is owned elsewhere:

```csharp
using var scope = serviceProvider.CreateScope();
var token = AmbientScopeService.CreateToken(scope);

using (token.Enter(AmbientScopeEnterMode.Reusable))
{
    var currentProvider = AmbientScopeService.CurrentServiceProvider;
    var myService = currentProvider.GetRequiredService<MyScopedService>();
}

// scope is still alive here and can be re-entered again
```

### Async Behavior

The ambient scope flows across `await` because it is backed by `AsyncLocal`.

```csharp
using var scope = serviceProvider.CreateScope();
var token = AmbientScopeService.CreateToken(scope);

await using (token.Enter(AmbientScopeEnterMode.Reusable))
{
    await SomeAsyncOperation();

    var currentProvider = AmbientScopeService.CurrentServiceProvider;
    var myService = currentProvider.GetRequiredService<MyScopedService>();
}
```

Nested enters restore the previous ambient scope when the inner lease exits.

### Guidance and Caveats

- Prefer `CreateToken(IServiceScope)` for library and application code.
- `CreateToken(IServiceProvider)` should only be used when you already know the provider is scoped.
- The `IServiceProvider` overload depends on container behavior and cannot reliably distinguish a root provider from a scoped provider for every DI implementation.
- Ambient scopes are convenient, but they should not replace explicit dependency flow in highly concurrent or long-lived background work.
- The ambient value is tied to the logical async flow. Child tasks may inherit the current ambient scope when they are created.
- `CurrentServiceProvider` throws if no ambient scope is active.

### When To Use This

Good fit:

- request-oriented pipelines
- application services that already have a clear scoped lifetime
- helpers or infrastructure code that need access to the current scope without threading `IServiceProvider` everywhere

Not a good fit:

- arbitrary parallel fan-out work
- code that crosses lifetime boundaries unclearly
- places where explicit dependency passing is simpler and safer

## Nuget
  https://www.nuget.org/packages/IL.Misc/
