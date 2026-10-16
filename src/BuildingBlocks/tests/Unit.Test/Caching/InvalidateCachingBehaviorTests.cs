using BuildingBlocks.Caching;
using EasyCaching.Core;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.CachingTests;

public class InvalidateCachingBehaviorTests
{
    public sealed record InvalidatePingRequest(string CacheKey) : IRequest<string>, IInvalidateCacheRequest;

    public sealed record PlainInvalidateRequest() : IRequest<string>;

    private static (
        InvalidateCachingBehavior<TRequest, string> Behavior,
        IEasyCachingProvider Provider
    ) Create<TRequest>()
        where TRequest : notnull, IRequest<string>
    {
        var provider = Substitute.For<IEasyCachingProvider>();
        var factory = Substitute.For<IEasyCachingProviderFactory>();
        factory.GetCachingProvider("mem").Returns(provider);
        var behavior = new InvalidateCachingBehavior<TRequest, string>(
            factory,
            NullLogger<InvalidateCachingBehavior<TRequest, string>>.Instance
        );
        return (behavior, provider);
    }

    [Fact]
    public async Task invalidate_request_runs_next_then_removes_the_key()
    {
        var (sut, provider) = Create<InvalidatePingRequest>();
        var nextCalls = 0;

        var result = await sut.Handle(
            new InvalidatePingRequest("stale"),
            _ =>
            {
                nextCalls++;
                return Task.FromResult("done");
            },
            CancellationToken.None
        );

        result.Should().Be("done");
        nextCalls.Should().Be(1);
        await provider.Received(1).RemoveAsync("stale");
        await provider.Received().RemoveAsync(Arg.Is<string>(key => nextCalls == 1 && key == "stale"));
    }

    [Fact]
    public async Task non_invalidate_request_never_removes_a_key()
    {
        var (sut, provider) = Create<PlainInvalidateRequest>();

        await sut.Handle(new PlainInvalidateRequest(), _ => Task.FromResult("done"), CancellationToken.None);

        await provider.DidNotReceive().RemoveAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task a_throwing_next_delegate_skips_the_removal()
    {
        var (sut, provider) = Create<InvalidatePingRequest>();

        var act = () =>
            sut.Handle(
                new InvalidatePingRequest("stale"),
                _ => Task.FromException<string>(new InvalidOperationException("handler failed")),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        await provider.DidNotReceive().RemoveAsync(Arg.Any<string>());
    }
}
