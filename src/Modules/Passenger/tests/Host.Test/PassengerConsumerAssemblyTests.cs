using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Passenger.Host.Extensions;
using Xunit;

namespace Passenger.Host.Test;

public class PassengerConsumerAssemblyTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void consumer_assemblies_follow_background_processing_setting(string value, bool expectedEnabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Modules:Passenger:BackgroundProcessingEnabled"] = value }
            )
            .Build();

        var assemblies = PassengerHostExtensions.PassengerConsumerAssemblies(configuration);

        if (expectedEnabled)
            assemblies
                .Should()
                .ContainSingle()
                .Which.FullName.Should()
                .Be(typeof(global::Passenger.PassengerRoot).Assembly.FullName);
        else
            assemblies.Should().BeEmpty();
    }
}
