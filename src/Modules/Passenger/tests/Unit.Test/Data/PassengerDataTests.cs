using BuildingBlocks.Mongo;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using Passenger.Data;
using Xunit;

namespace Unit.Test.Data;

public class PassengerDataTests
{
    [Fact]
    public void Passenger_ef_model_defines_postgres_schema_and_value_conversions()
    {
        var options = new DbContextOptionsBuilder<PassengerDbContext>()
            .UseNpgsql("Host=localhost")
            .UseSnakeCaseNamingConvention()
            .Options;
        using var db = new PassengerDbContext(options, null, null);
        var entity = db.Model.FindEntityType(typeof(global::Passenger.Passengers.Models.Passenger))!;
        var table = StoreObjectIdentifier.Table("passenger", null);
        var name = entity
            .FindNavigation(nameof(global::Passenger.Passengers.Models.Passenger.Name))!
            .TargetEntityType.FindProperty("Value")!;
        var passport = entity
            .FindNavigation(nameof(global::Passenger.Passengers.Models.Passenger.PassportNumber))!
            .TargetEntityType.FindProperty("Value")!;
        var passengerType = entity.FindProperty("PassengerType")!;
        var id = entity.FindProperty("Id")!;

        entity.GetTableName().Should().Be("passenger");
        name.GetMaxLength().Should().Be(50);
        name.IsNullable.Should().BeFalse();
        passport.GetMaxLength().Should().Be(10);
        passport.IsNullable.Should().BeFalse();
        name.GetColumnName(table).Should().Be("name");
        passport.GetColumnName(table).Should().Be("passport_number");
        passengerType.GetColumnName(table).Should().Be("passenger_type");
        passengerType.GetDefaultValue().Should().Be(global::Passenger.Passengers.Enums.PassengerType.Unknown);
        passengerType.GetValueConverter()!.ProviderClrType.Should().Be(typeof(string));
        passengerType
            .GetValueConverter()!
            .ConvertToProvider(global::Passenger.Passengers.Enums.PassengerType.Female)
            .Should()
            .Be("Female");
        passengerType
            .GetValueConverter()!
            .ConvertFromProvider("Male")
            .Should()
            .Be(global::Passenger.Passengers.Enums.PassengerType.Male);
        entity.FindProperty("Version")!.IsConcurrencyToken.Should().BeTrue();
        id.ValueGenerated.Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
        id.GetValueConverter()!.ProviderClrType.Should().Be(typeof(Guid));
        entity.GetQueryFilter()!.ToString().Should().Contain("IsDeleted");
    }

    [Fact]
    public void Design_time_factory_creates_a_postgres_backed_context()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        db.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
    }

    [Fact]
    public void Passenger_read_context_uses_the_expected_mongo_database_and_collection()
    {
        var options = Options.Create(
            new MongoOptions { ConnectionString = "mongodb://localhost:27017", DatabaseName = "x" }
        );
        using var db = new PassengerReadDbContext(options);

        db.Database.DatabaseNamespace.DatabaseName.Should().Be("x");
        db.Passenger.CollectionNamespace.CollectionName.Should().Be("passenger");
    }
}
