using Mediator;

namespace NauAssist.Backend.Tests.Helpers;

/// <summary>
/// Minimaler Mediator-Stub für Tool-Tests.
/// </summary>
public sealed class FakeMediator : IMediator
{
    private readonly Dictionary<Type, object> _responses = new();
    private readonly List<object> _sent = new();

    public IReadOnlyList<object> SentRequests => _sent;

    public void SetupResponse<TRequest, TResponse>(TResponse response)
        where TRequest : IRequest<TResponse>
    {
        _responses[typeof(TRequest)] = response!;
    }

    public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        _sent.Add(request);
        if (_responses.TryGetValue(request.GetType(), out var resp))
        {
            return ValueTask.FromResult((TResponse)resp);
        }
        throw new InvalidOperationException($"FakeMediator hat keine Response für {request.GetType().Name}.");
    }

    public ValueTask<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => ValueTask.CompletedTask;

    public ValueTask Publish(object notification, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    // Zusätzliche Mediator-3-Spezialisierungen — wir nutzen nur IRequest in den Tests,
    // diese Overloads sind also nicht relevant.
    public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
