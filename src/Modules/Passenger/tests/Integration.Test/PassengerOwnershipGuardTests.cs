using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Passenger.Data;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace Integration.Test;

public class PassengerOwnershipGuardTests
{
    private static readonly Guid OwnerId = Guid.Parse("a39c4a51-2079-4d84-b74e-e2349d251cf0");

    [Fact]
    public async Task should_return_not_found_for_empty_passenger_id()
    {
        await using var db = CreateContext();
        await Assert.ThrowsAsync<PassengerNotFoundException>(
            () => Check(Guid.Empty, CreateUser(OwnerId.ToString()), db)
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("d66f03ad-30d2-47e0-919a-627461a6e6fb")]
    public async Task should_deny_non_owner_of_owned_passenger(string? subject)
    {
        await using var db = CreateContext();
        var id = await SeedPassenger(db, OwnerId);
        await Assert.ThrowsAsync<PassengerNotFoundException>(() => Check(id, CreateUser(subject), db));
    }

    [Theory]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public async Task should_allow_owner_with_identity_claim(string claimType)
    {
        await using var db = CreateContext();
        var id = await SeedPassenger(db, OwnerId);
        await Check(id, CreateUser(OwnerId.ToString(), claimType), db);
    }

    [Fact]
    public async Task should_allow_legacy_passenger_and_log_warning()
    {
        await using var db = CreateContext();
        var id = await SeedPassenger(db, null);
        var logger = Substitute.For<ILogger>();
        await GetPassengerByIdEndpoint.EnsureOwnershipAsync(
            id,
            CreateUser(Guid.NewGuid().ToString()),
            db,
            logger,
            CancellationToken.None
        );
        Assert.Contains(
            logger.ReceivedCalls(),
            call => call.GetMethodInfo().Name == "Log" && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_deny_missing_or_deleted_write_record(bool deleted)
    {
        await using var db = CreateContext();
        var id = deleted ? await SeedPassenger(db, OwnerId, true) : Guid.NewGuid();
        await Assert.ThrowsAsync<PassengerNotFoundException>(() => Check(id, CreateUser(OwnerId.ToString()), db));
    }

    [Fact]
    public async Task should_prefer_subject_over_name_identifier()
    {
        await using var db = CreateContext();
        var id = await SeedPassenger(db, OwnerId);
        var user = CreateUser(Guid.NewGuid().ToString());
        ((ClaimsIdentity)user.Identity!).AddClaim(new Claim(ClaimTypes.NameIdentifier, OwnerId.ToString()));
        await Assert.ThrowsAsync<PassengerNotFoundException>(() => Check(id, user, db));
    }

    private static Task Check(Guid id, ClaimsPrincipal user, PassengerDbContext db) =>
        GetPassengerByIdEndpoint.EnsureOwnershipAsync(id, user, db, Substitute.For<ILogger>(), CancellationToken.None);

    private static ClaimsPrincipal CreateUser(string? subject, string claimType = "sub") =>
        new(
            new ClaimsIdentity(subject is null ? Array.Empty<Claim>() : new[] { new Claim(claimType, subject) }, "Test")
        );

    private static PassengerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PassengerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Guid> SeedPassenger(PassengerDbContext db, Guid? userId, bool deleted = false)
    {
        var id = Guid.NewGuid();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(id),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            deleted,
            userId
        );
        db.Passengers.Add(passenger);
        await db.SaveChangesAsync();
        return id;
    }
}
