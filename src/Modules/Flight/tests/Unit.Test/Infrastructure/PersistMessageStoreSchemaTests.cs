using System.Text.RegularExpressions;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight;

// The persist_message table is created by hand-written DDL when the module database already holds the module's
// own tables (EnsureCreated is then a no-op), so that DDL has to match what EF expects to read and write.
public class PersistMessageStoreSchemaTests
{
    private static string GenerateEfCreateScript()
    {
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<FlightRoot>>()
            .UseNpgsql("Host=localhost;Database=flight_schema_test")
            .UseSnakeCaseNamingConvention()
            .Options;

        using var context = new PersistMessageDbContext<FlightRoot>(options);

        return context.Database.GenerateCreateScript();
    }

    private static Dictionary<string, string> ParseColumnTypes(string createTableSql)
    {
        var body = createTableSql[(createTableSql.IndexOf('(', StringComparison.Ordinal) + 1)..];

        return Regex
            .Matches(
                body,
                @"^\s*""?(?<name>[a-z_]+)""?\s+(?<type>[a-z]+(?: with(?:out)? time zone)?)",
                RegexOptions.Multiline
            )
            .Where(match => match.Groups["name"].Value != "constraint")
            .ToDictionary(match => match.Groups["name"].Value, match => NormalizeType(match.Groups["type"].Value));
    }

    // Npgsql names the same store type by its SQL-standard or PostgreSQL alias, and (depending on the
    // Npgsql.EnableLegacyTimestampBehavior switch the persistence extensions turn on) maps DateTime to
    // either timestamp flavour, both of which it reads and writes transparently.
    private static string NormalizeType(string storeType) =>
        storeType switch
        {
            "timestamptz" or "timestamp with time zone" or "timestamp without time zone" => "timestamp",
            "int" or "int4" => "integer",
            "int8" => "bigint",
            _ => storeType,
        };

    [Fact]
    public void raw_ddl_should_use_the_same_column_types_as_the_ef_model()
    {
        var efColumns = ParseColumnTypes(GenerateEfCreateScript());
        var ddlColumns = ParseColumnTypes(PersistMessageDbContext<FlightRoot>.CreateTableSql);

        efColumns.Should().NotBeEmpty();
        ddlColumns.Should().BeEquivalentTo(efColumns);
    }

    [Fact]
    public void enum_columns_should_be_stored_as_integers()
    {
        var ddlColumns = ParseColumnTypes(PersistMessageDbContext<FlightRoot>.CreateTableSql);

        ddlColumns["message_status"].Should().Be("integer");
        ddlColumns["delivery_type"].Should().Be("integer");
    }
}
