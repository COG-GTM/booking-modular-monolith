using Booking;
using Flight;
using Identity;
using NetArchTest.Rules;
using Passenger;
using Xunit;

namespace Architecture.Tests;

public class ModuleBoundaryTests
{
    [Fact]
    public void FlightModule_ShouldNotDependOn_PassengerOrIdentityOrBookingData()
    {
        var result = Types
            .InAssembly(typeof(FlightRoot).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Passenger.Data", "Identity.Data", "Booking.Data")
            .GetResult();

        Assert.True(result.IsSuccessful, BuildFailureMessage("Flight module", result.FailingTypeNames));
    }

    [Fact]
    public void PassengerModule_ShouldNotDependOn_FlightOrIdentityOrBookingData()
    {
        var result = Types
            .InAssembly(typeof(PassengerRoot).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Flight.Data", "Identity.Data", "Booking.Data")
            .GetResult();

        Assert.True(result.IsSuccessful, BuildFailureMessage("Passenger module", result.FailingTypeNames));
    }

    [Fact]
    public void IdentityModule_ShouldNotDependOn_FlightOrPassengerOrBookingData()
    {
        var result = Types
            .InAssembly(typeof(IdentityRoot).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Flight.Data", "Passenger.Data", "Booking.Data")
            .GetResult();

        Assert.True(result.IsSuccessful, BuildFailureMessage("Identity module", result.FailingTypeNames));
    }

    [Fact]
    public void BookingModule_ShouldNotDependOn_FlightOrPassengerOrIdentityData()
    {
        var result = Types
            .InAssembly(typeof(BookingRoot).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Flight.Data", "Passenger.Data", "Identity.Data")
            .GetResult();

        Assert.True(result.IsSuccessful, BuildFailureMessage("Booking module", result.FailingTypeNames));
    }

    private static string BuildFailureMessage(string moduleName, IEnumerable<string>? failingTypes)
    {
        var types = failingTypes is null ? "<none>" : string.Join(", ", failingTypes);
        return $"{moduleName} has a forbidden cross-module Data dependency. Failing types: {types}";
    }
}
