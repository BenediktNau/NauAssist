using System;
using System.Threading;

namespace NauAssist.Common.Logging;

public sealed class CorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? CurrentId => Current.Value;

    public IDisposable Begin(string id)
    {
        var previous = Current.Value;
        Current.Value = id;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public Scope(string? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Current.Value = _previous;
            _disposed = true;
        }
    }
}
