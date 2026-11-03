using System.Runtime.CompilerServices;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;

[assembly: TypeForwardedTo(typeof(IEvent))]
[assembly: TypeForwardedTo(typeof(IIntegrationEvent))]
[assembly: TypeForwardedTo(typeof(FlightCreated))]
[assembly: TypeForwardedTo(typeof(FlightUpdated))]
[assembly: TypeForwardedTo(typeof(FlightDeleted))]
[assembly: TypeForwardedTo(typeof(AircraftCreated))]
[assembly: TypeForwardedTo(typeof(AirportCreated))]
[assembly: TypeForwardedTo(typeof(SeatCreated))]
[assembly: TypeForwardedTo(typeof(SeatReserved))]
[assembly: TypeForwardedTo(typeof(UserCreated))]
[assembly: TypeForwardedTo(typeof(PassengerRegistrationCompleted))]
[assembly: TypeForwardedTo(typeof(PassengerCreated))]
[assembly: TypeForwardedTo(typeof(BookingCreated))]
