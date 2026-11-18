using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Core;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.Web;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Testing;
using Integration.Test.Fakes;
using NSubstitute;
using Xunit;

namespace Integration.Test.Booking.Features;

using global::Booking.Booking.Exceptions;
using global::Booking.Booking.Features.CreatingBook.V1;
using BookingAggregate = global::Booking.Booking.Models.Booking;

public class SeatClaimCompletionTests
{
    [Fact]
    public async Task booking_should_not_be_dispatched_or_persisted_before_seat_claim_completes_or_when_it_faults()
    {
        var repository = Substitute.For<IEventStoreDBRepository<BookingAggregate>>();
        var dispatcher = Substitute.For<IEventDispatcher>();
        var user = Substitute.For<ICurrentUserProvider>();
        user.GetCurrentUserId().Returns(1);
        repository.Find(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<BookingAggregate>(null));
        var flight = Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();
        var passenger = Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();
        flight
            .GetByIdAsync(
                Arg.Any<BookingFlight.GetByIdRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(UnaryCall(Task.FromResult(FakeFlightResponse.Generate())));
        passenger
            .GetByIdAsync(
                Arg.Any<BookingPassenger.GetByIdRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(UnaryCall(Task.FromResult(FakePassengerResponse.Generate())));
        flight
            .GetAvailableSeatsAsync(
                Arg.Any<GetAvailableSeatsRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(UnaryCall(Task.FromResult(FakeGetAvailableSeatsResponse.Generate())));
        var claim = new TaskCompletionSource<ReserveSeatResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        flight
            .ReserveSeatAsync(
                Arg.Any<ReserveSeatRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(UnaryCall(claim.Task));
        var handler = new CreateBookingCommandHandler(repository, user, dispatcher, flight, passenger);

        var handling = handler.Handle(new FakeCreateBookingCommand().Generate(), CancellationToken.None);

        handling.IsCompleted.Should().BeFalse();
        repository.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == "Add");
        dispatcher.ReceivedCalls().Should().BeEmpty();

        var conflict = new RpcException(new Status(StatusCode.Aborted, "Seat is already reserved!"));
        claim.SetException(conflict);
        var act = async () => await handling;

        (await act.Should().ThrowAsync<RpcException>()).Which.Should().BeSameAs(conflict);
        repository.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == "Add");
        dispatcher.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void no_available_seat_exception_should_report_unprocessable_entity()
    {
        var exception = new NoAvailableSeatException();

        exception.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        exception.Message.Should().Be("No available seat for this flight!");
    }

    private static AsyncUnaryCall<T> UnaryCall<T>(Task<T> response)
    {
        return TestCalls.AsyncUnaryCall(
            response,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { }
        );
    }
}
