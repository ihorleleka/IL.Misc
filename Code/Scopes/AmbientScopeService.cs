using Microsoft.Extensions.DependencyInjection;

namespace IL.Misc.Scopes;

/// <summary>
/// Represents an active ambient scope lease.
/// </summary>
/// <remarks>
/// Dispose leases in reverse entry order within the execution flow that entered them.
/// Awaiting within that flow is supported; transferring a lease to another flow is not.
/// ExecutionContext inheritance does not acquire another lease: parallel users must enter
/// the token independently and keep their leases alive for their entire use of the provider.
/// </remarks>
public interface IAmbientScopeEntry : IDisposable, IAsyncDisposable
{
}

/// <summary>
/// Represents a reusable token that can enter an ambient scope.
/// </summary>
public interface IAmbientScopeToken : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Enters the ambient scope and returns a lease that restores the previous scope on exit.
    /// </summary>
    /// <param name="mode">
    /// Controls whether the underlying scope is disposed when the returned lease exits.
    /// </param>
    /// <returns>
    /// An active ambient scope lease that must be disposed to restore the previous ambient scope.
    /// </returns>
    IAmbientScopeEntry Enter(AmbientScopeEnterMode mode = AmbientScopeEnterMode.DisposeOnExit);
}

/// <summary>
/// Controls whether the entered scope is disposed when the ambient lease exits.
/// </summary>
public enum AmbientScopeEnterMode
{
    /// <summary>
    /// Request disposal when the ambient lease exits; defer cleanup until the final active lease exits.
    /// </summary>
    DisposeOnExit,

    /// <summary>
    /// Restore the previous ambient scope without disposing the underlying scope.
    /// </summary>
    Reusable
}

/// <summary>
/// Provides ambient access to the current service scope.
/// </summary>
public static class AmbientScopeService
{
    private static readonly AsyncLocal<AmbientScopeLease?> CurrentEntry = new();

    // ExecutionContext copies frame references, not ownership of their leases. Never
    // expose a frame another flow has already closed.
    private static AmbientScopeLease? ActiveEntry
    {
        get
        {
            var entry = CurrentEntry.Value;
            while (entry != null && entry.IsDisposed)
            {
                entry = entry.Previous;
            }

            return entry;
        }
    }

    /// <summary>
    /// Gets the currently active ambient service provider.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no ambient scope is currently active.
    /// </exception>
    public static IServiceProvider CurrentServiceProvider =>
        ActiveEntry?.Scope.ServiceProvider
        ?? throw new InvalidOperationException("No active service scope.");

    /// <summary>
    /// Creates a reusable token that can re-enter the specified scope later.
    /// </summary>
    /// <param name="scope">The scope to expose through the ambient context.</param>
    /// <returns>A token that can enter the provided scope multiple times.</returns>
    public static AmbientScopeToken CreateToken(IServiceScope scope) => new(scope);

    /// <summary>
    /// Creates a reusable token that can re-enter an existing scoped provider later.
    /// </summary>
    /// <param name="scopedServiceProvider">
    /// An existing scoped service provider whose lifetime is owned elsewhere.
    /// </param>
    /// <returns>A token that can enter the provided scoped service provider multiple times.</returns>
    /// <remarks>
    /// Prefer <see cref="CreateToken(IServiceScope)"/> when possible.
    /// This overload does not own the provider lifetime and relies on the caller to pass an already-scoped provider.
    /// Detection of root versus scoped providers is container-dependent and cannot be guaranteed for every DI implementation.
    /// </remarks>
    public static AmbientScopeToken CreateToken(IServiceProvider scopedServiceProvider) => new(scopedServiceProvider);

    /// <summary>
    /// A reusable token that allows re-entering the same service scope.
    /// </summary>
    /// <remarks>
    /// Token disposal, or exit of a DisposeOnExit lease, immediately prevents new entries.
    /// The owned scope is disposed exactly once after all active entries exit. A disposal
    /// request with active entries returns without waiting; the final lease's disposal method
    /// selects synchronous or asynchronous cleanup. Use DisposeAsync on that lease for scopes
    /// containing async-only services. Repeated disposal calls are no-ops, not completion joins.
    /// The Scope property exposes the original scope; disposing it directly bypasses this lifetime management.
    /// </remarks>
    public sealed class AmbientScopeToken : IAmbientScopeToken
    {
        private readonly IServiceScope _scope;
        private readonly object _gate = new();
        private int _activeEntries;
        private bool _disposalRequested;
        private bool _disposalStarted;

        internal AmbientScopeToken(IServiceScope scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            _scope = scope;
        }

        internal AmbientScopeToken(IServiceProvider scopedServiceProvider)
        {
            _scope = new ExistingScopedServiceProviderScopeWrapper(scopedServiceProvider);
        }

