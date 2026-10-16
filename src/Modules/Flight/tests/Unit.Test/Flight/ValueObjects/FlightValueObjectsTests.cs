namespace Unit.Test.Flight.ValueObjects;

using System;
using FluentAssertions;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.ValueObjects;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class FlightValueObjectsTests
{
    [Fact]
    public void flight_id_rejects_empty_guid_and_includes_it_in_the_message()
    {
        var act = () => FlightId.Of(Guid.Empty);

        act.Should().Throw<InvalidFlightIdException>().Which.Message.Should().Contain(Guid.Empty.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void flight_number_rejects_null_empty_and_whitespace(string value)
    {
        var act = () => FlightNumber.Of(value);

        act.Should().Throw<InvalidFlightNumberException>();
    }

    [Fact]
    public void price_rejects_negative_values_and_accepts_zero()
    {
        var act = () => Price.Of(-1);

        act.Should().Throw<InvalidPriceException>();
        Price.Of(0).Value.Should().Be(0);
    }

    [Fact]
    public void duration_rejects_negative_values_and_accepts_zero()
    {
        var act = () => DurationMinutes.Of(-1);

        act.Should().Throw<InvalidDurationException>();
        DurationMinutes.Of(0).Value.Should().Be(0);
    }

    [Fact]
    public void date_value_objects_reject_default_values()
    {
        var arrive = () => ArriveDate.Of(default);
        var departure = () => DepartureDate.Of(default);
        var flight = () => FlightDate.Of(default);

        arrive.Should().Throw<InvalidArriveDateException>();
        departure.Should().Throw<InvalidDepartureDateException>();
        flight.Should().Throw<InvalidFlightDateException>();
    }

    [Fact]
    public void flight_value_objects_convert_to_raw_values_and_records_have_value_equality()
    {
        var id = Guid.NewGuid();
        var date = new DateTime(2035, 2, 3, 4, 5, 0);
        Guid rawId = FlightId.Of(id);
        string rawNumber = FlightNumber.Of("FL123");
        decimal rawPrice = Price.Of(12.5m);
        decimal rawDuration = DurationMinutes.Of(90);
        DateTime rawDate = FlightDate.Of(date);

        rawId.Should().Be(id);
        rawNumber.Should().Be("FL123");
        rawPrice.Should().Be(12.5m);
        rawDuration.Should().Be(90);
        rawDate.Should().Be(date);
        FlightId.Of(id).Should().Be(FlightId.Of(id));
        FlightNumber.Of("FL123").Should().Be(FlightNumber.Of("FL123"));
        ArriveDate.Of(date).Should().Be(ArriveDate.Of(date));
        DepartureDate.Of(date).Should().Be(DepartureDate.Of(date));
        FlightDate.Of(date).Should().Be(FlightDate.Of(date));
    }
}
