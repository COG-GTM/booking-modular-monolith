using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class ApiUserSecretsTests
{
    private const string ApiUserSecretsId = "booking-modular-monolith-api";

    [Fact]
    public void Api_assembly_should_declare_user_secrets_id()
    {
        typeof(Api.Program)
            .Assembly.GetCustomAttribute<UserSecretsIdAttribute>()
            .Should()
            .NotBeNull()
            .And.Subject.As<UserSecretsIdAttribute>()
            .UserSecretsId.Should()
            .Be(ApiUserSecretsId);
    }

    [Fact]
    public void Development_api_host_should_load_identity_credentials_from_user_secrets()
    {
        var builder = CreateApiBuilder(Environments.Development);

        builder
            .Configuration.Sources.OfType<JsonConfigurationSource>()
            .Should()
            .ContainSingle(source => source.Path == "secrets.json");
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("test")]
    [InlineData("Production")]
    public void Non_development_api_host_should_not_load_user_secrets(string environmentName)
    {
        var builder = CreateApiBuilder(environmentName);

        builder
            .Configuration.Sources.OfType<JsonConfigurationSource>()
            .Should()
            .NotContain(source => source.Path == "secrets.json");
    }

    private static WebApplicationBuilder CreateApiBuilder(string environmentName) =>
        WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(Api.Program).Assembly.GetName().Name,
                EnvironmentName = environmentName,
            }
        );
}
