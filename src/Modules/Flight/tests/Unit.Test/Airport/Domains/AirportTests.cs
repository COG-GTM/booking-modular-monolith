namespace Unit.Test.Airport.Domains;

using System;
using FluentAssertions;
using global::Flight.Airports.Exceptions;
using global::Flight.Airports.Models;
using global::Flight.Airports.ValueObjects;
using Unit.Test.Common;
using Xunit;
using AirportName = global::Flight.Airports.ValueObjects.Name;

[Collection(nameof(UnitTestFixture))]
public class AirportTests
{
    [Fact]
    public void create_sets_properties_and_queues_created_event()
    {
        var id = Guid.NewGuid();

        var airport = Airport.Create(
            AirportId.Of(id),
            AirportName.Of("Lisbon"),
            Address.Of("Terminal 1"),
            Code.Of("LIS")
        );

        airport.Id.Value.Should().Be(id);
        airport.Name.Value.Should().Be("Lisbon");
        airport.Address.Value.Should().Be("Terminal 1");
        airport.Code.Value.Should().Be("LIS");
        airport
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<global::Flight.Airports.Features.CreatingAirport.V1.AirportCreatedDomainEvent>()
            .Subject.Id.Should()
            .Be(id);
    }

    [Fact]
    public void airport_id_rejects_empty_guid_and_includes_it_in_the_message()
    {
        var act = () => AirportId.Of(Guid.Empty);

        act.Should().Throw<InvalidAirportIdException>().Which.Message.Should().Contain(Guid.Empty.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void name_rejects_null_empty_and_whitespace(string value)
    {
        var act = () => AirportName.Of(value);

        act.Should().Throw<InvalidNameException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void address_rejects_null_empty_and_whitespace(string value)
    {
        var act = () => Address.Of(value);

        act.Should().Throw<InvalidAddressException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void code_rejects_null_empty_and_whitespace(string value)
    {
        var act = () => Code.Of(value);

        act.Should().Throw<InvalidCodeException>();
    }

    [Fact]
    public void airport_value_objects_convert_to_their_raw_values_and_record_values_compare_equal()
    {
        var id = Guid.NewGuid();
        Guid rawId = AirportId.Of(id);
        string rawName = AirportName.Of("Lisbon");
        string rawCode = Code.Of("LIS");
        string rawAddress = Address.Of("Terminal 1");

        rawId.Should().Be(id);
        rawName.Should().Be("Lisbon");
        rawCode.Should().Be("LIS");
        rawAddress.Should().Be("Terminal 1");
        AirportId.Of(id).Should().Be(AirportId.Of(id));
        AirportName.Of("Lisbon").Should().Be(AirportName.Of("Lisbon"));
        Code.Of("LIS").Should().Be(Code.Of("LIS"));
    }
}
