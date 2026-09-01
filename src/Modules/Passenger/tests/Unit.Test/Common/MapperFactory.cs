using Mapster;
using MapsterMapper;
using Passenger;

namespace Unit.Test.Common
{
    public static class MapperFactory
    {
        public static IMapper Create()
        {
            var config = TypeAdapterConfig.GlobalSettings;
            config.Scan(typeof(PassengerRoot).Assembly);
            return new Mapper(config);
        }
    }
}
