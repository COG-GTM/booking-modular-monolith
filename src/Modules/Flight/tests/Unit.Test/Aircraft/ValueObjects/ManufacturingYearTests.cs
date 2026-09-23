namespace Unit.Test.Aircraft.ValueObjects;

using System;
using FluentAssertions;
using global::Flight.Aircrafts.Exceptions;
using global::Flight.Aircrafts.ValueObjects;
using Xunit;

public class ManufacturingYearTests
{
    [Theory]
    [InlineData(1900)]
    [InlineData(2000)]
    [InlineData(2025)]
    public void of_with_year_at_or_after_1900_should_return_manufacturing_year_with_same_value(int value)
    {
        // Act
        var manufacturingYear = ManufacturingYear.Of(value);
        int converted = manufacturingYear;

        // Assert
        manufacturingYear.Value.Should().Be(value);
        converted.Should().Be(value);
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void of_with_year_before_1900_should_throw_invalid_manufacturing_year_exception(int value)
    {
        // Act
        Action act = () => ManufacturingYear.Of(value);

        // Assert
        act.Should().Throw<InvalidManufacturingYearException>();
    }
}
