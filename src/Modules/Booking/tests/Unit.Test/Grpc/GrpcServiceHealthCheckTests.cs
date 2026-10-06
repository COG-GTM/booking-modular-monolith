using BuildingBlocks.Grpc;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Unit.Test.Grpc;

public class GrpcServiceHealthCheckTests
{
    private const string ClientName = "flight-health";

    [Fact]
    public async Task should_be_healthy_when_dependency_is_serving()
    {
        var factory = new FakeGrpcClientFactory(HealthCheckResponse.Types.ServingStatus.Serving);
        var healthCheck = new GrpcServiceHealthCheck(factory, ClientName);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck, HealthStatus.Unhealthy));

        result.Status.Should().Be(HealthStatus.Healthy);
        factory.RequestedClientName.Should().Be(ClientName);
    }

    [Theory]
    [InlineData(HealthCheckResponse.Types.ServingStatus.NotServing)]
    [InlineData(HealthCheckResponse.Types.ServingStatus.Unknown)]
    [InlineData(HealthCheckResponse.Types.ServingStatus.ServiceUnknown)]
    public async Task should_report_registration_failure_status_when_dependency_is_not_serving(
        HealthCheckResponse.Types.ServingStatus status
    )
    {
        var factory = new FakeGrpcClientFactory(status);
        var healthCheck = new GrpcServiceHealthCheck(factory, ClientName);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck, HealthStatus.Degraded));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain(status.ToString());
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task should_report_registration_failure_status_when_dependency_is_unreachable()
    {
        var rpcStatus = new Status(StatusCode.Unavailable, "connection refused");
        var factory = new FakeGrpcClientFactory(new RpcException(rpcStatus));
        var healthCheck = new GrpcServiceHealthCheck(factory, ClientName);

        var result = await healthCheck.CheckHealthAsync(CreateContext(healthCheck, HealthStatus.Unhealthy));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be(rpcStatus.ToString());
        result.Exception.Should().BeOfType<RpcException>().Which.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    private static HealthCheckContext CreateContext(IHealthCheck healthCheck, HealthStatus failureStatus)
    {
        return new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("flight", healthCheck, failureStatus, tags: null),
        };
    }

    private sealed class FakeGrpcClientFactory : GrpcClientFactory
    {
        private readonly CallInvoker _invoker;

        public FakeGrpcClientFactory(HealthCheckResponse.Types.ServingStatus status)
        {
            _invoker = new HealthCallInvoker(() => new HealthCheckResponse { Status = status });
        }

        public FakeGrpcClientFactory(RpcException exception)
        {
            _invoker = new HealthCallInvoker(() => throw exception);
        }

        public string? RequestedClientName { get; private set; }

        public override TClient CreateClient<TClient>(string name)
            where TClient : class
        {
            RequestedClientName = name;
            return (TClient)(object)new Health.HealthClient(_invoker);
        }
    }

    private sealed class HealthCallInvoker : CallInvoker
    {
        private readonly Func<HealthCheckResponse> _respond;

        public HealthCallInvoker(Func<HealthCheckResponse> respond)
        {
            _respond = respond;
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request
        )
        {
            method.FullName.Should().Be("/grpc.health.v1.Health/Check");

            Task<TResponse> response;
            try
            {
                response = Task.FromResult((TResponse)(object)_respond());
            }
            catch (RpcException exception)
            {
                response = Task.FromException<TResponse>(exception);
            }

            return TestCalls.AsyncUnaryCall(
                response,
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
