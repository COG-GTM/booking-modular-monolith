using BuildingBlocks.Caching;
using EasyCaching.Core;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Caching;

public class CachingBehaviorTests
{
    private readonly IEasyCachingProvider _cachingProvider = Substitute.For<IEasyCachingProvider>();
    private readonly IEasyCachingProviderFactory _cachingFactory = Substitute.For<IEasyCachingProviderFactory>();

    public CachingBehaviorTests()
    {
        _cachingFactory.GetCachingProvider("mem").Returns(_cachingProvider);
    }

    private CachingBehavior<TRequest, string> CreateSut<TRequest>()
        where TRequest : notnull, IRequest<string>
    {
        return new CachingBehavior<TRequest, string>(
            _cachingFactory,
            Substitute.For<ILogger<CachingBehavior<TRequest, string>>>()
        );
    }

    [Fact]
    public async Task non_cacheable_request_should_pass_through_without_touching_cache()
    {
        var response = await CreateSut<FakeQuery>()
            .Handle(new FakeQuery("plain"), TransactionBehaviorFixture.Handler("fresh"), CancellationToken.None);

        response.Should().Be("fresh");
        await _cachingProvider.DidNotReceiveWithAnyArgs().GetAsync<string>(default!, default);
        await _cachingProvider.DidNotReceiveWithAnyArgs().SetAsync(default!, default(string)!, default, default);
    }

    [Fact]
    public async Task cache_hit_should_return_cached_value_and_skip_handler()
    {
        var request = new FakeCacheableQuery("hit");
        _cachingProvider
            .GetAsync<string>(request.CacheKey, Arg.Any<CancellationToken>())
            .Returns(new CacheValue<string>("cached", true));
        var handlerInvoked = false;

        var response = await CreateSut<FakeCacheableQuery>()
            .Handle(
                request,
                TransactionBehaviorFixture.Handler("fresh", () => handlerInvoked = true),
                CancellationToken.None
            );

        response.Should().Be("cached");
        handlerInvoked.Should().BeFalse();
        await _cachingProvider.DidNotReceiveWithAnyArgs().SetAsync(default!, default(string)!, default, default);
    }

    [Fact]
    public async Task cache_miss_should_invoke_handler_and_store_response_under_cache_key_with_requested_expiration()
    {
        var expiration = new DateTime(2000, 1, 1, 2, 30, 0);
        var request = new FakeCacheableQuery("miss", expiration);
        _cachingProvider
            .GetAsync<string>(request.CacheKey, Arg.Any<CancellationToken>())
            .Returns(CacheValue<string>.NoValue);

        var response = await CreateSut<FakeCacheableQuery>()
            .Handle(request, TransactionBehaviorFixture.Handler("fresh"), CancellationToken.None);

        response.Should().Be("fresh");
        await _cachingProvider
            .Received(1)
            .SetAsync(request.CacheKey, "fresh", expiration.TimeOfDay, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task cache_miss_without_explicit_expiration_should_default_to_roughly_one_hour_from_now()
    {
        var request = new FakeCacheableQuery("default-expiration");
        _cachingProvider
            .GetAsync<string>(request.CacheKey, Arg.Any<CancellationToken>())
            .Returns(CacheValue<string>.NoValue);
        var expected = DateTime.Now.AddHours(1).TimeOfDay;

        await CreateSut<FakeCacheableQuery>()
            .Handle(request, TransactionBehaviorFixture.Handler("fresh"), CancellationToken.None);

        await _cachingProvider
            .Received(1)
            .SetAsync(
                request.CacheKey,
                "fresh",
                Arg.Is<TimeSpan>(t =>
                    (t - expected).Duration() < TimeSpan.FromMinutes(1)
                    || (t - expected).Duration() > TimeSpan.FromHours(23)
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
