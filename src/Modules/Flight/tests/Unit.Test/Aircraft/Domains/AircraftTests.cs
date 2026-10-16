namespace Unit.Test.Aircraft.Domains;

using System;
using FluentAssertions;
using global::Flight.Aircrafts.Exceptions;
using global::Flight.Aircrafts.Models;
using global::Flight.Aircrafts.ValueObjects;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class AircraftTests
{
    [Fact]
    public void create_sets_properties_and_queues_created_event()
    {
        var id = Guid.NewGuid();

        var aircraft = Aircraft.Create(
            AircraftId.Of(id),
            Name.Of("Airbus"),
            Model.Of("A320"),
            ManufacturingYear.Of(2020)
        );

        aircraft.Id.Value.Should().Be(id);
        aircraft.Name.Value.Should().Be("Airbus");
        aircraft.Model.Value.Should().Be("A320");
        aircraft.ManufacturingYear.Value.Should().Be(2020);
        aircraft
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<global::Flight.Aircrafts.Features.CreatingAircraft.V1.AircraftCreatedDomainEvent>()
            .Subject.Id.Should()
            .Be(id);
    }

    [Fact]
    public void aircraft_id_rejects_empty_guid_and_includes_it_in_the_message()
    {
        var act = () => AircraftId.Of(Guid.Empty);

        act.Should().Throw<InvalidAircraftIdException>().Which.Message.Should().Contain(Guid.Empty.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void name_rejects_null_empty_and_whitespace(string value)
    {
        var act = () => Name.Of(value);

        act.Should().Throw<InvalidNameException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void model_rejects_null_empty_and_whitespace(string value)
    {
        var act = () => Model.Of(value);

        act.Should().Throw<InvalidModelException>();
    }

    [Fact]
    public void manufacturing_year_rejects_years_before_1900_and_accepts_1900()
    {
        var act = () => ManufacturingYear.Of(1899);

        act.Should().Throw<InvalidManufacturingYearException>();
        ManufacturingYear.Of(1900).Value.Should().Be(1900);
    }

    [Fact]
    public void aircraft_value_objects_convert_to_their_raw_values_and_record_values_compare_equal()
    {
        var id = Guid.NewGuid();
        Guid rawId = AircraftId.Of(id);
        string rawName = Name.Of("Airbus");
        string rawModel = Model.Of("A320");
        int rawYear = ManufacturingYear.Of(2020);

        rawId.Should().Be(id);
        rawName.Should().Be("Airbus");
        rawModel.Should().Be("A320");
        rawYear.Should().Be(2020);
        AircraftId.Of(id).Should().Be(AircraftId.Of(id));
        Name.Of("Airbus").Should().Be(Name.Of("Airbus"));
        Model.Of("A320").Should().Be(Model.Of("A320"));
        ManufacturingYear.Of(2020).Should().Be(ManufacturingYear.Of(2020));
    }
}
