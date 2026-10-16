using BuildingBlocks.Mapster;
using FluentAssertions;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Unit.Test.MapsterTests;

[CollectionDefinition("Mapster static state", DisableParallelization = true)]
public class MapsterStaticStateCollection;

[Collection("Mapster static state")]
public class CustomMapsterTests
{
    public sealed class MapsterSource
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    public sealed class MapsterDest
    {
        public string FullName { get; set; } = string.Empty;
    }

    public sealed class MapsterTestRegister : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            config.NewConfig<MapsterSource, MapsterDest>().Map(d => d.FullName, s => s.FirstName + " " + s.LastName);
        }
    }

    [Fact]
    public void add_custom_mapster_registers_a_singleton_mapper_applying_iregister_configs()
    {
        var services = new ServiceCollection();

        services.AddCustomMapster(typeof(MapsterTestRegister).Assembly);

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        mapper.Should().BeSameAs(provider.GetRequiredService<IMapper>());

        var dest = mapper.Map<MapsterDest>(new MapsterSource { FirstName = "Ada", LastName = "Lovelace" });
        dest.FullName.Should().Be("Ada Lovelace");
    }
}
