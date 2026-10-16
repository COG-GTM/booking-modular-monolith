using BuildingBlocks.Web;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Unit.Test.WebTests;

public class SlugifyParameterTransformerTests
{
    [Theory]
    [InlineData("FlightBooking", "flight-booking")]
    [InlineData("getById", "get-by-id")]
    [InlineData("flight", "flight")]
    public void transform_outbound_slugifies_pascal_and_camel_case(string input, string expected)
    {
        new SlugifyParameterTransformer().TransformOutbound(input).Should().Be(expected);
    }

    [Fact]
    public void transform_outbound_returns_null_for_null()
    {
        new SlugifyParameterTransformer().TransformOutbound(null!).Should().BeNull();
    }
}
