using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace BuildingBlocks.Tests;

public class PersistMessageModelTests
{
    [Fact]
    public void postgres_model_maps_persist_message_columns_and_enum_conversions()
    {
        using var context = CreateContext<First>();
        var entity = context.Model.FindEntityType(typeof(PersistMessage))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var status = entity.FindProperty(nameof(PersistMessage.MessageStatus))!;
        var deliveryType = entity.FindProperty(nameof(PersistMessage.DeliveryType))!;
        var created = entity.FindProperty(nameof(PersistMessage.Created))!;

        entity.GetTableName().Should().Be("persist_message");
        status.GetColumnName(table).Should().Be("message_status");
        status.GetRelationalTypeMapping().Converter!.ProviderClrType.Should().Be(typeof(string));
        status.GetColumnType().Should().Be("text");
        deliveryType.GetColumnName(table).Should().Be("delivery_type");
        deliveryType.GetRelationalTypeMapping().Converter!.ProviderClrType.Should().Be(typeof(string));
        deliveryType.GetColumnType().Should().Be("text");
        created.GetColumnName(table).Should().Be("created");
        created.GetColumnType().Should().Be("timestamp with time zone");
    }

    [Fact]
    public void module_context_types_each_map_their_own_persist_message_table()
    {
        using var first = CreateContext<First>();
        using var second = CreateContext<Second>();

        typeof(PersistMessageDbContext<First>).Should().NotBe(typeof(PersistMessageDbContext<Second>));
        first.Model.FindEntityType(typeof(PersistMessage))!.GetTableName().Should().Be("persist_message");
        second.Model.FindEntityType(typeof(PersistMessage))!.GetTableName().Should().Be("persist_message");
    }

    private static PersistMessageDbContext<TModule> CreateContext<TModule>() =>
        new(
            new DbContextOptionsBuilder<PersistMessageDbContext<TModule>>()
                .UseNpgsql("Host=localhost;Database=x;Username=u;Password=p")
                .UseSnakeCaseNamingConvention()
                .Options
        );

    public sealed class First;

    public sealed class Second;
}
