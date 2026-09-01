using FluentValidation.TestHelper;
using Identity.Identity.Features.RegisteringNewUser.V1;
using Xunit;

namespace Unit.Test.Identity.Features;

public class RegisterNewUserValidatorTests
{
    private readonly RegisterNewUserValidator _validator = new();

    private static RegisterNewUser ValidCommand =>
        new(
            FirstName: "John",
            LastName: "Doe",
            Username: "johndoe",
            Email: "john@example.com",
            Password: "P@ssw0rd",
            ConfirmPassword: "P@ssw0rd",
            PassportNumber: "AB123456"
        );

    [Fact]
    public void empty_password_should_fail()
    {
        var command = ValidCommand with { Password = string.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Password).WithErrorMessage("Please enter the password");
    }

    [Fact]
    public void empty_confirm_password_should_fail()
    {
        var command = ValidCommand with { ConfirmPassword = string.Empty };
        var result = _validator.TestValidate(command);
        result
            .ShouldHaveValidationErrorFor(x => x.ConfirmPassword)
            .WithErrorMessage("Please enter the confirmation password");
    }

    [Fact]
    public void mismatched_passwords_should_fail()
    {
        var command = ValidCommand with { Password = "P@ssw0rd", ConfirmPassword = "Different1" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Password).WithErrorMessage("Passwords should match");
    }

    [Fact]
    public void empty_username_should_fail()
    {
        var command = ValidCommand with { Username = string.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Username).WithErrorMessage("Please enter the username");
    }

    [Fact]
    public void empty_first_name_should_fail()
    {
        var command = ValidCommand with { FirstName = string.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.FirstName).WithErrorMessage("Please enter the first name");
    }

    [Fact]
    public void empty_last_name_should_fail()
    {
        var command = ValidCommand with { LastName = string.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.LastName).WithErrorMessage("Please enter the last name");
    }

    [Fact]
    public void empty_email_should_fail()
    {
        var command = ValidCommand with { Email = string.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage("Please enter the last email");
    }

    [Fact]
    public void invalid_email_should_fail()
    {
        var command = ValidCommand with { Email = "not-an-email" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Email).WithErrorMessage("A valid email is required");
    }

    [Fact]
    public void valid_command_should_pass_all_validation()
    {
        var result = _validator.TestValidate(ValidCommand);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
