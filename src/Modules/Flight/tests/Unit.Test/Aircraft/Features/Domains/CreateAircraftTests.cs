namespace Unit.Test.Aircraft.Features.Domains
{
    using System.Linq;
    using FluentAssertions;
    using global::Flight.Aircrafts.Features.CreatingAircraft.V1;
    using global::Flight.Aircrafts.Models;
    using global::Flight.Aircrafts.ValueObjects;
    using Unit.Test.Common;
    using Unit.Test.Fakes;
    using Xunit;

    [Collection(nameof(UnitTestFixture))]
    public class CreateAircraftTests
    {
        [Fact]
        public void can_create_valid_aircraft()
        {
            // Arrange + Act
            var fakeAircraft = FakeAircraftCreate.Generate();

            // Assert
            fakeAircraft.Should().NotBeNull();
        }

        [Fact]
        public void create_sets_aggregate_fields()
        {
            // Arrange
            var id = AircraftId.Of(Guid.NewGuid());
            var name = Name.Of("Boeing");
            var model = Model.Of("737 MAX");
            var manufacturingYear = ManufacturingYear.Of(2018);

            // Act
            var aircraft = Aircraft.Create(id, name, model, manufacturingYear);

            // Assert
            aircraft.Id.Should().Be(id);
            aircraft.Name.Should().Be(name);
            aircraft.Model.Should().Be(model);
            aircraft.ManufacturingYear.Should().Be(manufacturingYear);
            aircraft.IsDeleted.Should().BeFalse();
        }

        [Fact]
        public void queue_domain_event_on_create()
        {
            // Arrange + Act
            var fakeAircraft = FakeAircraftCreate.Generate();

            // Assert
            fakeAircraft.DomainEvents.Count.Should().Be(1);
            fakeAircraft.DomainEvents.FirstOrDefault().Should().BeOfType(typeof(AircraftCreatedDomainEvent));
        }

        [Fact]
        public void domain_event_carries_aggregate_state()
        {
            // Arrange
            var id = AircraftId.Of(Guid.NewGuid());
            var name = Name.Of("Airbus");
            var model = Model.Of("A320");
            var manufacturingYear = ManufacturingYear.Of(2015);

            // Act
            var aircraft = Aircraft.Create(id, name, model, manufacturingYear, isDeleted: true);

            // Assert
            var domainEvent = aircraft.DomainEvents.Single().Should().BeOfType<AircraftCreatedDomainEvent>().Subject;
            domainEvent.Id.Should().Be(id.Value);
            domainEvent.Name.Should().Be(name.Value);
            domainEvent.Model.Should().Be(model.Value);
            domainEvent.ManufacturingYear.Should().Be(manufacturingYear.Value);
            domainEvent.IsDeleted.Should().BeTrue();
        }
    }
}
