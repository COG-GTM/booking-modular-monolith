using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Booking.GrpcClient;

public class GrpcDeadlineInterceptor : Interceptor
{
    private readonly TimeSpan _deadline;

    public GrpcDeadlineInterceptor(TimeSpan deadline)
    {
        _deadline = deadline;
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithDefaultDeadline(context));

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithDefaultDeadline(context));

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithDefaultDeadline(context));

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(WithDefaultDeadline(context));

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(WithDefaultDeadline(context));

    private ClientInterceptorContext<TRequest, TResponse> WithDefaultDeadline<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context
    )
        where TRequest : class
        where TResponse : class
    {
        if (context.Options.Deadline.HasValue || _deadline <= TimeSpan.Zero)
        {
            return context;
        }

        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithDeadline(DateTime.UtcNow.Add(_deadline))
        );
    }
}
