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
