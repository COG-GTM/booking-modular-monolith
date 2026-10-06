using BuildingBlocks.Grpc;
using Contracts.Grpc.Flight.V1;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Core.Testing;
using Xunit;

namespace Unit.Test.Grpc;

public class GrpcClientDeadlineInterceptorTests
{
    [Fact]
    public async Task should_apply_default_deadline_when_call_has_none()
    {
        var invoker = new CapturingCallInvoker();
        var client = CreateClient(invoker, TimeSpan.FromSeconds(5));
        var before = DateTime.UtcNow;

        await client.GetByIdAsync(new GetByIdRequest { Id = "1" });

        invoker.Options.Deadline.Should().NotBeNull();
        invoker.Options.Deadline!.Value.Should().BeOnOrAfter(before.AddSeconds(5)).And.BeBefore(before.AddSeconds(6));
    }

    [Fact]
    public async Task should_keep_explicit_deadline()
    {
        var invoker = new CapturingCallInvoker();
        var client = CreateClient(invoker, TimeSpan.FromSeconds(5));
        var explicitDeadline = DateTime.UtcNow.AddMinutes(1);

        await client.GetByIdAsync(new GetByIdRequest { Id = "1" }, deadline: explicitDeadline);

        invoker.Options.Deadline.Should().Be(explicitDeadline);
    }

    [Fact]
    public async Task should_not_apply_deadline_when_disabled()
    {
        var invoker = new CapturingCallInvoker();
        var client = CreateClient(invoker, TimeSpan.Zero);

        await client.GetByIdAsync(new GetByIdRequest { Id = "1" });

        invoker.Options.Deadline.Should().BeNull();
    }

    private static FlightGrpcService.FlightGrpcServiceClient CreateClient(CallInvoker invoker, TimeSpan deadline)
    {
        return new FlightGrpcService.FlightGrpcServiceClient(
            invoker.Intercept(new GrpcClientDeadlineInterceptor(deadline))
        );
    }

    private sealed class CapturingCallInvoker : CallInvoker
    {
        public CallOptions Options { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request
        )
        {
            Options = options;
            return TestCalls.AsyncUnaryCall(
                Task.FromResult(Activator.CreateInstance<TResponse>()),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }
            );
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request
        ) => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request
        ) => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options
        ) => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options
        ) => throw new NotSupportedException();
    }
}
