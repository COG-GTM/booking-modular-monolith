namespace Unit.Test.Fakes;

using global::Flight.Airports.ValueObjects;

public static class FakeAirportCreate
{
    public static global::Flight.Airports.Models.Airport Generate()
    {
        var command = new FakeCreateAirportCommand().Generate();

        return global::Flight.Airports.Models.Airport.Create(
            AirportId.Of(command.Id),
            Name.Of(command.Name),
            Address.Of(command.Address),
            Code.Of(command.Code)
        );
    }
}
