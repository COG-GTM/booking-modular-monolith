using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Booking.Extensions.Infrastructure;

public sealed class GrpcDeadlineInterceptor(TimeSpan deadline) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation
    )
    {
        return continuation(request, WithDefaultDeadline(context));
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation
    )
    {
        return continuation(request, WithDefaultDeadline(context));
    }

    private ClientInterceptorContext<TRequest, TResponse> WithDefaultDeadline<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context
    )
        where TRequest : class
        where TResponse : class
    {
        if (context.Options.Deadline.HasValue)
        {
            return context;
        }

        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithDeadline(DateTime.UtcNow.Add(deadline))
        );
    }
}
