using System.Text.Json;
using BuildingBlocks.Validation;
using FluentAssertions;
using FluentValidation;
using Xunit;

namespace BuildingBlocks.Unit.Test.ValidationTests;

public class HandleValidationTests
{
    private sealed record ValidationForm(string Name, int Age);

    private sealed class ValidationFormValidator : AbstractValidator<ValidationForm>
    {
        public ValidationFormValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
            RuleFor(x => x.Age).GreaterThan(0).WithMessage("Age must be positive");
        }
    }

    [Fact]
    public async Task handle_validation_async_passes_for_a_valid_request()
    {
        var validator = new ValidationFormValidator();

        var act = () => validator.HandleValidationAsync(new ValidationForm("alice", 30));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task handle_validation_async_throws_the_first_rule_failure_message()
    {
        var validator = new ValidationFormValidator();

        var act = () => validator.HandleValidationAsync(new ValidationForm(string.Empty, 0));

        var ex = await act.Should().ThrowAsync<BuildingBlocks.Exception.ValidationException>();
        ex.Which.Message.Should().Be("Name is required");
    }

    [Fact]
    public void validation_error_drops_an_empty_field_and_keeps_a_non_empty_one()
    {
        new ValidationError(string.Empty, "m").Field.Should().BeNull();
        new ValidationError("Name", "m").Field.Should().Be("Name");
    }

    [Fact]
    public void validation_result_model_serializes_to_problem_json()
    {
        var model = new ValidationResultModel
        {
            Errors = [new FluentValidation.Results.ValidationFailure("Name", "required")],
        };

        using var doc = JsonDocument.Parse(model.ToString());

        doc.RootElement.GetProperty("StatusCode").GetInt32().Should().Be(400);
        doc.RootElement.GetProperty("Message").GetString().Should().Be("Validation Failed.");
        doc.RootElement.GetProperty("Errors").GetArrayLength().Should().Be(1);
    }
}
