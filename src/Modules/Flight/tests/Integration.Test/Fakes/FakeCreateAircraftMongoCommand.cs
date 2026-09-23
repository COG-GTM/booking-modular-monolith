namespace Integration.Test.Fakes;

using AutoBogus;
using global::Flight.Aircrafts.Features.CreatingAircraft.V1;
using MassTransit;

public sealed class FakeCreateAircraftMongoCommand : AutoFaker<CreateAircraftMongo>
{
    public FakeCreateAircraftMongoCommand()
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.Name, _ => "Boeing 737");
        RuleFor(r => r.Model, _ => "B737-800");
        RuleFor(r => r.ManufacturingYear, _ => 2010);
        RuleFor(r => r.IsDeleted, _ => false);
    }
}
