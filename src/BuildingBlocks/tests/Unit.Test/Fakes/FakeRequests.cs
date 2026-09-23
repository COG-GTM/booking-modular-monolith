using BuildingBlocks.Caching;
using BuildingBlocks.Core.CQRS;
using MediatR;

namespace Unit.Test.Fakes;

public record FakePlainRequest(string Name) : IRequest<string>;

public record FakeCommand(string Name) : ICommand<string>;

public record FakeQuery(string Name) : IQuery<string>;

public record FakeCacheableQuery(string Name, DateTime? AbsoluteExpirationRelativeToNow = null)
    : IQuery<string>,
        ICacheRequest
{
    public string CacheKey => $"fake-cache-{Name}";
}

public record FakeInvalidateCacheCommand(string Name) : ICommand<string>, IInvalidateCacheRequest
{
    public string CacheKey => $"fake-cache-{Name}";
}
