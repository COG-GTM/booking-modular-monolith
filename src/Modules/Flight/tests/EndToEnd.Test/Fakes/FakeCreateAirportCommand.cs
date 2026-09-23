namespace EndToEnd.Test.Fakes;

using AutoBogus;
using global::Flight.Airports.Features.CreatingAirport.V1;
using MassTransit;

public sealed class FakeCreateAirportCommand : AutoFaker<CreateAirport>
{
    public FakeCreateAirportCommand()
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.Name, r => r.Address.City() + " International Airport");
        RuleFor(r => r.Address, r => r.Address.CountryCode());
        RuleFor(r => r.Code, _ => NewId.NextGuid().ToString("N")[..8]);
    }
}
