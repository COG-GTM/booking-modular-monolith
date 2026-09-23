using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Exception;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Exceptions;
using Integration.Test.Fakes;
using Integration.Test.Routes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Integration.Test.Identity.Features;

using global::Identity.Identity.Features.RegisteringNewUser.V1;

public class RegisterNewUserEndpointTests : IdentityIntegrationTestBase
{
    public RegisterNewUserEndpointTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_return_ok_with_registered_user_when_request_is_valid()
    {
        // Arrange
        var request = new FakeRegisterNewUserRequestDto().Generate();

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await result.Content.ReadFromJsonAsync<RegisterNewUserResponseDto>();

        response.Should().NotBeNull();
        response!.Id.Should().NotBeEmpty();
        response.Username.Should().Be(request.Username);
        response.FirstName.Should().Be(request.FirstName);
        response.LastName.Should().Be(request.LastName);
        response.PassportNumber.Should().Be(request.PassportNumber);

        var persistedUser = await Fixture.ExecuteDbContextAsync(db =>
            db.Users.SingleOrDefaultAsync(x => x.UserName == request.Username)
        );

        persistedUser.Should().NotBeNull();
        persistedUser!.Id.Should().Be(response.Id);
        persistedUser.Email.Should().Be(request.Email);
    }

    [Fact]
    public async Task should_return_unauthorized_when_request_has_no_bearer_token()
    {
        // Arrange
        var request = new FakeRegisterNewUserRequestDto().Generate();

        var httpClient = Fixture.HttpClient;
        httpClient.DefaultRequestHeaders.Authorization = null;

        // Act
        var result = await httpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await Fixture.ExecuteDbContextAsync(db => db.Users.AnyAsync(x => x.UserName == request.Username)))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_return_bad_request_problem_details_when_request_fails_validation()
    {
        // Arrange
        var request = new FakeRegisterNewUserRequestDto().Generate() with
        {
            ConfirmPassword = "Mismatch@123",
        };

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await result.Content.ReadFromJsonAsync<ProblemDetails>();

        problem.Should().NotBeNull();
        problem!.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Title.Should().Be(nameof(ValidationException));
        problem.Detail.Should().Be("Passwords should match");
    }

    [Fact]
    public async Task should_return_bad_request_problem_details_when_password_violates_policy()
    {
        // Arrange
        var request = new FakeRegisterNewUserRequestDto().Generate() with
        {
            Password = "12345",
            ConfirmPassword = "12345",
        };

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await result.Content.ReadFromJsonAsync<ProblemDetails>();

        problem.Should().NotBeNull();
        problem!.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Title.Should().Be(nameof(RegisterIdentityUserException));
        problem.Detail.Should().Contain("Passwords must be at least 6 characters");

        (await Fixture.ExecuteDbContextAsync(db => db.Users.AnyAsync(x => x.UserName == request.Username)))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task should_return_bad_request_problem_details_when_username_is_duplicate()
    {
        // Arrange
        var request = new FakeRegisterNewUserRequestDto().Generate();
        (await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, request))
            .StatusCode.Should()
            .Be(HttpStatusCode.OK);

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Identity.RegisterUser, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await result.Content.ReadFromJsonAsync<ProblemDetails>();

        problem.Should().NotBeNull();
        problem!.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Title.Should().Be(nameof(RegisterIdentityUserException));
        problem.Detail.Should().Contain(request.Username);

        (await Fixture.ExecuteDbContextAsync(db => db.Users.CountAsync(x => x.UserName == request.Username)))
            .Should()
            .Be(1);
    }
}
