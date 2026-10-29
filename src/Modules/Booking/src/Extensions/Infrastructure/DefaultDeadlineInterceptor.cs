using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Booking.Extensions.Infrastructure;

/// <summary>Applies a default deadline to calls that don't set one, so a hung dependency fails fast with DeadlineExceeded.</summary>
internal sealed class DefaultDeadlineInterceptor : Interceptor
{
    private readonly TimeSpan _deadline;
    private readonly TimeProvider _timeProvider;

    public DefaultDeadlineInterceptor(TimeSpan deadline, TimeProvider? timeProvider = null)
    {
        _deadline = deadline;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, WithDeadline(context));
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, WithDeadline(context));
    }

    private ClientInterceptorContext<TRequest, TResponse> WithDeadline<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (context.Options.Deadline is not null)
            return context;

        var options = context.Options.WithDeadline(_timeProvider.GetUtcNow().UtcDateTime.Add(_deadline));
        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
    }
}
