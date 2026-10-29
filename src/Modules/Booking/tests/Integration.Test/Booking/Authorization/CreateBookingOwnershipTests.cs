using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Booking.Booking.Exceptions;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using BookingFlight;
using BookingPassenger;
using BuildingBlocks.Constants;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.Exception;
using BuildingBlocks.Web;
using FluentAssertions;
using FluentValidation.TestHelper;
using Grpc.Core;
using Grpc.Core.Testing;
using Integration.Test.Fakes;
using NSubstitute;
using Xunit;
using BookingModel = Booking.Booking.Models.Booking;
using FlightGetByIdRequest = BookingFlight.GetByIdRequest;
using PassengerGetByIdRequest = BookingPassenger.GetByIdRequest;

namespace Integration.Test.Booking.Authorization;

public class CreateBookingOwnershipTests
{
    private readonly IEventStoreDBRepository<BookingModel> _repository = Substitute.For<
        IEventStoreDBRepository<BookingModel>
    >();

    private readonly ICurrentUserProvider _currentUserProvider = Substitute.For<ICurrentUserProvider>();
    private readonly IEventDispatcher _eventDispatcher = Substitute.For<IEventDispatcher>();

    private readonly FlightGrpcService.FlightGrpcServiceClient _flightClient =
        Substitute.For<FlightGrpcService.FlightGrpcServiceClient>();

    private readonly PassengerGrpcService.PassengerGrpcServiceClient _passengerClient =
        Substitute.For<PassengerGrpcService.PassengerGrpcServiceClient>();

