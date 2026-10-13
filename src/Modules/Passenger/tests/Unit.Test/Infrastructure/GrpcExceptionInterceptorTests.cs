namespace Unit.Test.Infrastructure;

using BuildingBlocks.Exception;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Xunit;

public class GrpcExceptionInterceptorTests
{
    private readonly GrpcExceptionInterceptor _interceptor = new();

    [Fact]
    public async Task should_rethrow_rpc_exception_unchanged()
    {
        var original = new RpcException(
            new Status(StatusCode.PermissionDenied, "not the owner"),
            new Metadata { { "reason", "ownership" } }
        );

        var act = async () =>
            await _interceptor.UnaryServerHandler<string, string>("request", CreateContext(), (_, _) => throw original);

        var thrown = (await act.Should().ThrowAsync<RpcException>()).Which;
        thrown.Should().BeSameAs(original);
        thrown.StatusCode.Should().Be(StatusCode.PermissionDenied);
        thrown.Status.Detail.Should().Be("not the owner");
        thrown.Trailers.GetValue("reason").Should().Be("ownership");
    }

    [Fact]
    public async Task should_wrap_non_rpc_exception_as_internal_rpc_exception()
    {
        var act = async () =>
            await _interceptor.UnaryServerHandler<string, string>(
                "request",
                CreateContext(),
                (_, _) => throw new InvalidOperationException("boom")
            );

        var thrown = (await act.Should().ThrowAsync<RpcException>()).Which;
        thrown.StatusCode.Should().Be(StatusCode.Internal);
        thrown.Status.Detail.Should().Be("boom");
    }

    [Fact]
    public async Task should_return_continuation_result_when_no_exception()
    {
        var result = await _interceptor.UnaryServerHandler<string, string>(
            "request",
            CreateContext(),
            (request, _) => Task.FromResult(request + "-response")
        );

        result.Should().Be("request-response");
    }

    private static ServerCallContext CreateContext() =>
        TestServerCallContext.Create(
            "method",
            "host",
            DateTime.UtcNow.AddMinutes(1),
            new Metadata(),
            CancellationToken.None,
            "peer",
            null,
            null,
            _ => Task.CompletedTask,
            () => new WriteOptions(),
            _ => { }
        );
}
