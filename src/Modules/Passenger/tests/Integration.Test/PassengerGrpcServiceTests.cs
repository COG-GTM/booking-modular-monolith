using BuildingBlocks.Exception;
using Grpc.Core;
using Grpc.Core.Testing;
using MediatR;
using NSubstitute;
using Passenger;
using Passenger.GrpcServer.Services;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;
using QueryResult = Passenger.Passengers.Features.GettingPassengerById.V1.GetPassengerByIdResult;

namespace Integration.Test;

public class PassengerGrpcServiceTests
{
    [Fact]
    public async Task should_preserve_not_found_through_the_service_and_global_interceptor()
    {
        var mediator = Substitute.For<IMediator>();
        mediator
            .Send(Arg.Any<GetPassengerById>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<QueryResult>(new PassengerNotFoundException()));
        var service = new PassengerGrpcServices(mediator);
        var interceptor = new GrpcExceptionInterceptor();
        var request = new GetByIdRequest { Id = Guid.NewGuid().ToString() };

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => interceptor.UnaryServerHandler(request, CreateContext(CancellationToken.None), service.GetById)
        );

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
        Assert.Equal("Passenger not found!", exception.Status.Detail);
    }

    [Fact]
    public async Task should_send_the_requested_id_and_call_cancellation_to_mediator()
    {
        using var cts = new CancellationTokenSource();
        var id = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        mediator
            .Send(Arg.Any<GetPassengerById>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<QueryResult>(new PassengerNotFoundException()));
        var service = new PassengerGrpcServices(mediator);

        await Assert.ThrowsAsync<RpcException>(
            () => service.GetById(new GetByIdRequest { Id = id.ToString() }, CreateContext(cts.Token))
        );

        await mediator.Received(1).Send(Arg.Is<GetPassengerById>(query => query.Id == id), cts.Token);
    }

    [Theory]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.Cancelled)]
    public async Task should_rethrow_existing_rpc_exceptions_with_trailers_intact(StatusCode statusCode)
    {
        var original = new RpcException(
            new Status(statusCode, "Expected failure"),
            new Metadata { { "error-id", "test" } }
        );
        var interceptor = new GrpcExceptionInterceptor();

        var actual = await Assert.ThrowsAsync<RpcException>(
            () =>
                interceptor.UnaryServerHandler(
                    new GetByIdRequest(),
                    CreateContext(CancellationToken.None),
                    (_, _) => Task.FromException<global::Passenger.GetPassengerByIdResult>(original)
                )
        );

        Assert.Same(original, actual);
        Assert.Equal("test", actual.Trailers.GetValue("error-id"));
    }

    [Fact]
    public async Task should_still_translate_unexpected_exceptions_to_internal()
    {
        var interceptor = new GrpcExceptionInterceptor();

        var exception = await Assert.ThrowsAsync<RpcException>(
            () =>
                interceptor.UnaryServerHandler(
                    new GetByIdRequest(),
                    CreateContext(CancellationToken.None),
                    (_, _) =>
                        Task.FromException<global::Passenger.GetPassengerByIdResult>(
                            new InvalidOperationException("Failure")
                        )
                )
        );

        Assert.Equal(StatusCode.Internal, exception.StatusCode);
    }

    private static ServerCallContext CreateContext(CancellationToken cancellationToken) =>
        TestServerCallContext.Create(
            "/passenger.PassengerGrpcService/GetById",
            "localhost",
            DateTime.MaxValue,
            new Metadata(),
            cancellationToken,
            "test-peer",
            null!,
            null!,
            _ => Task.CompletedTask,
            () => new WriteOptions(),
            _ => { }
        );
}
