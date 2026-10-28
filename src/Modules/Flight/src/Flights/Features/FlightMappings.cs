using Google.Protobuf.WellKnownTypes;
using Mapster;

namespace Flight.Flights.Features;

using CreatingFlight.V1;
using DeletingFlight.V1;
using MassTransit;
using Models;
using UpdatingFlight.V1;
using FlightDto = Dtos.FlightDto;

public class FlightMappings : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Models.Flight, FlightDto>()
            .ConstructUsing(x => new FlightDto(x.Id, x.FlightNumber, x.AircraftId, x.DepartureAirportId,
                x.DepartureDate,
                x.ArriveDate, x.ArriveAirportId, x.DurationMinutes, x.FlightDate, x.Status, x.Price));

        config.NewConfig<CreateFlightMongo, FlightReadModel>()
            .Map(d => d.Id, s => NewId.NextGuid())
            .Map(d => d.FlightId, s => s.Id);

        config.NewConfig<Models.Flight, FlightReadModel>()
            .Map(d => d.Id, s => NewId.NextGuid())
            .Map(d => d.FlightId, s => s.Id.Value)
            .Map(d => d.FlightNumber, s => s.FlightNumber.Value);

        config.NewConfig<FlightReadModel, FlightDto>()
            .Map(d => d.Id, s => s.FlightId);

        config.NewConfig<FlightDto, global::Contracts.Grpc.Flight.FlightResponse>()
            .Map(d => d.DepartureDate, s => Timestamp.FromDateTime(s.DepartureDate.ToUniversalTime()))
            .Map(d => d.ArriveDate, s => Timestamp.FromDateTime(s.ArriveDate.ToUniversalTime()))
            .Map(d => d.FlightDate, s => Timestamp.FromDateTime(s.FlightDate.ToUniversalTime()));

        config.NewConfig<UpdateFlightMongo, FlightReadModel>()
            .Map(d => d.FlightId, s => s.Id);

        config.NewConfig<DeleteFlightMongo, FlightReadModel>()
            .Map(d => d.FlightId, s => s.Id);

        config.NewConfig<CreateFlightRequestDto, CreateFlight>()
            .ConstructUsing(x => new CreateFlight(x.FlightNumber, x.AircraftId, x.DepartureAirportId,
                x.DepartureDate, x.ArriveDate, x.ArriveAirportId, x.DurationMinutes, x.FlightDate, x.Status, x.Price));

        config.NewConfig<UpdateFlightRequestDto, UpdateFlight>()
            .ConstructUsing(x => new UpdateFlight(x.Id, x.FlightNumber, x.AircraftId, x.DepartureAirportId, x.DepartureDate,
                x.ArriveDate, x.ArriveAirportId, x.DurationMinutes, x.FlightDate, x.Status, x.IsDeleted, x.Price));

    }
}