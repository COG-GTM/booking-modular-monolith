using System.Runtime.CompilerServices;
using BuildingBlocks.Contracts.EventBus.Messages;

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
