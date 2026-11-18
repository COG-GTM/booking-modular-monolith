using System.Security.Claims;
using BuildingBlocks.Core.Model;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.Web;
using FluentAssertions;
using Flight.Data;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Xunit;

namespace Unit.Test.Audit;

using global::Flight.Aircrafts.ValueObjects;
using AircraftModel = global::Flight.Aircrafts.Models.Aircraft;
using Name = global::Flight.Aircrafts.ValueObjects.Name;

public class AuditUserAttributionTests
{
    private static HttpContextAccessor AccessorFor(params Claim[] claims) =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = claims.Length == 0
                    ? new ClaimsPrincipal(new ClaimsIdentity())
                    : new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
            }
        };

    // JwtBearer runs with MapInboundClaims = false, so a validated token carries its subject as "sub".
    private static HttpContextAccessor SignedInAs(Guid userId) =>
        AccessorFor(new Claim("sub", userId.ToString()), new Claim("name", "mallory"));

    private static FlightDbContext CreateDbContext(IHttpContextAccessor accessor, string? databaseName = null) =>
        new(new DbContextOptionsBuilder<FlightDbContext>()
                .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString()).Options,
            new CurrentUserProvider(accessor), null);

    private static AircraftModel NewAircraft() =>
        AircraftModel.Create(AircraftId.Of(NewId.NextGuid()), Name.Of("Boeing 737"), Model.Of("B737"),
            ManufacturingYear.Of(2005));

    [Fact]
    public void current_user_id_should_be_the_guid_from_the_sub_claim()
    {
        var userId = Guid.NewGuid();

        new CurrentUserProvider(SignedInAs(userId)).GetCurrentUserId().Should().Be(userId);
    }

    [Fact]
    public void current_user_id_should_be_null_without_a_signed_in_user()
    {
        new CurrentUserProvider(AccessorFor()).GetCurrentUserId().Should().BeNull();
        new CurrentUserProvider(new HttpContextAccessor()).GetCurrentUserId().Should().BeNull();
    }

    [Fact]
    public void current_user_id_should_be_null_when_subject_is_not_a_guid()
    {
        new CurrentUserProvider(AccessorFor(new Claim("sub", "0"))).GetCurrentUserId().Should().BeNull();
    }

    [Fact]
    public async Task created_by_and_last_modified_by_should_record_the_signed_in_user()
    {
        var creator = Guid.NewGuid();
        var aircraft = NewAircraft();
        var databaseName = Guid.NewGuid().ToString();

        await using (var context = CreateDbContext(SignedInAs(creator), databaseName))
        {
            await context.Aircraft.AddAsync(aircraft);
            await context.SaveChangesAsync();
        }

        var modifier = Guid.NewGuid();

        await using (var context = CreateDbContext(SignedInAs(modifier), databaseName))
        {
            context.Attach(aircraft);
            context.Entry(aircraft).State = EntityState.Modified;
            await context.SaveChangesAsync();
        }

        aircraft.CreatedBy.Should().Be(creator);
        aircraft.LastModifiedBy.Should().Be(modifier);
    }

    [Fact]
    public async Task created_by_should_be_null_for_anonymous_changes()
    {
        var aircraft = NewAircraft();

        await using var context = CreateDbContext(AccessorFor());
        await context.Aircraft.AddAsync(aircraft);
        await context.SaveChangesAsync();

        aircraft.CreatedBy.Should().BeNull();
    }

    [Fact]
    public void legacy_events_with_numeric_audit_user_id_should_deserialize_as_null()
    {
        var userId = Guid.NewGuid();
        var converter = new LegacyAuditUserIdConverter();

        JsonConvert.DeserializeObject<AuditedEvent>("{\"Id\":1,\"CreatedBy\":0,\"LastModifiedBy\":null}", converter)!
            .CreatedBy.Should().BeNull();
        JsonConvert.DeserializeObject<AuditedEvent>(JsonConvert.SerializeObject(new AuditedEvent { CreatedBy = userId }), converter)!
            .CreatedBy.Should().Be(userId);
    }

    private record AuditedEvent : Entity<long>;
}
