using BuildingBlocks.Web;
using Flight.Data;
using FluentAssertions;
using global::Flight.Aircrafts.ValueObjects;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using AircraftModel = global::Flight.Aircrafts.Models.Aircraft;

namespace Unit.Test.Audit;

public class AuditUpdateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task update_should_preserve_creator_and_record_current_modifier(bool anonymous)
    {
        var creator = Guid.NewGuid();
        Guid? modifier = anonymous ? null : Guid.NewGuid();
        var provider = Substitute.For<ICurrentUserProvider>();
        provider.GetCurrentUserId().Returns(creator);
        var options = new DbContextOptionsBuilder<FlightDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new FlightDbContext(options, provider);
        var aircraft = AircraftModel.Create(
            AircraftId.Of(Guid.NewGuid()),
            Name.Of("Boeing 737"),
            Model.Of("B737"),
            ManufacturingYear.Of(2005)
        );
        context.Aircraft.Add(aircraft);
        await context.SaveChangesAsync();
        var createdAt = aircraft.CreatedAt;
        provider.GetCurrentUserId().Returns(modifier);
        context.Entry(aircraft).State = EntityState.Modified;

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var persisted = await context.Aircraft.SingleAsync();

        persisted.CreatedBy.Should().Be(creator);
        persisted.CreatedAt.Should().Be(createdAt);
        persisted.LastModifiedBy.Should().Be(modifier);
        persisted.LastModified.Should().NotBeNull();
    }
}