        /// <summary>
        /// Enters the ambient scope.
        /// </summary>
        /// <param name="mode">
        /// Controls whether the underlying scope is disposed when the returned lease exits.
        /// </param>
        /// <returns>
        /// An active lease that restores the previous ambient scope when disposed.
        /// </returns>
        /// <exception cref="ObjectDisposedException">
        /// Thrown when the token has already been disposed.
        /// </exception>
        public IAmbientScopeEntry Enter(AmbientScopeEnterMode mode = AmbientScopeEnterMode.DisposeOnExit)
        {
            if (mode != AmbientScopeEnterMode.DisposeOnExit && mode != AmbientScopeEnterMode.Reusable)
            {
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ambient scope entry mode.");
            }

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposalRequested, this);
                var entry = new AmbientScopeLease(ActiveEntry, this, mode == AmbientScopeEnterMode.DisposeOnExit);
                _activeEntries++;
                CurrentEntry.Value = entry;
                return entry;
            }
        }

        /// <summary>
        /// Gets the scope wrapped by this token.
        /// </summary>
        public IServiceScope Scope => _scope;

        /// <summary>
        /// Disposes the underlying scope owned by this token.
        /// </summary>
        /// <remarks>
        /// If this token was created from <see cref="CreateToken(IServiceProvider)"/>, dispose is a no-op for the wrapped provider lifetime.
        /// </remarks>
        public void Dispose()
        {
            if (TryBeginDisposal(exitEntry: false, requestDisposal: true))
            {
                _scope.Dispose();
            }
        }

        /// <summary>
        /// Asynchronously disposes the underlying scope owned by this token.
        /// </summary>
        /// <remarks>
        /// If this token was created from <see cref="CreateToken(IServiceProvider)"/>, asynchronous disposal is a no-op for the wrapped provider lifetime.
        /// </remarks>
        public ValueTask DisposeAsync() =>
            TryBeginDisposal(exitEntry: false, requestDisposal: true)
                ? DisposeScopeAsync()
                : ValueTask.CompletedTask;

        private bool TryBeginDisposal(bool exitEntry, bool requestDisposal)
        {
            lock (_gate)
            {
                if (exitEntry)
                {
                    _activeEntries--;
                }

                _disposalRequested |= requestDisposal;
                if (!_disposalRequested || _activeEntries != 0 || _disposalStarted)
                {
                    return false;
                }

                _disposalStarted = true;
                return true;
            }
        }

        internal void Exit(bool requestDisposal)
        {
            if (TryBeginDisposal(exitEntry: true, requestDisposal))
            {
                _scope.Dispose();
            }
        }

        internal ValueTask ExitAsync(bool requestDisposal) =>
            TryBeginDisposal(exitEntry: true, requestDisposal)
                ? DisposeScopeAsync()
                : ValueTask.CompletedTask;

        private ValueTask DisposeScopeAsync()
        {
            if (_scope is IAsyncDisposable asyncDisposable)
            {
                return asyncDisposable.DisposeAsync();
            }

            _scope.Dispose();
            return ValueTask.CompletedTask;
        }

        private sealed class ExistingScopedServiceProviderScopeWrapper : IServiceScope, IAsyncDisposable
        {
            public ExistingScopedServiceProviderScopeWrapper(IServiceProvider serviceProvider)
            {
                ArgumentNullException.ThrowIfNull(serviceProvider);

                var factory = serviceProvider.GetService<IServiceScopeFactory>();
                if (factory == null)
                {
                    throw new InvalidOperationException("The provided IServiceProvider does not appear to be a scoped provider.");
                }

                ServiceProvider = serviceProvider;
            }

            public IServiceProvider ServiceProvider { get; }

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class AmbientScopeLease : IAmbientScopeEntry
    {
        private readonly AmbientScopeToken _token;
        private readonly bool _disposeScopeOnExit;
        private readonly object _gate = new();
        private volatile bool _disposed;

        public AmbientScopeLease(AmbientScopeLease? previous, AmbientScopeToken token, bool disposeScopeOnExit)
        {
            Previous = previous;
            _token = token;
            _disposeScopeOnExit = disposeScopeOnExit;
        }

        public AmbientScopeLease? Previous { get; }
        public IServiceScope Scope => _token.Scope;
        public bool IsDisposed => _disposed;

        private bool RestoreContext()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                if (!ReferenceEquals(ActiveEntry, this))
                {
                    throw new InvalidOperationException("Ambient scope leases must exit in reverse entry order in their originating execution flow.");
                }

                _disposed = true;
                CurrentEntry.Value = ActiveEntry;
                return true;
            }
        }

        public void Dispose()
        {
            if (RestoreContext())
            {
                _token.Exit(_disposeScopeOnExit);
            }
        }

        // Do not make this method async: AsyncLocal restoration must be visible
        // to the caller before asynchronous scope disposal starts.
        public ValueTask DisposeAsync() => RestoreContext()
            ? _token.ExitAsync(_disposeScopeOnExit)
            : ValueTask.CompletedTask;
    }
}