    public CreateBookingOwnershipTests()
    {
        _flightClient
            .GetByIdAsync(
                Arg.Any<FlightGetByIdRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Call(FakeFlightResponse.Generate()));

        _flightClient
            .GetAvailableSeatsAsync(
                Arg.Any<GetAvailableSeatsRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Call(FakeGetAvailableSeatsResponse.Generate()));

        _flightClient
            .ReserveSeatAsync(
                Arg.Any<ReserveSeatRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Call(FakeReserveSeatResponse.Generate()));

        _passengerClient
            .GetByIdAsync(
                Arg.Any<PassengerGetByIdRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Call(FakePassengerResponse.Generate()));

        _repository.Find(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((BookingModel?)null);
        _repository.Add(Arg.Any<BookingModel>(), Arg.Any<CancellationToken>()).Returns(7UL);
    }

    [Fact]
    public async Task owner_can_create_booking_for_their_own_passenger()
    {
        var command = new FakeCreateBookingCommand().Generate();
        command.Id.Should().NotBeEmpty();
        SignInAs(command.PassengerId, isAdmin: false);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.Id.Should().Be(7UL);
        _ = _passengerClient
            .Received(1)
            .GetByIdAsync(
                Arg.Is<PassengerGetByIdRequest>(r => r.Id == command.PassengerId.ToString()),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
        _ = _repository
            .Received(1)
            .Add(
                Arg.Is<BookingModel>(booking => booking.Id == command.Id),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void booking_created_event_round_trips_through_event_store_serializer()
    {
        var now = DateTime.UtcNow;
        var @event = new BookingCreatedDomainEvent(
            Guid.NewGuid(),
            PassengerInfo.Of("Test Passenger"),
            Trip.Of("AB123", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now, 100, "description", "1A")
        )
        {
            CreatedAt = now,
            CreatedBy = 123,
            IsDeleted = true
        };

        var eventData = @event.ToJsonEventData();
        var serializedEvent = Encoding.UTF8.GetString(eventData.Data.Span);
        var deserializedEvent = serializedEvent.FromJson<BookingCreatedDomainEvent>();

        deserializedEvent.Id.Should().Be(@event.Id);
        deserializedEvent.CreatedAt.Should().Be(@event.CreatedAt);
        deserializedEvent.CreatedBy.Should().Be(@event.CreatedBy);
        deserializedEvent.IsDeleted.Should().Be(@event.IsDeleted);
        deserializedEvent.PassengerInfo.Should().BeEquivalentTo(@event.PassengerInfo);
        deserializedEvent.Trip.Should().BeEquivalentTo(@event.Trip);
    }

    [Fact]
    public async Task booking_for_another_passenger_is_forbidden_before_any_downstream_call()
    {
        var command = new FakeCreateBookingCommand().Generate();
        SignInAs(Guid.NewGuid(), isAdmin: false);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<PassengerBookingForbiddenException>();
        AssertNoSideEffects();
    }

    [Fact]
    public async Task caller_without_a_user_id_is_forbidden_before_any_downstream_call()
    {
        var command = new FakeCreateBookingCommand().Generate();
        SignInAs(null, isAdmin: false);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<PassengerBookingForbiddenException>();
        AssertNoSideEffects();
    }

    [Fact]
    public async Task caller_with_an_empty_user_id_is_forbidden()
    {
        var command = new FakeCreateBookingCommand().Generate() with { PassengerId = Guid.NewGuid() };
        SignInAs(Guid.Empty, isAdmin: false);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<PassengerBookingForbiddenException>();
        AssertNoSideEffects();
    }

    [Fact]
    public async Task non_admin_role_check_uses_the_admin_role_constant()
    {
        var command = new FakeCreateBookingCommand().Generate();
        SignInAs(Guid.NewGuid(), isAdmin: false);
        _currentUserProvider.IsInRole(IdentityConstant.Role.User).Returns(true);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<PassengerBookingForbiddenException>();
        _currentUserProvider.Received().IsInRole(IdentityConstant.Role.Admin);
    }

    [Fact]
    public async Task admin_can_create_booking_for_another_passenger()
    {
        var command = new FakeCreateBookingCommand().Generate();
        SignInAs(Guid.NewGuid(), isAdmin: true);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.Id.Should().Be(7UL);
        _ = _passengerClient
            .Received(1)
            .GetByIdAsync(
                Arg.Is<PassengerGetByIdRequest>(r => r.Id == command.PassengerId.ToString()),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task admin_without_a_user_id_can_still_create_booking()
    {
        var command = new FakeCreateBookingCommand().Generate();
        SignInAs(null, isAdmin: true);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.Id.Should().Be(7UL);
    }

    [Fact]
    public async Task missing_passenger_throws_not_found_without_reserving_a_seat()
    {
        var command = new FakeCreateBookingCommand().Generate();
        SignInAs(command.PassengerId, isAdmin: false);
        _passengerClient
            .GetByIdAsync(
                Arg.Any<PassengerGetByIdRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => Call(new GetPassengerByIdResult()));

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<PassengerNotFoundException>();
        _flightClient
            .ReceivedCalls()
            .Select(c => c.GetMethodInfo().Name)
            .Should()
            .NotContain(nameof(FlightGrpcService.FlightGrpcServiceClient.ReserveSeatAsync))
            .And.NotContain(nameof(FlightGrpcService.FlightGrpcServiceClient.GetAvailableSeatsAsync));
        _repository.ReceivedCalls().Should().BeEmpty();
        _eventDispatcher.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void validator_rejects_empty_passenger_and_flight_ids()
    {
        var command = new CreateBooking(Guid.Empty, Guid.Empty, "desc");

        var result = new CreateBookingValidator().TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.PassengerId).WithErrorMessage("PassengerId is required!");
        result.ShouldHaveValidationErrorFor(x => x.FlightId).WithErrorMessage("FlightId is required!");
    }

    [Fact]
    public void validator_accepts_non_empty_ids()
    {
        var result = new CreateBookingValidator().TestValidate(new FakeCreateBookingCommand().Generate());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void forbidden_exception_carries_403_and_not_found_exception_carries_404()
    {
        var forbidden = new PassengerBookingForbiddenException();
        var notFound = new PassengerNotFoundException();

        forbidden.Should().BeAssignableTo<ForbiddenException>();
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forbidden.Message.Should().Be("You are not allowed to create a booking for this passenger.");
        notFound.Should().BeAssignableTo<NotFoundException>();
        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private CreateBookingCommandHandler CreateHandler() =>
        new(_repository, _currentUserProvider, _eventDispatcher, _flightClient, _passengerClient);

    private void SignInAs(Guid? userId, bool isAdmin)
    {
        _currentUserProvider.GetCurrentUserGuid().Returns(userId);
        _currentUserProvider.IsInRole(IdentityConstant.Role.Admin).Returns(isAdmin);
    }

    private void AssertNoSideEffects()
    {
        _flightClient.ReceivedCalls().Should().BeEmpty();
        _passengerClient.ReceivedCalls().Should().BeEmpty();
        _repository.ReceivedCalls().Should().BeEmpty();
        _eventDispatcher.ReceivedCalls().Should().BeEmpty();
    }

    private static AsyncUnaryCall<T> Call<T>(T response) =>
        TestCalls.AsyncUnaryCall(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { }
        );
}
