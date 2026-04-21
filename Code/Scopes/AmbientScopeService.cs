using Microsoft.Extensions.DependencyInjection;

namespace IL.Misc.Scopes;

/// <summary>
/// Represents an active ambient scope lease.
/// </summary>
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
    /// Dispose the underlying scope when the ambient lease exits.
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
    private static readonly AsyncLocal<IServiceScope?> CurrentScope = new();

    /// <summary>
    /// Gets the currently active ambient service provider.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no ambient scope is currently active.
    /// </exception>
    public static IServiceProvider CurrentServiceProvider =>
        CurrentScope.Value?.ServiceProvider
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
    public sealed class AmbientScopeToken : IAmbientScopeToken
    {
        private readonly IServiceScope _scope;
        private bool _disposed;

        internal AmbientScopeToken(IServiceScope scope)
        {
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
            ObjectDisposedException.ThrowIf(_disposed, this);

            return new AmbientScopeLease(CurrentScope.Value, _scope, disposeScopeOnExit: mode == AmbientScopeEnterMode.DisposeOnExit);
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
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _scope.Dispose();
        }

        /// <summary>
        /// Asynchronously disposes the underlying scope owned by this token.
        /// </summary>
        /// <remarks>
        /// If this token was created from <see cref="CreateToken(IServiceProvider)"/>, asynchronous disposal is a no-op for the wrapped provider lifetime.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_scope is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                _scope.Dispose();
            }
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
        private readonly IServiceScope? _previous;
        private readonly IServiceScope _current;
        private readonly bool _disposeScopeOnExit;
        private bool _disposed;

        public AmbientScopeLease(IServiceScope? previous, IServiceScope current, bool disposeScopeOnExit)
        {
            _previous = previous;
            _current = current;
            _disposeScopeOnExit = disposeScopeOnExit;
            CurrentScope.Value = current;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentScope.Value = _previous;

            if (_disposeScopeOnExit)
            {
                _current.Dispose();
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            CurrentScope.Value = _previous;

            if (!_disposeScopeOnExit)
            {
                return ValueTask.CompletedTask;
            }

            if (_current is IAsyncDisposable asyncDisposable)
            {
                return asyncDisposable.DisposeAsync();
            }

            _current.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
