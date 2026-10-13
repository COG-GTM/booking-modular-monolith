using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Validation;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class UserValidatorTests
{
    private const string UserName = "samh";
    private const string Password = "configured-password";

    [Fact]
    public async Task should_return_invalid_credentials_for_unknown_username_without_attempting_sign_in()
    {
        await using var harness = await ValidatorHarness.CreateAsync();
        await harness.SeedUserAsync();

        var context = new ResourceOwnerPasswordValidationContext { UserName = "nobody", Password = Password };

        var act = async () => await harness.Validator.ValidateAsync(context);

        await act.Should().NotThrowAsync();
        AssertInvalidCredentials(context.Result);
        harness.AuthenticationService.SignIns.Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_invalid_credentials_for_wrong_password_and_count_the_failed_attempt()
    {
        await using var harness = await ValidatorHarness.CreateAsync();
        var user = await harness.SeedUserAsync();

        var context = new ResourceOwnerPasswordValidationContext { UserName = UserName, Password = "wrong" };

        await harness.Validator.ValidateAsync(context);

        AssertInvalidCredentials(context.Result);
        harness.AuthenticationService.SignIns.Should().BeEmpty();
        (await harness.UserManager.GetAccessFailedCountAsync(user)).Should().Be(1);
    }

    [Fact]
    public async Task should_succeed_with_subject_and_name_claims_for_valid_credentials()
    {
        await using var harness = await ValidatorHarness.CreateAsync();
        var user = await harness.SeedUserAsync();

        var context = new ResourceOwnerPasswordValidationContext { UserName = UserName, Password = Password };

        await harness.Validator.ValidateAsync(context);

        var result = context.Result;
        result.IsError.Should().BeFalse();
        result.Subject.Should().NotBeNull();
        result.Subject!.FindFirst("sub")!.Value.Should().Be(user.Id.ToString());
        result.Subject.FindFirst("amr")!.Value.Should().Be("custom");
        result.Subject.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be(user.Id.ToString());
        result.Subject.FindFirst(ClaimTypes.Name)!.Value.Should().Be(UserName);

        harness
            .AuthenticationService.SignIns.Should()
            .ContainSingle()
            .Which.Scheme.Should()
            .Be(IdentityConstants.ApplicationScheme);
    }

    [Fact]
    public async Task should_return_invalid_credentials_when_user_is_locked_out_even_with_valid_password()
    {
        await using var harness = await ValidatorHarness.CreateAsync();
        var user = await harness.SeedUserAsync();
        await harness.UserManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));

        var context = new ResourceOwnerPasswordValidationContext { UserName = UserName, Password = Password };

        await harness.Validator.ValidateAsync(context);

        AssertInvalidCredentials(context.Result);
        harness.AuthenticationService.SignIns.Should().BeEmpty();
    }

    private static void AssertInvalidCredentials(GrantValidationResult result)
    {
        result.IsError.Should().BeTrue();
        result.Error.Should().Be("unauthorized_client");
        result.ErrorDescription.Should().Be("Invalid Credentials");
        result.Subject.Should().BeNull();
    }

    private sealed class ValidatorHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private ValidatorHarness(SqliteConnection connection, ServiceProvider provider)
        {
            _connection = connection;
            _provider = provider;
            _scope = provider.CreateAsyncScope();

            var httpContext = new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
            _scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

            UserManager = _scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            AuthenticationService = _scope.ServiceProvider.GetRequiredService<RecordingAuthenticationService>();
            Validator = ActivatorUtilities.CreateInstance<UserValidator>(_scope.ServiceProvider);
        }

        public UserManager<User> UserManager { get; }
        public RecordingAuthenticationService AuthenticationService { get; }
        public UserValidator Validator { get; }

        public async Task<User> SeedUserAsync()
        {
            var user = new User
            {
                UserName = UserName,
                Email = "samh@test.com",
                FirstName = "Sam",
                LastName = "H",
                PassPortNumber = "12345678",
                EmailConfirmed = true,
                LockoutEnabled = true,
            };

            (await UserManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
            return user;
        }

        public static async Task<ValidatorHarness> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IHttpContextAccessor, StaticHttpContextAccessor>();
            services.AddSingleton<RecordingAuthenticationService>();
            services.AddSingleton<IAuthenticationService>(sp =>
                sp.GetRequiredService<RecordingAuthenticationService>()
            );
            services.AddAuthentication();

            services.AddDbContext<IdentityContext>(options =>
            {
                options.UseSqlite(connection);
                options.ReplaceService<IMigrationsAssembly, NoMigrationsAssembly>();
            });

            services
                .AddIdentityCore<User>(config =>
                {
                    config.Password.RequiredLength = 6;
                    config.Password.RequireDigit = false;
                    config.Password.RequireNonAlphanumeric = false;
                    config.Password.RequireUppercase = false;
                })
                .AddRoles<Role>()
                .AddSignInManager()
                .AddEntityFrameworkStores<IdentityContext>();

            var provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IdentityContext>().Database.EnsureCreatedAsync();
            }

            return new ValidatorHarness(connection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    // HttpContextAccessor is AsyncLocal-based, so a context set inside the harness's async factory
    // would not be visible to the test method; a plain property-backed accessor sidesteps that.
    private sealed class StaticHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public List<(string? Scheme, ClaimsPrincipal Principal)> SignIns { get; } = new();

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties
        )
        {
            SignIns.Add((scheme, principal));
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            Task.CompletedTask;
    }

    private sealed class NoMigrationsAssembly : IMigrationsAssembly
    {
        public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } = new Dictionary<string, TypeInfo>();
        public ModelSnapshot? ModelSnapshot => null;
        public Assembly Assembly => typeof(NoMigrationsAssembly).Assembly;

        public string? FindMigrationId(string nameOrId) => null;

        public Migration CreateMigration(TypeInfo migrationClass, string activeProvider) =>
            throw new NotSupportedException();
    }
}
