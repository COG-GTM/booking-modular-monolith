namespace Unit.Test.Fakes;

using global::Flight.Aircrafts.Models;
using global::Flight.Aircrafts.ValueObjects;

public static class FakeAircraftCreate
{
    public static Aircraft Generate()
    {
        var command = new FakeCreateAircraftCommand().Generate();

        return Aircraft.Create(
            AircraftId.Of(command.Id),
            Name.Of(command.Name),
            Model.Of(command.Model),
            ManufacturingYear.Of(command.ManufacturingYear)
        );
    }
}
