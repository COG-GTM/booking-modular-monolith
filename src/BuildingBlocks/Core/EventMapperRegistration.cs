namespace BuildingBlocks.Core;

public interface IEventMapperRegistration
{
    IEventMapper Mapper { get; }
}

internal sealed class EventMapperRegistration<TMapper> : IEventMapperRegistration
    where TMapper : class, IEventMapper
{
    public EventMapperRegistration(TMapper mapper)
    {
        Mapper = mapper;
    }

    public IEventMapper Mapper { get; }
}
