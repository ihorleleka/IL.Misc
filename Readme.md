[![NuGet version (IL.Misc)](https://img.shields.io/nuget/v/IL.Misc.svg?style=flat-square)](https://www.nuget.org/packages/IL.Misc/)
# Misc & helpers

## Supported frameworks and development

The library and tests target .NET 10 and 11. The .NET 10 target uses DI abstractions
and implementation 10.0.15, with the library dependency constrained to major
version 10. .NET 11 includes DI abstractions in Microsoft.NETCore.App, so the
library needs no separate package dependency for that target; the tests use the
DI implementation package 11.0.0.

Use the .NET 11 SDK to build both targets, with the .NET 10 and 11 runtimes
installed to execute the complete test suite:

```sh
dotnet restore IL.Misc.slnx
dotnet build IL.Misc.slnx --configuration Release --no-restore
dotnet test IL.Misc.slnx --configuration Release --no-build --no-restore
dotnet test Tests/IL.Misc.Tests.csproj --configuration Release --no-build --no-restore --collect:"XPlat Code Coverage"
```

Build CI builds and tests both targets with coverage collection. The release
workflow also runs all tests before producing a package. The tests include real
DI scope lifetime and async disposal checks on each target. The project retains xUnit v2; its current VSTest
adapter supports xUnit v2 on all supported targets. Unused Moq and console-runner
dependencies have been removed.

## Release version policy

Local builds default to `0.0.0-dev`. Tag-triggered releases derive package and
assembly metadata from one validated tag: an optional `v`, a three- or four-part
numeric version, and optional prerelease/build metadata. The release helper
exports the version properties once before restore, build, test, and pack;
assembly/file versions use the numeric core, while informational versions retain
prerelease/build metadata without an appended source revision. Invalid tags fail
before building. Publishing remains tag-only.

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
  The previous ambient scope is restored on exit and token disposal is requested. New entries are then rejected; the underlying scope is disposed exactly once after the final active lease exits.
- `AmbientScopeEnterMode.Reusable`
  The previous ambient scope is restored on exit, but the underlying scope is kept alive for reuse.

### Recommended Usage

Prefer creating tokens from `IServiceScope`:

```csharp
using var token = AmbientScopeService.CreateToken(serviceProvider.CreateScope());

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

Nested enters restore the previous ambient scope when the inner lease exits. Dispose leases in reverse entry order; out-of-order disposal throws without closing the lease. Token disposal while leases are active requests cleanup without waiting; the final lease chooses synchronous or asynchronous cleanup. Use `await using` on that lease for scopes containing async-only services. Do not transfer leases between independent execution flows; each parallel user must acquire its own entry. Inheriting an ambient value does not extend the scope lifetime.

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
