using BuildingBlocks.Caching;
using EasyCaching.Core;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Unit.Test.Common;

public class InvalidateCachingBehaviorTests
{
    [Fact]
    public async Task should_invalidate_by_prefix_after_handler_and_return_inner_response()
    {
        var provider = Substitute.For<IEasyCachingProvider>();
        var factory = Substitute.For<IEasyCachingProviderFactory>();
        factory.GetCachingProvider("mem").Returns(provider);
        var behavior = new InvalidateCachingBehavior<InvalidateRequest, object>(
            factory,
            NullLogger<InvalidateCachingBehavior<InvalidateRequest, object>>.Instance
        );
        var request = new InvalidateRequest("GetAvailableFlights");
        var innerResponse = new object();
        var handlerCompleted = false;
        provider
            .RemoveByPrefixAsync(request.CacheKey, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                handlerCompleted.Should().BeTrue();
                return Task.CompletedTask;
            });

        var response = await behavior.Handle(
            request,
            async _ =>
            {
                await Task.Yield();
                handlerCompleted = true;
                return innerResponse;
            },
            CancellationToken.None
        );

        response.Should().BeSameAs(innerResponse);
        await provider.Received(1).RemoveByPrefixAsync(request.CacheKey, Arg.Any<CancellationToken>());
        await provider.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private sealed record InvalidateRequest(string CacheKey) : IRequest<object>, IInvalidateCacheRequest;
}
