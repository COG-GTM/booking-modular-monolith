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

public class InvalidateCachingBehaviorTests
{
    private readonly IEasyCachingProvider _cachingProvider = Substitute.For<IEasyCachingProvider>();
    private readonly IEasyCachingProviderFactory _cachingFactory = Substitute.For<IEasyCachingProviderFactory>();

    public InvalidateCachingBehaviorTests()
    {
        _cachingFactory.GetCachingProvider("mem").Returns(_cachingProvider);
    }

    private InvalidateCachingBehavior<TRequest, string> CreateSut<TRequest>()
        where TRequest : notnull, IRequest<string>
    {
        return new InvalidateCachingBehavior<TRequest, string>(
            _cachingFactory,
            Substitute.For<ILogger<InvalidateCachingBehavior<TRequest, string>>>()
        );
    }

    [Fact]
    public async Task non_invalidating_request_should_pass_through_without_removing_anything()
    {
        var response = await CreateSut<FakeCommand>()
            .Handle(new FakeCommand("plain"), TransactionBehaviorFixture.Handler("ok"), CancellationToken.None);

        response.Should().Be("ok");
        await _cachingProvider.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task invalidating_request_should_run_handler_first_then_remove_cache_key()
    {
        var request = new FakeInvalidateCacheCommand("stale");
        var order = new List<string>();
        _cachingProvider
            .RemoveAsync(request.CacheKey, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("remove");
                return Task.CompletedTask;
            });

        var response = await CreateSut<FakeInvalidateCacheCommand>()
            .Handle(
                request,
                TransactionBehaviorFixture.Handler("ok", () => order.Add("handler")),
                CancellationToken.None
            );

        response.Should().Be("ok");
        order.Should().Equal("handler", "remove");
        await _cachingProvider.Received(1).RemoveAsync(request.CacheKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task handler_failure_should_propagate_and_keep_cache_entry()
    {
        var request = new FakeInvalidateCacheCommand("stale");

        var act = () =>
            CreateSut<FakeInvalidateCacheCommand>()
                .Handle(
                    request,
                    TransactionBehaviorFixture.FailingHandler<string>(new InvalidOperationException("handler failed")),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _cachingProvider.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }
}
