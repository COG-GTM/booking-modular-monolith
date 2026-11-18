using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Data;

public class AddPassengerUserIdAndUniquePassportNumberMigrationTests : PassengerIntegrationTestBase
{
    private const string InitialMigration = "20230611213031_initial";

    public AddPassengerUserIdAndUniquePassportNumberMigrationTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_backfill_legacy_passengers_with_empty_user_id_and_add_non_unique_user_id_index()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PassengerDbContext>();
        var databaseName = $"passenger_migration_{Guid.NewGuid():N}";
        var adminConnectionString = dbContext.Database.GetConnectionString()!;
        var connectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;

        await ExecuteAsync(adminConnectionString, $"CREATE DATABASE {databaseName}");

        try
        {
            dbContext.Database.SetConnectionString(connectionString);
            var migrator = dbContext.GetService<IMigrator>();
            await migrator.MigrateAsync(InitialMigration);

            await InsertLegacyPassengerAsync(connectionString, "Legacy One", "AB12345");
            await InsertLegacyPassengerAsync(connectionString, "Legacy Two", "CD7654321");

            await migrator.MigrateAsync();

            var userIds = await QueryAsync(connectionString, "SELECT user_id::text FROM passenger ORDER BY name");
            userIds.Should().Equal(Guid.Empty.ToString(), Guid.Empty.ToString());

            var nullability = await QueryAsync(
                connectionString,
                "SELECT is_nullable FROM information_schema.columns WHERE table_name = 'passenger' AND column_name = 'user_id'"
            );
            nullability.Should().ContainSingle().Which.Should().Be("NO");

            var userIdIndex = await QueryAsync(
                connectionString,
                "SELECT indexdef FROM pg_indexes WHERE tablename = 'passenger' AND indexname = 'ix_passenger_user_id'"
            );
            userIdIndex.Should().ContainSingle().Which.Should().NotContain("UNIQUE").And.Contain("(user_id)");
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(adminConnectionString, $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)");
        }
    }

    private static Task InsertLegacyPassengerAsync(string connectionString, string name, string passportNumber)
    {
        return ExecuteAsync(
            connectionString,
            "INSERT INTO passenger (id, passport_number, name, is_deleted, version) "
                + "VALUES (@id, @passport, @name, false, 0)",
            new NpgsqlParameter("id", Guid.CreateVersion7()),
            new NpgsqlParameter("passport", passportNumber),
            new NpgsqlParameter("name", name)
        );
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> QueryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
