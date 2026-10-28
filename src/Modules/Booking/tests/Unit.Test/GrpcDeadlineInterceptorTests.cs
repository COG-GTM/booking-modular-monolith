using Booking.Extensions.Infrastructure;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Xunit;

namespace Booking.Unit.Test;

public class GrpcDeadlineInterceptorTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(150);

    [Fact]
    public void async_unary_call_adds_default_deadline_and_preserves_call_context()
    {
        var method = CreateMethod();
        var headers = new Metadata { { "x-test", "value" } };
        using var cancellation = new CancellationTokenSource();
        var originalContext = new ClientInterceptorContext<string, string>(
            method,
            "grpc.example",
            new CallOptions(headers: headers, cancellationToken: cancellation.Token)
        );
        var interceptor = new GrpcDeadlineInterceptor(Deadline);
        var request = new string("request".ToCharArray());
        var expectedCall = CreateAsyncCall("response");
        string? continuedRequest = null;
        ClientInterceptorContext<string, string> continuedContext = default;
        var before = DateTime.UtcNow;

        var actualCall = interceptor.AsyncUnaryCall(
            request,
            originalContext,
            (continued, context) =>
            {
                continuedRequest = continued;
                continuedContext = context;
                return expectedCall;
            }
        );
        var after = DateTime.UtcNow;

        actualCall.Should().BeSameAs(expectedCall);
        continuedRequest.Should().BeSameAs(request);
        continuedContext.Method.Should().BeSameAs(method);
        continuedContext.Host.Should().Be("grpc.example");
        continuedContext.Options.Headers.Should().BeSameAs(headers);
        continuedContext.Options.CancellationToken.Should().Be(cancellation.Token);
        continuedContext.Options.Deadline.Should().BeAfter(before.Add(Deadline - Tolerance));
        continuedContext.Options.Deadline.Should().BeBefore(after.Add(Deadline + Tolerance));
    }

    [Fact]
    public void blocking_unary_call_adds_default_deadline_and_preserves_call_context()
    {
        var method = CreateMethod();
        var headers = new Metadata { { "x-test", "value" } };
        using var cancellation = new CancellationTokenSource();
        var originalContext = new ClientInterceptorContext<string, string>(
            method,
            "grpc.example",
            new CallOptions(headers: headers, cancellationToken: cancellation.Token)
        );
        var interceptor = new GrpcDeadlineInterceptor(Deadline);
        var request = new string("request".ToCharArray());
        var expectedResponse = new string("response".ToCharArray());
        string? continuedRequest = null;
        ClientInterceptorContext<string, string> continuedContext = default;
        var before = DateTime.UtcNow;

        var actualResponse = interceptor.BlockingUnaryCall(
            request,
            originalContext,
            (continued, context) =>
            {
                continuedRequest = continued;
                continuedContext = context;
                return expectedResponse;
            }
        );
        var after = DateTime.UtcNow;

        actualResponse.Should().BeSameAs(expectedResponse);
        continuedRequest.Should().BeSameAs(request);
        continuedContext.Method.Should().BeSameAs(method);
        continuedContext.Host.Should().Be("grpc.example");
        continuedContext.Options.Headers.Should().BeSameAs(headers);
        continuedContext.Options.CancellationToken.Should().Be(cancellation.Token);
        continuedContext.Options.Deadline.Should().BeAfter(before.Add(Deadline - Tolerance));
        continuedContext.Options.Deadline.Should().BeBefore(after.Add(Deadline + Tolerance));
    }

    [Fact]
    public void async_unary_call_preserves_explicit_deadline_and_return_value()
    {
        var method = CreateMethod();
        var explicitDeadline = DateTime.UtcNow.AddMinutes(2);
        var originalContext = CreateContext(method, new CallOptions(deadline: explicitDeadline));
        var expectedCall = CreateAsyncCall("response");
        var request = new string("request".ToCharArray());
        string? continuedRequest = null;
        ClientInterceptorContext<string, string> continuedContext = default;
        var interceptor = new GrpcDeadlineInterceptor(Deadline);

        var actualCall = interceptor.AsyncUnaryCall(
            request,
            originalContext,
            (continued, context) =>
            {
                continuedRequest = continued;
                continuedContext = context;
                return expectedCall;
            }
        );

        actualCall.Should().BeSameAs(expectedCall);
        continuedRequest.Should().BeSameAs(request);
        continuedContext.Options.Deadline.Should().Be(explicitDeadline);
    }

    [Fact]
    public void blocking_unary_call_preserves_explicit_deadline_and_return_value()
    {
        var method = CreateMethod();
        var explicitDeadline = DateTime.UtcNow.AddMinutes(2);
        var originalContext = CreateContext(method, new CallOptions(deadline: explicitDeadline));
        var expectedResponse = new string("response".ToCharArray());
        var request = new string("request".ToCharArray());
        string? continuedRequest = null;
        ClientInterceptorContext<string, string> continuedContext = default;
        var interceptor = new GrpcDeadlineInterceptor(Deadline);

        var actualResponse = interceptor.BlockingUnaryCall(
            request,
            originalContext,
            (continued, context) =>
            {
                continuedRequest = continued;
                continuedContext = context;
                return expectedResponse;
            }
        );

        actualResponse.Should().BeSameAs(expectedResponse);
        continuedRequest.Should().BeSameAs(request);
        continuedContext.Options.Deadline.Should().Be(explicitDeadline);
    }

    private static Method<string, string> CreateMethod() =>
        new(MethodType.Unary, "svc", "m", Marshallers.StringMarshaller, Marshallers.StringMarshaller);

    private static ClientInterceptorContext<string, string> CreateContext(
        Method<string, string> method,
        CallOptions options
    ) => new(method, "grpc.example", options);

    private static AsyncUnaryCall<string> CreateAsyncCall(string response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { }
        );
}
