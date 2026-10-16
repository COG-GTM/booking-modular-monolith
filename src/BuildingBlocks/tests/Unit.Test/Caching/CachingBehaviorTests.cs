using BuildingBlocks.Caching;
using EasyCaching.Core;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.CachingTests;

public class CachingBehaviorTests
{
    public sealed record CachedPingRequest(string CacheKey) : IRequest<string>, ICacheRequest
    {
        public DateTime? AbsoluteExpirationRelativeToNow => null;
    }

    public sealed record PlainPingRequest() : IRequest<string>;

    private sealed class CacheStub
    {
        public static (
            CachingBehavior<TRequest, string> Behavior,
            IEasyCachingProviderFactory Factory,
            IEasyCachingProvider Provider
        ) Create<TRequest>()
            where TRequest : notnull, IRequest<string>
        {
            var provider = Substitute.For<IEasyCachingProvider>();
            var factory = Substitute.For<IEasyCachingProviderFactory>();
            factory.GetCachingProvider("mem").Returns(provider);
            var behavior = new CachingBehavior<TRequest, string>(
                factory,
                NullLogger<CachingBehavior<TRequest, string>>.Instance
            );
            return (behavior, factory, provider);
        }
    }

    [Fact]
    public async Task non_cache_request_passes_through_without_touching_the_provider()
    {
        var (sut, _, provider) = CacheStub.Create<PlainPingRequest>();

        var result = await sut.Handle(new PlainPingRequest(), _ => Task.FromResult("pong"), CancellationToken.None);

        result.Should().Be("pong");
        await provider.DidNotReceive().GetAsync<string>(Arg.Any<string>());
        await provider.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task cache_hit_returns_the_cached_value_and_skips_next()
    {
        var (sut, factory, provider) = CacheStub.Create<CachedPingRequest>();
        provider.GetAsync<string>("k1").Returns(new CacheValue<string>("cached-pong", hasValue: true));
        var nextCalls = 0;

        var result = await sut.Handle(
            new CachedPingRequest("k1"),
            _ =>
            {
                nextCalls++;
                return Task.FromResult("fresh");
            },
            CancellationToken.None
        );

        result.Should().Be("cached-pong");
        nextCalls.Should().Be(0);
        factory.Received(1).GetCachingProvider("mem");
        await provider.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task cache_miss_runs_next_and_stores_the_response_under_the_cache_key()
    {
        var (sut, _, provider) = CacheStub.Create<CachedPingRequest>();
        provider.GetAsync<string>("k2").Returns(new CacheValue<string>(null, hasValue: false));
        var nextCalls = 0;

        var result = await sut.Handle(
            new CachedPingRequest("k2"),
            _ =>
            {
                nextCalls++;
                return Task.FromResult("fresh");
            },
            CancellationToken.None
        );

        result.Should().Be("fresh");
        nextCalls.Should().Be(1);
        await provider.Received(1).SetAsync("k2", "fresh", Arg.Any<TimeSpan>());
    }
}
