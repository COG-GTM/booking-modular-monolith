using Mapster;
using MapsterMapper;
using Payments.FPS;

namespace Unit.Test.Common;

public static class MapperFactory
{
    public static IMapper Create()
    {
        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(typeof(PaymentsFpsRoot).Assembly);
        return new Mapper(config);
    }
}
