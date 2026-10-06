using AutoBogus;

namespace EndToEnd.Test.Fakes;

using global::Flight.Aircrafts.Features.CreatingAircraft.V1;
using MassTransit;

public sealed class FakeCreateAircraftCommand : AutoFaker<CreateAircraft>
{
    public FakeCreateAircraftCommand()
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.Name, _ => "Boeing 777");
        RuleFor(r => r.Model, _ => "B777");
        RuleFor(r => r.ManufacturingYear, _ => 2010);
    }
}
