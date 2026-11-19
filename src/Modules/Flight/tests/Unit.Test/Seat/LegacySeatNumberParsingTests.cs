using Flight.Data.Seed;
using FluentAssertions;
using Xunit;

namespace Unit.Test.Seat;

public class LegacySeatNumberParsingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("SeatNumber { Value = 12A")]
    [InlineData("SeatNumber { Value = 12A }suffix")]
    [InlineData("prefixSeatNumber { Value = 12A }")]
    [InlineData("seatNumber { Value = 12A }")]
    [InlineData("SeatNumber { Value = 12A}")]
    public void malformed_legacy_values_are_left_unchanged(string stored)
    {
        FlightDataSeeder.TryParseLegacySeatNumber(stored, out var seatNumber).Should().BeFalse();
        seatNumber.Should().Be(stored);
    }

    [Fact]
    public void repair_is_idempotent_for_a_parsed_legacy_value()
    {
        FlightDataSeeder.TryParseLegacySeatNumber("SeatNumber { Value = 12B }", out var raw).Should().BeTrue();
        raw.Should().Be("12B");

        FlightDataSeeder.TryParseLegacySeatNumber(raw, out var unchanged).Should().BeFalse();
        unchanged.Should().Be(raw);
    }
}
