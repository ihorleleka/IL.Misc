using IL.Misc.Scopes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IL.Misc.Tests.Scopes;

public class AmbientScopeServiceTests
{
    [Fact]
    public void Enter_DisposeOnExit_PushesScope_AndDisposesIt_OnExit()
    {
        var tracked = new TrackedScopedDependency();
        var services = new ServiceCollection()
            .AddScoped(_ => tracked)
            .BuildServiceProvider();

        var token = AmbientScopeService.CreateToken(services.CreateScope());

        using (token.Enter())
        {
            var resolved = AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>();
            Assert.Same(tracked, resolved);
            Assert.False(tracked.IsDisposed);
        }

        Assert.True(tracked.IsDisposed);
        Assert.Throws<InvalidOperationException>(() => AmbientScopeService.CurrentServiceProvider);
    }

    [Fact]
    public void Enter_Reusable_RestoresPreviousScope_WithoutDisposingUnderlyingScope()
    {
        var outerTracked = new TrackedScopedDependency();
        var innerTracked = new TrackedScopedDependency();
        var services = new ServiceCollection();

        using var outerScope = CreateTrackedScope(outerTracked);
        using var innerScope = CreateTrackedScope(innerTracked);

        var outerToken = AmbientScopeService.CreateToken(outerScope);
        var innerToken = AmbientScopeService.CreateToken(innerScope);

        using var outerEntry = outerToken.Enter(AmbientScopeEnterMode.Reusable);
        Assert.Same(outerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());

        using (innerToken.Enter(AmbientScopeEnterMode.Reusable))
        {
            Assert.Same(innerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
        }

        Assert.False(innerTracked.IsDisposed);
        Assert.Same(outerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());

        outerEntry.Dispose();

        Assert.False(outerTracked.IsDisposed);
        Assert.Throws<InvalidOperationException>(() => AmbientScopeService.CurrentServiceProvider);
    }

    [Fact]
    public void Token_Dispose_PreventsFutureEntry()
    {
        var tracked = new TrackedScopedDependency();
        var services = new ServiceCollection()
            .AddScoped(_ => tracked)
            .BuildServiceProvider();

        using var scope = services.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<TrackedScopedDependency>();

        var token = AmbientScopeService.CreateToken(scope);
        token.Dispose();

        Assert.True(tracked.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => token.Enter());
    }

    [Fact]
    public void CreateToken_FromScopedProvider_DoesNotOwnScopeLifetime()
    {
        var tracked = new TrackedScopedDependency();
        using var scope = CreateTrackedScope(tracked);

        var token = AmbientScopeService.CreateToken(scope.ServiceProvider);

        using (token.Enter())
        {
            Assert.Same(tracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
        }

        Assert.False(tracked.IsDisposed);

        token.Dispose();

        Assert.False(tracked.IsDisposed);
        scope.Dispose();
        Assert.True(tracked.IsDisposed);
    }

    [Fact]
    public async Task Enter_DisposeAsync_DisposeOnExit_RestoresPreviousScope_AndDisposesScope()
    {
        var outerTracked = new TrackedScopedDependency();
        var innerTracked = new TrackedScopedDependency();
        using var outerScope = CreateTrackedScope(outerTracked);
        using var innerScope = CreateTrackedScope(innerTracked);
        var outerToken = AmbientScopeService.CreateToken(outerScope);
        var innerToken = AmbientScopeService.CreateToken(innerScope);

        using var outerEntry = outerToken.Enter(AmbientScopeEnterMode.Reusable);
        var outerProvider = AmbientScopeService.CurrentServiceProvider;

        var entry = innerToken.Enter();
        Assert.Same(innerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());

        await entry.DisposeAsync();

        Assert.True(innerTracked.IsDisposed);
        Assert.Same(outerProvider, AmbientScopeService.CurrentServiceProvider);
        Assert.Same(outerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
    }

    [Fact]
    public async Task Enter_DisposeAsync_Reusable_RestoresPreviousScope_WithoutDisposingEnteredScope()
    {
        var outerTracked = new TrackedScopedDependency();
        var innerTracked = new TrackedScopedDependency();
        using var outerScope = CreateTrackedScope(outerTracked);
        using var innerScope = CreateTrackedScope(innerTracked);

        var outerToken = AmbientScopeService.CreateToken(outerScope);
        var innerToken = AmbientScopeService.CreateToken(innerScope);

        await using var outerEntry = outerToken.Enter(AmbientScopeEnterMode.Reusable);
        var outerProvider = AmbientScopeService.CurrentServiceProvider;
        Assert.Same(outerTracked, outerProvider.GetRequiredService<TrackedScopedDependency>());

        var innerEntry = innerToken.Enter(AmbientScopeEnterMode.Reusable);
        Assert.Same(innerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());

        await innerEntry.DisposeAsync();

        Assert.False(innerTracked.IsDisposed);
        Assert.Same(outerProvider, AmbientScopeService.CurrentServiceProvider);
        Assert.Same(outerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
    }

    [Fact]
    public void Nested_MixedModes_RestoreAndDisposeAccordingToMode()
    {
        var outerTracked = new TrackedScopedDependency();
        var innerTracked = new TrackedScopedDependency();
        using var outerScope = CreateTrackedScope(outerTracked);
        using var innerScope = CreateTrackedScope(innerTracked);

        var outerToken = AmbientScopeService.CreateToken(outerScope);
        var innerToken = AmbientScopeService.CreateToken(innerScope);

        using (outerToken.Enter(AmbientScopeEnterMode.Reusable))
        {
            Assert.Same(outerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());

            using (innerToken.Enter(AmbientScopeEnterMode.DisposeOnExit))
            {
                Assert.Same(innerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
            }

            Assert.True(innerTracked.IsDisposed);
            Assert.Same(outerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
        }

        Assert.False(outerTracked.IsDisposed);
        Assert.Throws<InvalidOperationException>(() => AmbientScopeService.CurrentServiceProvider);
    }

    [Fact]
    public void Nested_DefaultOuter_ReusableInner_RestoresOuterAndDisposesOnlyOuterOnExit()
    {
        var outerTracked = new TrackedScopedDependency();
        var innerTracked = new TrackedScopedDependency();
        using var outerScope = CreateTrackedScope(outerTracked);
        using var innerScope = CreateTrackedScope(innerTracked);

        var outerToken = AmbientScopeService.CreateToken(outerScope);
        var innerToken = AmbientScopeService.CreateToken(innerScope);

        using (outerToken.Enter())
        {
            Assert.Same(outerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());

            using (innerToken.Enter(AmbientScopeEnterMode.Reusable))
            {
                Assert.Same(innerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
            }

            Assert.False(innerTracked.IsDisposed);
            Assert.Same(outerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
        }

        Assert.True(outerTracked.IsDisposed);
        Assert.False(innerTracked.IsDisposed);
        Assert.Throws<InvalidOperationException>(() => AmbientScopeService.CurrentServiceProvider);
    }

    [Fact]
    public void ReenteringSameToken_WithReusableLeases_RestoresPreviousLeaseOrder()
    {
        var tracked = new TrackedScopedDependency();
        using var scope = CreateTrackedScope(tracked);
        var token = AmbientScopeService.CreateToken(scope);

        using var outerEntry = token.Enter(AmbientScopeEnterMode.Reusable);
        var outerProvider = AmbientScopeService.CurrentServiceProvider;

        using (token.Enter(AmbientScopeEnterMode.Reusable))
        {
            Assert.Same(tracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
            Assert.Same(outerProvider, AmbientScopeService.CurrentServiceProvider);
        }

        Assert.Same(outerProvider, AmbientScopeService.CurrentServiceProvider);
        Assert.False(tracked.IsDisposed);
    }

    [Fact]
    public void Dispose_IsIdempotent_ForLeaseAndToken()
    {
        var outerTracked = new TrackedScopedDependency();
        var tracked = new TrackedScopedDependency();
        using var outerScope = CreateTrackedScope(outerTracked);
        using var scope = CreateTrackedScope(tracked);
        var outerToken = AmbientScopeService.CreateToken(outerScope);
        var token = AmbientScopeService.CreateToken(scope);

        using var outerEntry = outerToken.Enter(AmbientScopeEnterMode.Reusable);
        var outerProvider = AmbientScopeService.CurrentServiceProvider;
        _ = scope.ServiceProvider.GetRequiredService<TrackedScopedDependency>();

        var entry = token.Enter(AmbientScopeEnterMode.Reusable);
        entry.Dispose();
        entry.Dispose();

        Assert.False(tracked.IsDisposed);
        Assert.Same(outerProvider, AmbientScopeService.CurrentServiceProvider);

        token.Dispose();
        token.Dispose();

        Assert.True(tracked.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent_ForLeaseAndToken()
    {
        var outerTracked = new TrackedScopedDependency();
        var tracked = new TrackedScopedDependency();
        using var outerScope = CreateTrackedScope(outerTracked);
        using var scope = CreateTrackedScope(tracked);
        var outerToken = AmbientScopeService.CreateToken(outerScope);
        var token = AmbientScopeService.CreateToken(scope);

        await using var outerEntry = outerToken.Enter(AmbientScopeEnterMode.Reusable);
        var outerProvider = AmbientScopeService.CurrentServiceProvider;
        _ = scope.ServiceProvider.GetRequiredService<TrackedScopedDependency>();

        var entry = token.Enter(AmbientScopeEnterMode.Reusable);
        await entry.DisposeAsync();
        await entry.DisposeAsync();

        Assert.False(tracked.IsDisposed);
        Assert.Same(outerProvider, AmbientScopeService.CurrentServiceProvider);

        await token.DisposeAsync();
        await token.DisposeAsync();

        Assert.True(tracked.IsDisposed);
    }

    [Fact]
    public async Task AmbientScope_FlowsAcrossAwait_AndRestoresAfterExit()
    {
        var outerTracked = new TrackedScopedDependency();
        var tracked = new TrackedScopedDependency();
        using var outerScope = CreateTrackedScope(outerTracked);
        using var scope = CreateTrackedScope(tracked);
        var outerToken = AmbientScopeService.CreateToken(outerScope);
        var token = AmbientScopeService.CreateToken(scope);

        using var outerEntry = outerToken.Enter(AmbientScopeEnterMode.Reusable);
        var outerProvider = AmbientScopeService.CurrentServiceProvider;

        using (token.Enter(AmbientScopeEnterMode.Reusable))
        {
            await Task.Yield();
            Assert.Same(tracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
        }

        Assert.Same(outerProvider, AmbientScopeService.CurrentServiceProvider);
        Assert.Same(outerTracked, AmbientScopeService.CurrentServiceProvider.GetRequiredService<TrackedScopedDependency>());
    }

    [Fact]
    public async Task Enter_RealAsyncScope_DisposesAsyncOnlyService_AndRestoresAmbientProvider()
    {
        await using var services = new ServiceCollection()
            .AddScoped<AsyncTrackedDependency>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var outerScope = services.CreateScope();
        using var outerToken = AmbientScopeService.CreateToken(outerScope);
        using var outerEntry = outerToken.Enter(AmbientScopeEnterMode.Reusable);
        var outerProvider = AmbientScopeService.CurrentServiceProvider;

        await using var token = AmbientScopeService.CreateToken(services.CreateAsyncScope());
        var entry = token.Enter();
        var dependency = AmbientScopeService.CurrentServiceProvider.GetRequiredService<AsyncTrackedDependency>();
        Assert.False(dependency.IsDisposed);

        await entry.DisposeAsync();

        Assert.True(dependency.IsDisposed);
        Assert.Same(outerProvider, AmbientScopeService.CurrentServiceProvider);
        await entry.DisposeAsync();
        Assert.Same(outerProvider, AmbientScopeService.CurrentServiceProvider);
    }

    [Fact]
    public async Task DefaultLeaseExit_ClosesToken_AndDisposesScopeExactlyOnce()
    {
        var scope = new CountingScope();
        var token = AmbientScopeService.CreateToken(scope);
        var entry = token.Enter();

        entry.Dispose();
        entry.Dispose();
        await entry.DisposeAsync();
        token.Dispose();
        await token.DisposeAsync();

        Assert.Equal(1, scope.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => token.Enter());
        Assert.Throws<ObjectDisposedException>(() => token.Enter(AmbientScopeEnterMode.Reusable));
    }

    [Theory]
    [InlineData(AmbientScopeEnterMode.DisposeOnExit)]
    [InlineData(AmbientScopeEnterMode.Reusable)]
    public void SameToken_NestedDefaultExit_DefersDisposalUntilFinalLease(AmbientScopeEnterMode outerMode)
    {
        var scope = new CountingScope();
        var token = AmbientScopeService.CreateToken(scope);
        var outer = token.Enter(outerMode);
        var inner = token.Enter();

        inner.Dispose();

        Assert.Equal(0, scope.DisposeCount);
        Assert.Same(scope.ServiceProvider, AmbientScopeService.CurrentServiceProvider);
        Assert.Throws<ObjectDisposedException>(() => token.Enter());
        outer.Dispose();
        token.Dispose();
        Assert.Equal(1, scope.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => AmbientScopeService.CurrentServiceProvider);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenDisposal_WithActiveReusableLease_DefersCleanup(bool asynchronous)
    {
        var scope = new CountingScope();
        var token = AmbientScopeService.CreateToken(scope);
        var entry = token.Enter(AmbientScopeEnterMode.Reusable);

        if (asynchronous)
        {
            await token.DisposeAsync();
        }
        else
        {
            token.Dispose();
        }

        Assert.Equal(0, scope.DisposeCount);
        Assert.Same(scope.ServiceProvider, AmbientScopeService.CurrentServiceProvider);
        Assert.Throws<ObjectDisposedException>(() => token.Enter());
        await entry.DisposeAsync();
        Assert.Equal(1, scope.DisposeCount);
    }

    [Fact]
    public async Task OutOfOrderExit_IsRejectedWithoutChangingAmbientOrLifetime()
    {
        var scope = new CountingScope();
        var token = AmbientScopeService.CreateToken(scope);
        var outer = token.Enter();
        var inner = token.Enter(AmbientScopeEnterMode.Reusable);

        Assert.Throws<InvalidOperationException>(() => outer.Dispose());
        Assert.Throws<InvalidOperationException>(() => outer.DisposeAsync());
        Assert.Same(scope.ServiceProvider, AmbientScopeService.CurrentServiceProvider);
        Assert.Equal(0, scope.DisposeCount);
        // A failed exit must not even request disposal.
        using (token.Enter(AmbientScopeEnterMode.Reusable))
        {
        }

        inner.Dispose();
        await outer.DisposeAsync();
        Assert.Equal(1, scope.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => AmbientScopeService.CurrentServiceProvider);
    }

    [Fact]
    public void InvalidArguments_AreRejectedWithoutChangingAmbientOrToken()
    {
        Assert.Throws<ArgumentNullException>(() => AmbientScopeService.CreateToken((IServiceScope)null!));
        Assert.Throws<ArgumentNullException>(() => AmbientScopeService.CreateToken((IServiceProvider)null!));
        var scope = new CountingScope();
        using var token = AmbientScopeService.CreateToken(scope);
        using var outer = token.Enter(AmbientScopeEnterMode.Reusable);

        Assert.Throws<ArgumentOutOfRangeException>(() => token.Enter((AmbientScopeEnterMode)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => token.Enter((AmbientScopeEnterMode)42));
        Assert.Same(scope.ServiceProvider, AmbientScopeService.CurrentServiceProvider);
        Assert.Equal(0, scope.DisposeCount);
        using var validEntry = token.Enter(AmbientScopeEnterMode.Reusable);
    }

    [Fact]
    public async Task ParallelEntries_KeepScopeAliveUntilEveryLeaseExits()
    {
        var scope = new CountingScope();
        var token = AmbientScopeService.CreateToken(scope);
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var tasks = Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            var entry = token.Enter();
            if (Interlocked.Increment(ref entered) == 12)
            {
                allEntered.SetResult();
            }

            await release.Task;
            Assert.Same(scope.ServiceProvider, AmbientScopeService.CurrentServiceProvider);
            await entry.DisposeAsync();
        })).ToArray();

        await allEntered.Task;
        token.Dispose();
        Assert.Equal(0, scope.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => token.Enter());
        release.SetResult();
        await Task.WhenAll(tasks);
        Assert.Equal(1, scope.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => AmbientScopeService.CurrentServiceProvider);
    }

    [Fact]
    public async Task InheritedFrame_AfterOwningLeaseExits_DoesNotExposeDisposedProvider()
    {
        var scope = new CountingScope();
        var token = AmbientScopeService.CreateToken(scope);
        var entry = token.Enter();
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = Task.Run(async () =>
        {
            await proceed.Task;
            Assert.Throws<InvalidOperationException>(() => AmbientScopeService.CurrentServiceProvider);
        });

        entry.Dispose();
        proceed.SetResult();
        await child;
        Assert.Equal(1, scope.DisposeCount);
    }

    [Fact]
    public async Task ClosedInheritedFrames_AreSkippedWhenRestoringPreviousScope()
    {
        var outerScope = new CountingScope();
        using var outerToken = AmbientScopeService.CreateToken(outerScope);
        using var outer = outerToken.Enter(AmbientScopeEnterMode.Reusable);
        var innerScope = new CountingScope();
        var innerToken = AmbientScopeService.CreateToken(innerScope);
        var inner = innerToken.Enter();

        // Lease transfer is unsupported, but a closed shared frame must not leave
        // the parent's ambient lookup pointing at the disposed provider.
        await Task.Run(() => inner.Dispose());
        Assert.Same(outerScope.ServiceProvider, AmbientScopeService.CurrentServiceProvider);
        inner.Dispose();
        Assert.Equal(1, innerScope.DisposeCount);
        outer.Dispose();
        Assert.Throws<InvalidOperationException>(() => AmbientScopeService.CurrentServiceProvider);
    }

    [Fact]
    public async Task AsyncOnlyDiScope_DeferredDisposal_RestoresContextBeforeCleanupCompletes()
    {
        var dependency = new GatedAsyncDependency();
        await using var services = new ServiceCollection()
            .AddScoped(_ => dependency)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var outerScope = services.CreateScope();
        using var outerToken = AmbientScopeService.CreateToken(outerScope);
        using var outer = outerToken.Enter(AmbientScopeEnterMode.Reusable);
        var outerProvider = AmbientScopeService.CurrentServiceProvider;
        var token = AmbientScopeService.CreateToken(services.CreateAsyncScope());
        var first = token.Enter(AmbientScopeEnterMode.Reusable);
        var lastEntered = token.Enter();
        Assert.Same(dependency, AmbientScopeService.CurrentServiceProvider.GetRequiredService<GatedAsyncDependency>());

        await lastEntered.DisposeAsync();
        Assert.Equal(0, dependency.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => token.Enter());
        var cleanup = first.DisposeAsync();
        try
        {
            Assert.Same(outerProvider, AmbientScopeService.CurrentServiceProvider);
            Assert.False(cleanup.IsCompleted);
            Assert.Equal(1, dependency.DisposeCount);
        }
        finally
        {
            dependency.CompleteDisposal.SetResult();
            await cleanup;
        }

        await first.DisposeAsync();
        await token.DisposeAsync();
        token.Dispose();
        Assert.Equal(1, dependency.DisposeCount);
    }

    [Fact]
    public async Task ConcurrentTokenDisposal_DisposesScopeExactlyOnce()
    {
        var scope = new CountingScope();
        var token = AmbientScopeService.CreateToken(scope);
        var tasks = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            if (index % 2 == 0)
            {
                token.Dispose();
            }
            else
            {
                await token.DisposeAsync();
            }
        }));

        await Task.WhenAll(tasks);
        Assert.Equal(1, scope.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => token.Enter());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalFailure_StillRestoresAmbient_AndCannotDisposeAgain(bool asynchronous)
    {
        var scope = new ThrowingScope();
        var token = AmbientScopeService.CreateToken(scope);
        var entry = token.Enter();
        if (asynchronous)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await entry.DisposeAsync());
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => entry.Dispose());
        }

        Assert.Throws<InvalidOperationException>(() => AmbientScopeService.CurrentServiceProvider);
        Assert.Throws<ObjectDisposedException>(() => token.Enter());
        entry.Dispose();
        await entry.DisposeAsync();
        token.Dispose();
        await token.DisposeAsync();
        Assert.Equal(1, scope.DisposeCount);
    }

    private sealed class ThrowingScope : IServiceScope, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();
        public void Dispose()
        {
            DisposeCount++;
            throw new InvalidOperationException("Disposal failed.");
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            Dispose();
        }
    }

    private sealed class CountingScope : IServiceScope, IAsyncDisposable
    {
        private int _disposeCount;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public IServiceProvider ServiceProvider { get; } = new ServiceCollection().BuildServiceProvider();
        public void Dispose() => Interlocked.Increment(ref _disposeCount);
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GatedAsyncDependency : IAsyncDisposable
    {
        public TaskCompletionSource CompleteDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }
        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            await CompleteDisposal.Task;
        }
    }

    private sealed class AsyncTrackedDependency : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static IServiceScope CreateTrackedScope(TrackedScopedDependency dependency)
    {
        var scopedServices = new ServiceCollection()
            .AddScoped(_ => dependency)
            .BuildServiceProvider();

        return new DelegatingScope(scopedServices);
    }

    private sealed class TrackedScopedDependency : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class DelegatingScope : IServiceScope, IAsyncDisposable
    {
        private readonly IServiceProvider _provider;
        private readonly IAsyncDisposable? _asyncDisposable;
        private readonly IDisposable _disposable;

        public DelegatingScope(IServiceProvider provider)
        {
            _provider = provider;
            _asyncDisposable = provider as IAsyncDisposable;
            _disposable = (IDisposable)provider;
        }

        public IServiceProvider ServiceProvider => _provider;

        public void Dispose()
        {
            _disposable.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return _asyncDisposable?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
