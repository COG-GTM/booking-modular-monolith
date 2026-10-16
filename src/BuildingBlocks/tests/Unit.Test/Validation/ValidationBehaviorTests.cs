using BuildingBlocks.Validation;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Unit.Test.ValidationTests;

public class ValidationBehaviorTests
{
    public sealed record ValidationPingRequest(string Name) : IRequest<string>;

    private sealed class ValidationPingRequestValidator : AbstractValidator<ValidationPingRequest>
    {
        public ValidationPingRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name must not be empty");
        }
    }

    private static ValidationBehavior<ValidationPingRequest, string> CreateBehavior(IServiceProvider provider) =>
        new(provider);

    [Fact]
    public async Task without_a_registered_validator_the_next_delegate_runs()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var sut = CreateBehavior(provider);
        var nextCalls = 0;

        var result = await sut.Handle(
            new ValidationPingRequest("x"),
            _ =>
            {
                nextCalls++;
                return Task.FromResult("pong");
            },
            CancellationToken.None
        );

        result.Should().Be("pong");
        nextCalls.Should().Be(1);
    }

    [Fact]
    public async Task valid_request_reaches_the_next_delegate()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IValidator<ValidationPingRequest>>(new ValidationPingRequestValidator())
            .BuildServiceProvider();
        var sut = CreateBehavior(provider);
        var nextCalls = 0;

        var result = await sut.Handle(
            new ValidationPingRequest("alice"),
            _ =>
            {
                nextCalls++;
                return Task.FromResult("pong");
            },
            CancellationToken.None
        );

        result.Should().Be("pong");
        nextCalls.Should().Be(1);
    }

    [Fact]
    public async Task invalid_request_throws_the_first_failure_message_and_skips_next()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IValidator<ValidationPingRequest>>(new ValidationPingRequestValidator())
            .BuildServiceProvider();
        var sut = CreateBehavior(provider);
        var nextCalls = 0;

        var act = () =>
            sut.Handle(
                new ValidationPingRequest(string.Empty),
                _ =>
                {
                    nextCalls++;
                    return Task.FromResult("pong");
                },
                CancellationToken.None
            );

        var ex = await act.Should().ThrowAsync<BuildingBlocks.Exception.ValidationException>();
        ex.Which.Message.Should().Be("Name must not be empty");
        ex.Which.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        nextCalls.Should().Be(0);
    }
}
