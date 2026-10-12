using BuildingBlocks.Exception;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Unit.Test.Interceptors;

public class GrpcExceptionInterceptorTests
{
    private const string Method = "/flight.FlightGrpcService/ReserveSeat";

    private readonly ILogger<GrpcExceptionInterceptor> _logger = Substitute.For<ILogger<GrpcExceptionInterceptor>>();
    private readonly GrpcExceptionInterceptor _interceptor;

    public GrpcExceptionInterceptorTests()
    {
        _interceptor = new GrpcExceptionInterceptor(_logger);
    }

    private static ServerCallContext CreateContext()
    {
        return TestServerCallContext.Create(
            Method,
            "localhost",
            DateTime.UtcNow.AddMinutes(1),
            new Metadata(),
            CancellationToken.None,
            "ipv4:127.0.0.1:5000",
            null,
            null,
            _ => Task.CompletedTask,
            () => null,
            _ => { }
        );
    }

    private Task<string> Act(UnaryServerMethod<string, string> continuation)
    {
        return _interceptor.UnaryServerHandler("request", CreateContext(), continuation);
    }

    [Fact]
    public async Task should_return_continuation_response_when_handler_succeeds()
    {
        // Act
        var response = await Act((request, _) => Task.FromResult(request + "-response"));

        // Assert
        response.Should().Be("request-response");
        _logger.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_rethrow_rpc_exception_without_wrapping_it()
    {
        // Arrange
        var rpcException = new RpcException(new Status(StatusCode.Unauthenticated, "missing bearer token"));

        // Act
        var act = async () => await Act((_, _) => throw rpcException);

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.Should().BeSameAs(rpcException);
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        exception.Which.Status.Detail.Should().Be("missing bearer token");
        _logger.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.InvalidArgument)]
    public async Task should_preserve_status_code_of_rethrown_rpc_exception(StatusCode statusCode)
    {
        // Act
        var act = async () => await Act((_, _) => throw new RpcException(new Status(statusCode, "detail")));

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(statusCode);
    }

    [Fact]
    public async Task should_wrap_unhandled_exception_in_internal_rpc_exception_with_generic_message()
    {
        // Arrange
        var unhandled = new InvalidOperationException("connection string Host=db;Password=secret");

        // Act
        var act = async () => await Act((_, _) => throw unhandled);

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Internal);
        exception.Which.Status.Detail.Should().Be("An internal error occurred.");
        exception.Which.Status.Detail.Should().NotContain(unhandled.Message);
    }

    [Fact]
    public async Task should_log_unhandled_exception_with_method_name()
    {
        // Arrange
        var unhandled = new InvalidOperationException("boom");

        // Act
        var act = async () => await Act((_, _) => throw unhandled);

        // Assert
        await act.Should().ThrowAsync<RpcException>();

        var logCall = _logger.ReceivedCalls().Should().ContainSingle().Subject;
        var arguments = logCall.GetArguments();

        logCall.GetMethodInfo().Name.Should().Be(nameof(ILogger.Log));
        arguments[0].Should().Be(LogLevel.Error);
        arguments[2]!.ToString().Should().Contain(Method);
        arguments[3].Should().BeSameAs(unhandled);
    }
}
