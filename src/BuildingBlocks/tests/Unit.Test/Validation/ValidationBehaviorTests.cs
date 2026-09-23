using BuildingBlocks.Validation;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Validation;

public class ValidationBehaviorTests
{
    private sealed class FakeCommandValidator : AbstractValidator<FakeCommand>
    {
        public FakeCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
        }
    }

    private static ValidationBehavior<FakeCommand, string> CreateSut(bool registerValidator)
    {
        var services = new ServiceCollection();
        if (registerValidator)
            services.AddSingleton<IValidator<FakeCommand>, FakeCommandValidator>();

        return new ValidationBehavior<FakeCommand, string>(services.BuildServiceProvider());
    }

    [Fact]
    public async Task without_registered_validator_should_invoke_handler()
    {
        var response = await CreateSut(registerValidator: false)
            .Handle(new FakeCommand(string.Empty), TransactionBehaviorFixture.Handler("ok"), CancellationToken.None);

        response.Should().Be("ok");
    }

    [Fact]
    public async Task valid_request_should_invoke_handler()
    {
        var response = await CreateSut(registerValidator: true)
            .Handle(new FakeCommand("valid"), TransactionBehaviorFixture.Handler("ok"), CancellationToken.None);

        response.Should().Be("ok");
    }

    [Fact]
    public async Task invalid_request_should_throw_validation_exception_and_not_invoke_handler()
    {
        var handlerInvoked = false;

        var act = () =>
            CreateSut(registerValidator: true)
                .Handle(
                    new FakeCommand(string.Empty),
                    TransactionBehaviorFixture.Handler("ok", () => handlerInvoked = true),
                    CancellationToken.None
                );

        var exception = await act.Should().ThrowAsync<BuildingBlocks.Exception.ValidationException>();
        exception.Which.Message.Should().Be("Name is required");
        handlerInvoked.Should().BeFalse();
    }
}
