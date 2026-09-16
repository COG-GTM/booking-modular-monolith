using Mapster;
using MapsterMapper;
using Passenger;

namespace Unit.Test.Common;

public static class MapperFactory
{
    public static IMapper Create()
    {
        var typeAdapterConfig = TypeAdapterConfig.GlobalSettings;
        typeAdapterConfig.Scan(typeof(PassengerRoot).Assembly);
        IMapper instance = new Mapper(typeAdapterConfig);

        return instance;
    }
}
