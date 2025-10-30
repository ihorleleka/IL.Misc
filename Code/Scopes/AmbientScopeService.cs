using Microsoft.Extensions.DependencyInjection;

namespace IL.Misc.Scopes;

public interface IAmbientScopeEntry : IDisposable, IAsyncDisposable
{
}

public static class AmbientScopeService
{
    private static readonly AsyncLocal<IServiceScope?> CurrentScope = new();

    public static IServiceProvider CurrentServiceProvider =>
        CurrentScope.Value?.ServiceProvider
        ?? throw new InvalidOperationException("No active service scope.");

    /// <summary>
    /// Temporarily pushes a new service scope into the ambient context.
    /// </summary>
    private static ScopeReset Push(IServiceScope scope)
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = scope;
        return new ScopeReset(previous, scope);
    }

    /// <summary>
    /// Creates a reusable token that can re-enter this scope later.
    /// </summary>
    public static AmbientScopeToken CreateToken(IServiceScope scope) => new(scope);

    private sealed class ScopeReset : IAmbientScopeEntry
    {
        private bool _disposed;
        private readonly IServiceScope? _previous;
        private readonly IServiceScope _current;

        public ScopeReset(IServiceScope? previous, IServiceScope current)
        {
            _previous = previous;
            _current = current;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentScope.Value = _previous;
            _current.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentScope.Value = _previous;

            if (_current is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                _current.Dispose();
            }
        }

        /// <summary>
        /// Resets current ambient scope without disposing actual scoped services object.
        /// </summary>
        public void CurrentScopeResetOnly()
        {
            CurrentScope.Value = _previous;
        }
    }

    /// <summary>
    /// A reusable token that allows re-entering the same service scope.
    /// </summary>
    public sealed class AmbientScopeToken : IAmbientScopeEntry
    {
        private readonly IServiceScope _scope;
        private bool _disposed;

        internal AmbientScopeToken(IServiceScope scope)
        {
            _scope = scope;
        }

        /// <summary>
        /// Enters the ambient scope.
        /// By default, the scope will be disposed when the returned object is disposed.
        /// Pass <paramref name="reusable"/> = true to keep the scope alive for reuse.
        /// </summary>
        public IAmbientScopeEntry Enter(bool reusable = false)
        {
            var reset = Push(_scope);

            return reusable
                ? new ReusableEntry(reset) // only reset AsyncLocal, do NOT dispose scope
                : new AutoDisposeWrapper(this, reset); // reset + dispose scope at end
        }

        public IServiceScope Scope => _scope;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _scope.Dispose();
        }

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

        /// <summary>
        /// Wraps a ScopeReset and disposes both the token and scope at the end.
        /// </summary>
        private sealed class AutoDisposeWrapper : IAmbientScopeEntry
        {
            private bool _disposed;
            private readonly AmbientScopeToken _token;
            private readonly IDisposable _reset;

            public AutoDisposeWrapper(AmbientScopeToken token, IDisposable reset)
            {
                _token = token;
                _reset = reset;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                _reset.Dispose();
                _token.Dispose();
            }

            public async ValueTask DisposeAsync()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (_reset is IAsyncDisposable asyncReset)
                {
                    await asyncReset.DisposeAsync();
                }
                else
                {
                    _reset.Dispose();
                }

                await _token.DisposeAsync();
            }
        }

        /// <summary>
        /// Wraps a ScopeReset for reusable scopes.
        /// Only resets AsyncLocal on dispose; does NOT dispose the underlying scope.
        /// </summary>
        private sealed class ReusableEntry : IAmbientScopeEntry
        {
            private bool _disposed;
            private readonly ScopeReset _reset;

            public ReusableEntry(ScopeReset reset)
            {
                _reset = reset;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                // only reset AsyncLocal
                _reset.CurrentScopeResetOnly();
            }

            public ValueTask DisposeAsync()
            {
                if (_disposed)
                {
                    return ValueTask.CompletedTask;
                }

                _disposed = true;
                _reset.CurrentScopeResetOnly();
                return ValueTask.CompletedTask;
            }
        }
    }
}