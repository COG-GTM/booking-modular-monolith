namespace Unit.Test.Airport.Features.Domains
{
    using System.Linq;
    using FluentAssertions;
    using global::Flight.Airports.Features.CreatingAirport.V1;
    using global::Flight.Airports.ValueObjects;
    using Unit.Test.Common;
    using Unit.Test.Fakes;
    using Xunit;

    [Collection(nameof(UnitTestFixture))]
    public class CreateAirportTests
    {
        [Fact]
        public void can_create_valid_airport()
        {
            // Arrange + Act
            var fakeAirport = FakeAirportCreate.Generate();

            // Assert
            fakeAirport.Should().NotBeNull();
        }

        [Fact]
        public void create_sets_fields_from_arguments()
        {
            // Arrange
            var command = new FakeCreateAirportCommand().Generate();

            // Act
            var airport = global::Flight.Airports.Models.Airport.Create(
                AirportId.Of(command.Id),
                Name.Of(command.Name),
                Address.Of(command.Address),
                Code.Of(command.Code)
            );

            // Assert
            airport.Id.Value.Should().Be(command.Id);
            airport.Name.Value.Should().Be(command.Name);
            airport.Address.Value.Should().Be(command.Address);
            airport.Code.Value.Should().Be(command.Code);
        }

        [Fact]
        public void queue_domain_event_on_create()
        {
            // Arrange + Act
            var fakeAirport = FakeAirportCreate.Generate();

            // Assert
            fakeAirport.DomainEvents.Count.Should().Be(1);
            fakeAirport.DomainEvents.FirstOrDefault().Should().BeOfType(typeof(AirportCreatedDomainEvent));
        }

        [Fact]
        public void queued_domain_event_carries_airport_state()
        {
            // Arrange
            var command = new FakeCreateAirportCommand().Generate();

            // Act
            var airport = global::Flight.Airports.Models.Airport.Create(
                AirportId.Of(command.Id),
                Name.Of(command.Name),
                Address.Of(command.Address),
                Code.Of(command.Code)
            );

            // Assert
            var @event = airport.DomainEvents.Single().Should().BeOfType<AirportCreatedDomainEvent>().Subject;
            @event.Id.Should().Be(command.Id);
            @event.Name.Should().Be(command.Name);
            @event.Address.Should().Be(command.Address);
            @event.Code.Should().Be(command.Code);
            @event.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public void queued_domain_event_reflects_is_deleted_flag()
        {
            // Arrange
            var command = new FakeCreateAirportCommand().Generate();

            // Act
            var airport = global::Flight.Airports.Models.Airport.Create(
                AirportId.Of(command.Id),
                Name.Of(command.Name),
                Address.Of(command.Address),
                Code.Of(command.Code),
                isDeleted: true
            );

            // Assert
            airport
                .DomainEvents.Single()
                .Should()
                .BeOfType<AirportCreatedDomainEvent>()
                .Which.IsDeleted.Should()
                .BeTrue();
        }
    }
}
