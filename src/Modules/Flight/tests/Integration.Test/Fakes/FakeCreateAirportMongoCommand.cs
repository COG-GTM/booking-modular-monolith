namespace Integration.Test.Fakes;

using AutoBogus;
using global::Flight.Airports.Features.CreatingAirport.V1;
using MassTransit;

public sealed class FakeCreateAirportMongoCommand : AutoFaker<CreateAirportMongo>
{
    public FakeCreateAirportMongoCommand()
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.Name, _ => "Mehrabad International Airport");
        RuleFor(r => r.Address, _ => "Tehran");
        RuleFor(r => r.Code, _ => "THR");
        RuleFor(r => r.IsDeleted, _ => false);
    }
}
