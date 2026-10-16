namespace Unit.Test.Extensions;

using System.Net;
using BuildingBlocks.Caching;
using BuildingBlocks.EFCore;
using BuildingBlocks.Logging;
using BuildingBlocks.Validation;
using Duende.IdentityServer.Stores;
using Duende.IdentityServer.Validation;
using FluentAssertions;
using FluentValidation;
using global::Identity.Configurations;
using global::Identity.Data;
using global::Identity.Data.Seed;
using global::Identity.Extensions.Infrastructure;
using global::Identity.Identity.Constants;
using global::Identity.Identity.Features.RegisteringNewUser.V1;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Constants = global::Identity.Identity.Constants.Constants;

public class InfrastructureExtensionsTests
{
    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AuthOptions:IssuerUri"] = "https://identity.test",
                ["PostgresOptions:ConnectionString:Identity"] = "Host=localhost;Database=identity_test",
                ["PersistMessageOptions:ConnectionString:Identity"] = "Host=localhost;Database=identity_outbox_test",
            }
        );

        builder.AddIdentityModules();

        return builder;
    }

    [Fact]
    public void should_configure_aspnet_identity_password_policy()
    {
        using var provider = CreateBuilder().Services.BuildServiceProvider();

        var identityOptions = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identityOptions.Password.RequiredLength.Should().Be(6);
        identityOptions.Password.RequireDigit.Should().BeFalse();
        identityOptions.Password.RequireNonAlphanumeric.Should().BeFalse();
        identityOptions.Password.RequireUppercase.Should().BeFalse();
    }

    [Fact]
    public void should_register_user_validator_as_resource_owner_password_validator()
    {
        var services = CreateBuilder().Services;

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IResourceOwnerPasswordValidator)
                && d.ImplementationType == typeof(UserValidator)
            );
    }

    [Fact]
    public void should_register_identity_data_seeder()
    {
        var services = CreateBuilder().Services;

        services
            .Should()
            .Contain(d => d.ServiceType == typeof(IDataSeeder) && d.ImplementationType == typeof(IdentityDataSeeder));
    }

    [Fact]
    public void should_register_fluent_validation_validators()
    {
        using var provider = CreateBuilder().Services.BuildServiceProvider();

        provider.GetRequiredService<IValidator<RegisterNewUser>>().Should().BeOfType<RegisterNewUserValidator>();
    }

    [Fact]
    public void should_register_mediatr_pipeline_behaviors_in_order()
    {
        var behaviorTypes = CreateBuilder()
            .Services.Where(d =>
                d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)
            )
            .Select(d => d.ImplementationType!.GetGenericTypeDefinition())
            .ToList();

        behaviorTypes
            .Should()
            .ContainInOrder(
                typeof(ValidationBehavior<,>),
                typeof(LoggingBehavior<,>),
                typeof(EfTxIdentityBehavior<,>),
                typeof(CachingBehavior<,>),
                typeof(InvalidateCachingBehavior<,>)
            );

        CreateBuilder()
            .Services.Should()
            .Contain(d =>
                d.ImplementationType == typeof(EfTxIdentityBehavior<,>) && d.Lifetime == ServiceLifetime.Scoped
            );
    }

    [Fact]
    public void should_configure_forwarded_headers()
    {
        using var provider = CreateBuilder().Services.BuildServiceProvider();

        provider
            .GetRequiredService<IOptions<ForwardedHeadersOptions>>()
            .Value.ForwardedHeaders.Should()
            .Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
    }

    [Fact]
    public async Task should_resolve_configured_identity_server_client_and_scopes()
    {
        using var provider = CreateBuilder().Services.BuildServiceProvider();

        var clientStore = provider.GetRequiredService<IClientStore>();
        (await clientStore.FindClientByIdAsync("client")).Should().NotBeNull();
        (await clientStore.FindClientByIdAsync("does-not-exist")).Should().BeNull();

        var resourceStore = provider.GetRequiredService<IResourceStore>();
        var scopes = await resourceStore.FindApiScopesByNameAsync(
            new[] { Constants.StandardScopes.BookingModularMonolith }
        );
        scopes.Should().ContainSingle(s => s.Name == Constants.StandardScopes.BookingModularMonolith);
    }

    [Fact]
    public async Task should_return_401_and_403_instead_of_cookie_redirects()
    {
        using var provider = CreateBuilder().Services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var scheme = new AuthenticationScheme(
            IdentityConstants.ApplicationScheme,
            null,
            typeof(CookieAuthenticationHandler)
        );

        var loginContext = new RedirectContext<CookieAuthenticationOptions>(
            new DefaultHttpContext(),
            scheme,
            options,
            new AuthenticationProperties(),
            "/login"
        );
        await options.Events.OnRedirectToLogin(loginContext);
        loginContext.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);

        var deniedContext = new RedirectContext<CookieAuthenticationOptions>(
            new DefaultHttpContext(),
            scheme,
            options,
            new AuthenticationProperties(),
            "/denied"
        );
        await options.Events.OnRedirectToAccessDenied(deniedContext);
        deniedContext.Response.StatusCode.Should().Be((int)HttpStatusCode.Forbidden);
    }
}
