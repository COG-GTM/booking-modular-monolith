using BuildingBlocks.Exception;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Exception;

public class GrpcExceptionInterceptorTests
{
    private readonly GrpcExceptionInterceptor _sut = new();

    private static ServerCallContext CreateContext() =>
        TestServerCallContext.Create(
            method: "/fake.Service/Method",
            host: "localhost",
            deadline: DateTime.UtcNow.AddMinutes(1),
            requestHeaders: new Metadata(),
            cancellationToken: CancellationToken.None,
            peer: "ipv4:127.0.0.1:5000",
            authContext: null,
            contextPropagationToken: null,
            writeHeadersFunc: _ => Task.CompletedTask,
            writeOptionsGetter: () => null,
            writeOptionsSetter: _ => { }
        );

    [Fact]
    public async Task successful_continuation_should_return_its_response_untouched()
    {
        var request = new FakePlainRequest("grpc");
        var context = CreateContext();
        FakePlainRequest? seenRequest = null;
        ServerCallContext? seenContext = null;

        var response = await _sut.UnaryServerHandler(
            request,
            context,
            (req, ctx) =>
            {
                seenRequest = req;
                seenContext = ctx;
                return Task.FromResult("pong");
            }
        );

        response.Should().Be("pong");
        seenRequest.Should().BeSameAs(request);
        seenContext.Should().BeSameAs(context);
    }

    [Fact]
    public async Task failing_continuation_should_be_converted_to_internal_rpc_exception_with_original_message()
    {
        var act = () =>
            _sut.UnaryServerHandler<FakePlainRequest, string>(
                new FakePlainRequest("grpc"),
                CreateContext(),
                (_, _) => throw new InvalidOperationException("something broke")
            );

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Internal);
        exception.Which.Status.Detail.Should().Be("something broke");
    }

    [Fact]
    public async Task rpc_exception_from_continuation_should_also_be_wrapped_as_internal()
    {
        var act = () =>
            _sut.UnaryServerHandler<FakePlainRequest, string>(
                new FakePlainRequest("grpc"),
                CreateContext(),
                (_, _) => Task.FromException<string>(new RpcException(new Status(StatusCode.NotFound, "missing")))
            );

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Internal);
        exception.Which.Status.Detail.Should().Contain("missing");
    }
}
