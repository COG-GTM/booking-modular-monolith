using BuildingBlocks.Exception;
using FluentAssertions;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight.Seats.Exceptions;

public class GrpcExceptionInterceptorTests
{
    private readonly GrpcExceptionInterceptor _interceptor = new();

    [Fact]
    public async Task conflict_app_exception_should_be_mapped_to_already_exists()
    {
        var act = () => Invoke(new SeatAlreadyReservedException());

        var ex = (await act.Should().ThrowAsync<RpcException>()).Which;
        ex.StatusCode.Should().Be(StatusCode.AlreadyExists);
        ex.Status.Detail.Should().Be(new SeatAlreadyReservedException().Message);
    }

    [Fact]
    public async Task db_update_concurrency_exception_should_be_mapped_to_aborted()
    {
        var act = () => Invoke(new DbUpdateConcurrencyException("conflict"));

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Aborted);
    }

    [Fact]
    public async Task other_exceptions_should_be_mapped_to_internal()
    {
        var act = () => Invoke(new SeatNumberIncorrectException());

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Internal);
    }

    private Task<object> Invoke(System.Exception exception) =>
        _interceptor.UnaryServerHandler<object, object>(new object(), null!, (_, _) => throw exception);
}
