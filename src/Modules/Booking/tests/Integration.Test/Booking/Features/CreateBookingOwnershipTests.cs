using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Booking.Booking.Exceptions;
using Booking.Booking.Features.CreatingBook.V1;
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
using BookingAggregate = Booking.Booking.Models.Booking;

namespace Integration.Test.Booking.Features;

public class CreateBookingOwnershipTests
{
    private readonly IEventStoreDBRepository<BookingAggregate> repository = Substitute.For<
        IEventStoreDBRepository<BookingAggregate>
    >();
    private readonly ICurrentUserProvider currentUser = Substitute.For<ICurrentUserProvider>();
    private readonly IEventDispatcher dispatcher = Substitute.For<IEventDispatcher>();
    private readonly FlightGrpcService.FlightGrpcServiceClient flight =
        Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();
    private readonly PassengerGrpcService.PassengerGrpcServiceClient passenger =
        Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();

    [Fact]
    public async Task should_deny_missing_current_user_before_any_external_call()
    {
        currentUser.GetCurrentUserGuid().Returns((Guid?)null);

        var act = () => Handler().Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<PassengerAccessDeniedException>();
        passenger.ReceivedCalls().Should().BeEmpty();
        AssertNoSideEffects();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("1968659a-4ac5-47bb-976b-ab69e6f7eacd")]
    public async Task should_deny_missing_invalid_or_different_passenger_owner(string? owner)
    {
        currentUser.GetCurrentUserGuid().Returns(Guid.NewGuid());
        var response = new GetPassengerByIdResult();
        if (owner is not null)
        {
            response.PassengerDto = new PassengerResponse { UserId = owner };
        }

        passenger.GetByIdAsync(Arg.Any<BookingPassenger.GetByIdRequest>()).Returns(Call(response));

        var act = () => Handler().Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<PassengerAccessDeniedException>();
        passenger.ReceivedCalls().Should().ContainSingle();
        AssertNoSideEffects();
    }

    [Fact]
    public async Task should_reserve_and_persist_owners_booking_with_audit_fields()
    {
        var userId = Guid.NewGuid();
        var command = Command();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        currentUser.GetCurrentUserGuid().Returns(userId);
        passenger
            .GetByIdAsync(Arg.Any<BookingPassenger.GetByIdRequest>(), cancellationToken: token)
            .Returns(Call(FakePassengerResponse.Generate(userId)));
        flight
            .GetByIdAsync(Arg.Any<BookingFlight.GetByIdRequest>(), cancellationToken: token)
            .Returns(Call(FakeFlightResponse.Generate()));
        flight
            .GetAvailableSeatsAsync(Arg.Any<GetAvailableSeatsRequest>(), cancellationToken: token)
            .Returns(Call(FakeGetAvailableSeatsResponse.Generate()));
        flight
            .ReserveSeatAsync(Arg.Any<ReserveSeatRequest>(), cancellationToken: token)
            .Returns(Call(FakeReserveSeatResponse.Generate()));
        BookingAggregate? saved = null;
        repository.Add(Arg.Do<BookingAggregate>(aggregate => saved = aggregate), token).Returns(7UL);

        var result = await Handler().Handle(command, token);

        result.Id.Should().Be(7UL);
        saved.Should().NotBeNull();
        var created = saved!.DomainEvents.OfType<BookingCreatedDomainEvent>().Should().ContainSingle().Subject;
        created.PassengerId.Should().Be(command.PassengerId);
        created.BookedByUserId.Should().Be(userId);
        dispatcher.ReceivedCalls().Should().ContainSingle();
        await repository.Received(1).Find(command.Id, token);
        await repository.Received(1).Add(saved, token);
        using var reservation = flight
            .Received(1)
            .ReserveSeatAsync(
                Arg.Is<ReserveSeatRequest>(request =>
                    request.SeatNumber == "33F" && request.FlightId == FakeFlightResponse.Generate().FlightDto.Id
                ),
                cancellationToken: token
            );
    }

    [Theory]
    [InlineData(true, false, "FlightId")]
    [InlineData(false, true, "PassengerId")]
    [InlineData(true, true, "FlightId,PassengerId")]
    [InlineData(false, false, "")]
    public void should_require_nonempty_flight_and_passenger_ids(
        bool emptyFlight,
        bool emptyPassenger,
        string expectedProperties
    )
    {
        var command = new CreateBooking(
            emptyPassenger ? Guid.Empty : Guid.NewGuid(),
            emptyFlight ? Guid.Empty : Guid.NewGuid(),
            "test"
        );

        var result = new CreateBookingValidator().Validate(command);

        result
            .Errors.Select(error => error.PropertyName)
            .Should()
            .BeEquivalentTo(expectedProperties.Split(',', StringSplitOptions.RemoveEmptyEntries));
        result.IsValid.Should().Be(expectedProperties.Length == 0);
    }

    private void AssertNoSideEffects()
    {
        flight.ReceivedCalls().Should().BeEmpty();
        repository.ReceivedCalls().Should().BeEmpty();
        dispatcher.ReceivedCalls().Should().BeEmpty();
    }

    private CreateBookingCommandHandler Handler() => new(repository, currentUser, dispatcher, flight, passenger);

    private static CreateBooking Command() => new(Guid.NewGuid(), Guid.NewGuid(), "test");

    private static AsyncUnaryCall<T> Call<T>(T response) =>
        TestCalls.AsyncUnaryCall(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { }
        );
}
