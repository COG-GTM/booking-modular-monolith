using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Integration.Test.Passenger.Data;

using PassengerModule = global::Passenger;

public class PassengerUserIdModelTests
{
    [Fact]
    public void Passenger_user_id_maps_to_required_indexed_column_and_migration()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(PassengerModule.Passengers.Models.Passenger))!;
        var property = entityType.FindProperty(nameof(PassengerModule.Passengers.Models.Passenger.UserId))!;
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());

        property.GetColumnName(table).Should().Be("user_id");
        property.ClrType.Should().Be(typeof(Guid));
        property.IsNullable.Should().BeFalse();
        entityType
            .GetIndexes()
            .Should()
            .ContainSingle(index =>
                index.GetDatabaseName() == "ix_passenger_user_id" && index.Properties.Contains(property)
            );
        context.Database.GetMigrations().Should().Contain("20261029154023_AddPassengerUserId");

        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        if (snapshot is IMutableModel mutableSnapshot)
        {
            snapshot = mutableSnapshot.FinalizeModel();
        }

        snapshot = context
            .GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot, designTime: true, validationLogger: null);
        var differences = context
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                snapshot.GetRelationalModel(),
                context.GetService<IDesignTimeModel>().Model.GetRelationalModel()
            );

        differences
            .Where(operation =>
                operation
                    is ColumnOperation { Name: "user_id" }
                        or CreateIndexOperation { Name: "ix_passenger_user_id" }
                        or DropIndexOperation { Name: "ix_passenger_user_id" }
                        or RenameIndexOperation { Name: "ix_passenger_user_id" }
                        or RenameIndexOperation { NewName: "ix_passenger_user_id" }
            )
            .Should()
            .BeEmpty();
    }

    private static PassengerModule.Data.PassengerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PassengerModule.Data.PassengerDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=passenger_model_test",
                postgresOptions =>
                    postgresOptions.MigrationsAssembly(
                        typeof(PassengerModule.Data.PassengerDbContext).Assembly.GetName().Name
                    )
            )
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new PassengerModule.Data.PassengerDbContext(options);
    }
}
