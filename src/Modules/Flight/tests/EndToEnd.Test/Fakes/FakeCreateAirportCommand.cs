using AutoBogus;

namespace EndToEnd.Test.Fakes;

using global::Flight.Airports.Features.CreatingAirport.V1;
using MassTransit;

public sealed class FakeCreateAirportCommand : AutoFaker<CreateAirport>
{
    public FakeCreateAirportCommand()
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.Name, _ => "Madrid Barajas Airport");
        RuleFor(r => r.Address, _ => "MAD");
        RuleFor(r => r.Code, _ => "13500");
    }
}
