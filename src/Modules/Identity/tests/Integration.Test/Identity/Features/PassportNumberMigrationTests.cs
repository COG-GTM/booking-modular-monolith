using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using global::Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Integration.Test.Identity.Features;

public class PassportNumberMigrationTests
{
    private const string PreviousMigration = "20230331193410_initial";
    private const string TargetMigration = "20261118145156_AddUniquePassportNumberIndex";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_normalize_existing_passports_and_reject_normalized_duplicates(bool collide)
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:latest").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<IdentityContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var context = new IdentityContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await InsertPassportAsync(context, firstId, "\t ab c\n ");
        await InsertPassportAsync(context, secondId, collide ? " AB C\t\n" : "\n de f \t");

        if (collide)
        {
            var act = () => migrator.MigrateAsync(TargetMigration);
            var exception = await act.Should().ThrowAsync<PostgresException>();
            exception.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
            exception.Which.ConstraintName.Should().Be("PassportNumberIndex");
            (await context.Database.GetAppliedMigrationsAsync()).Should().NotContain(TargetMigration);
            return;
        }

        var emptyId = Guid.NewGuid();
        await InsertPassportAsync(context, emptyId, "");
        await migrator.MigrateAsync(TargetMigration);

        var passports = await context
            .Database.SqlQueryRaw<string>("SELECT pass_port_number AS \"Value\" FROM asp_net_users")
            .ToListAsync();
        passports.Should().BeEquivalentTo("AB C", "DE F", "");
        (await context.Database.GetAppliedMigrationsAsync()).Should().Contain(TargetMigration);
    }

    private static Task<int> InsertPassportAsync(IdentityContext context, Guid id, string passportNumber)
    {
        return context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO asp_net_users
                (id, pass_port_number, version, email_confirmed, phone_number_confirmed,
                 two_factor_enabled, lockout_enabled, access_failed_count)
            VALUES ({id}, {passportNumber}, 0, false, false, false, false, 0)
            """
        );
    }
}
