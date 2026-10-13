using System.Text;
using Booking.GrpcClient;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Xunit;

namespace Unit.Test.GrpcClient;

public class GrpcDeadlineInterceptorTests
{
    private static readonly Marshaller<string> Utf8 = Marshallers.Create(
        Encoding.UTF8.GetBytes,
        Encoding.UTF8.GetString
    );

    private static readonly Method<string, string> Unary = new(MethodType.Unary, "test.Svc", "Unary", Utf8, Utf8);
    private static readonly Method<string, string> ServerStreaming = new(
        MethodType.ServerStreaming,
        "test.Svc",
        "ServerStreaming",
        Utf8,
        Utf8
    );
    private static readonly Method<string, string> ClientStreaming = new(
        MethodType.ClientStreaming,
        "test.Svc",
        "ClientStreaming",
        Utf8,
        Utf8
    );
    private static readonly Method<string, string> Duplex = new(
        MethodType.DuplexStreaming,
        "test.Svc",
        "Duplex",
        Utf8,
        Utf8
    );

    [Fact]
    public async Task async_unary_call_without_deadline_gets_the_configured_default()
    {
        var invoker = new RecordingCallInvoker();
        var intercepted = invoker.Intercept(new GrpcDeadlineInterceptor(TimeSpan.FromSeconds(10)));

        var before = DateTime.UtcNow;
        await intercepted.AsyncUnaryCall(Unary, null, new CallOptions(), "request");

        invoker.LastOptions.Should().NotBeNull();
        invoker.LastOptions!.Value.Deadline.Should().NotBeNull();
        invoker.LastOptions.Value.Deadline!.Value.Should().BeCloseTo(before.AddSeconds(10), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void blocking_unary_call_without_deadline_gets_the_configured_default()
    {
        var invoker = new RecordingCallInvoker();
        var intercepted = invoker.Intercept(new GrpcDeadlineInterceptor(TimeSpan.FromSeconds(3)));

        var before = DateTime.UtcNow;
        intercepted.BlockingUnaryCall(Unary, null, new CallOptions(), "request");

        invoker.LastOptions!.Value.Deadline.Should().BeCloseTo(before.AddSeconds(3), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void explicit_call_deadline_is_preserved()
    {
        var invoker = new RecordingCallInvoker();
        var intercepted = invoker.Intercept(new GrpcDeadlineInterceptor(TimeSpan.FromSeconds(1)));
        var explicitDeadline = DateTime.UtcNow.AddMinutes(5);

        intercepted.AsyncUnaryCall(Unary, null, new CallOptions(deadline: explicitDeadline), "request");

        invoker.LastOptions!.Value.Deadline.Should().Be(explicitDeadline);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void non_positive_default_deadline_leaves_the_call_unbounded(int seconds)
    {
        var invoker = new RecordingCallInvoker();
        var intercepted = invoker.Intercept(new GrpcDeadlineInterceptor(TimeSpan.FromSeconds(seconds)));

        intercepted.AsyncUnaryCall(Unary, null, new CallOptions(), "request");

        invoker.LastOptions!.Value.Deadline.Should().BeNull();
    }

    [Fact]
    public void other_call_options_survive_the_deadline_rewrite()
    {
        var invoker = new RecordingCallInvoker();
        var intercepted = invoker.Intercept(new GrpcDeadlineInterceptor(TimeSpan.FromSeconds(10)));
        var headers = new Metadata { { "x-correlation-id", "abc" } };
        using var cts = new CancellationTokenSource();

        intercepted.AsyncUnaryCall(
            Unary,
            "flight-host",
            new CallOptions(headers: headers, cancellationToken: cts.Token),
            "request"
        );

        invoker.LastHost.Should().Be("flight-host");
        invoker.LastOptions!.Value.Headers.Should().BeSameAs(headers);
        invoker.LastOptions.Value.CancellationToken.Should().Be(cts.Token);
        invoker.LastOptions.Value.Deadline.Should().NotBeNull();
    }

    [Fact]
    public void streaming_calls_get_the_configured_default_deadline()
    {
        var invoker = new RecordingCallInvoker();
        var intercepted = invoker.Intercept(new GrpcDeadlineInterceptor(TimeSpan.FromSeconds(10)));

        intercepted.AsyncServerStreamingCall(ServerStreaming, null, new CallOptions(), "request");
        invoker.LastOptions!.Value.Deadline.Should().NotBeNull("server streaming");

        intercepted.AsyncClientStreamingCall(ClientStreaming, null, new CallOptions());
        invoker.LastOptions!.Value.Deadline.Should().NotBeNull("client streaming");

        intercepted.AsyncDuplexStreamingCall(Duplex, null, new CallOptions());
        invoker.LastOptions!.Value.Deadline.Should().NotBeNull("duplex streaming");
    }

    private sealed class RecordingCallInvoker : CallInvoker
    {
        public CallOptions? LastOptions { get; private set; }

        public string? LastHost { get; private set; }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request
        )
        {
            Record(host, options);
            return null!;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request
        )
        {
            Record(host, options);
            return new AsyncUnaryCall<TResponse>(
                Task.FromResult<TResponse>(null!),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }
            );
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request
        )
        {
            Record(host, options);
            return new AsyncServerStreamingCall<TResponse>(
                new EmptyStreamReader<TResponse>(),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }
            );
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options
        )
        {
            Record(host, options);
            return new AsyncClientStreamingCall<TRequest, TResponse>(
                new NoopStreamWriter<TRequest>(),
                Task.FromResult<TResponse>(null!),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }
            );
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options
        )
        {
            Record(host, options);
            return new AsyncDuplexStreamingCall<TRequest, TResponse>(
                new NoopStreamWriter<TRequest>(),
                new EmptyStreamReader<TResponse>(),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { }
            );
        }

        private void Record(string? host, CallOptions options)
        {
            LastHost = host;
            LastOptions = options;
        }
    }

    private sealed class EmptyStreamReader<T> : IAsyncStreamReader<T>
    {
        public T Current => default!;

        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NoopStreamWriter<T> : IClientStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message) => Task.CompletedTask;

        public Task CompleteAsync() => Task.CompletedTask;
    }
}
