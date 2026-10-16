using BuildingBlocks.Exception;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace BuildingBlocks.Unit.Test.ExceptionTests;

public class GrpcExceptionInterceptorTests
{
    [Fact]
    public async Task unary_server_handler_passes_the_continuation_result_through()
    {
        var interceptor = new GrpcExceptionInterceptor();

        var result = await interceptor.UnaryServerHandler<string, string>(
            "request",
            context: null!,
            continuation: (req, _) => Task.FromResult($"handled-{req}")
        );

        result.Should().Be("handled-request");
    }

    [Fact]
    public async Task unary_server_handler_wraps_failures_in_rpc_exception_with_the_original_message()
    {
        var interceptor = new GrpcExceptionInterceptor();
        var original = new InvalidOperationException("the original failure");

        var act = () =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                context: null!,
                continuation: (_, _) => Task.FromException<string>(original)
            );

        var rpc = await act.Should().ThrowAsync<RpcException>();
        rpc.Which.Status.Detail.Should().Be("the original failure");
        rpc.Which.Status.StatusCode.Should().Be(Grpc.Core.StatusCode.Internal);
    }
}
