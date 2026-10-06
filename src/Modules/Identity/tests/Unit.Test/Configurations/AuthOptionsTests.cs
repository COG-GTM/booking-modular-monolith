using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using FluentAssertions;
using Identity.Configurations;
using Xunit;

namespace Unit.Test.Configurations;

public class AuthOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void client_secret_should_be_required_by_data_annotations(string? clientSecret)
    {
        var authOptions = new AuthOptions { IssuerUri = "https://localhost", ClientSecret = clientSecret };

        var results = Validate(authOptions);

        results.Should().ContainSingle().Which.MemberNames.Should().BeEquivalentTo(nameof(AuthOptions.ClientSecret));
    }

    [Fact]
    public void configured_client_secret_should_satisfy_data_annotations()
    {
        var authOptions = new AuthOptions { ClientSecret = "configured-secret" };

        Validate(authOptions).Should().BeEmpty();
    }

    [Fact]
    public void issuer_uri_should_remain_optional()
    {
        var authOptions = new AuthOptions { IssuerUri = null, ClientSecret = "configured-secret" };

        Validate(authOptions).Should().BeEmpty();
    }

    private static List<ValidationResult> Validate(AuthOptions authOptions)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            authOptions,
            new ValidationContext(authOptions),
            results,
            validateAllProperties: true
        );
        return results;
    }
}
