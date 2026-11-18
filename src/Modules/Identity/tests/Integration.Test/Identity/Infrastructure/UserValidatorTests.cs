using System;
using System.Threading;
using System.Threading.Tasks;
using Duende.IdentityServer.Validation;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Validator = global::Identity.Configurations.UserValidator;

namespace Integration.Test.Identity.Infrastructure;

public class UserValidatorTests : IAsyncLifetime
{
    private const string Password = "CorrectPassword@123";
    private const int MaxFailedAttempts = 3;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly UserManager<User> _users;
    private readonly TrackingUserStore _store;
    private readonly Validator _validator;
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly User _user = new()
    {
        Id = Guid.NewGuid(),
        UserName = "known-user",
        FirstName = "Test",
        LastName = "User",
        PassPortNumber = "TEST123",
        LockoutEnabled = true,
    };

    public UserValidatorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication();
        services.AddDbContext<IdentityContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services
            .AddIdentityCore<User>(options =>
            {
                options.Lockout.MaxFailedAccessAttempts = MaxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<IdentityContext>()
            .AddSignInManager();
        services.AddScoped<IUserStore<User>, TrackingUserStore>();
        services.AddSingleton(_authentication);
        var accessor = new PropertyHttpContextAccessor { HttpContext = new DefaultHttpContext() };
        services.AddSingleton<IHttpContextAccessor>(accessor);
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        accessor.HttpContext.RequestServices = _scope.ServiceProvider;
        _users = _scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        _store = (TrackingUserStore)_scope.ServiceProvider.GetRequiredService<IUserStore<User>>();
        _validator = new Validator(_scope.ServiceProvider.GetRequiredService<SignInManager<User>>(), _users);
    }

    public async Task InitializeAsync()
    {
        (await _users.CreateAsync(_user, Password)).Succeeded.Should().BeTrue();
        _store.FindByNameCalls = 0;
    }

    public Task DisposeAsync()
    {
        _scope.Dispose();
        return _provider.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task unknown_username_should_get_same_error_as_wrong_password()
    {
        var unknown = await ValidateAsync("unknown-user", Password);
        var wrongPassword = await ValidateAsync(_user.UserName, "WrongPassword@123");

        AssertInvalidCredentials(wrongPassword);
        AssertInvalidCredentials(unknown);
        unknown.Result.Error.Should().Be(wrongPassword.Result.Error);
        unknown.Result.ErrorDescription.Should().Be(wrongPassword.Result.ErrorDescription);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task empty_or_null_username_should_reject_without_looking_up_user(string? username)
    {
        var context = await ValidateAsync(username, Password);

        AssertInvalidCredentials(context);
        _store.FindByNameCalls.Should().Be(0);
    }

    [Fact]
    public async Task unknown_username_with_null_password_should_reject_without_throwing()
    {
        var context = await ValidateAsync("unknown-user", null);

        AssertInvalidCredentials(context);
    }

    [Fact]
    public async Task valid_credentials_should_succeed_with_user_id_as_subject()
    {
        var context = await ValidateAsync(_user.UserName, Password);

        context.Result.IsError.Should().BeFalse();
        context.Result.Subject.FindFirst("sub")!.Value.Should().Be(_user.Id.ToString());
    }

    [Fact]
    public async Task wrong_password_should_increment_failed_count_and_lock_out_at_configured_limit()
    {
        for (var attempt = 1; attempt <= MaxFailedAttempts; attempt++)
        {
            AssertInvalidCredentials(await ValidateAsync(_user.UserName, "WrongPassword@123"));
            var persistedUser = await ReloadUserAsync();
            if (attempt < MaxFailedAttempts)
            {
                persistedUser.AccessFailedCount.Should().Be(attempt);
                (await _users.IsLockedOutAsync(persistedUser)).Should().BeFalse();
            }
            else
            {
                (await _users.IsLockedOutAsync(persistedUser)).Should().BeTrue();
            }
        }

        AssertInvalidCredentials(await ValidateAsync(_user.UserName, Password));
        (await _users.IsLockedOutAsync(await ReloadUserAsync())).Should().BeTrue();
    }

    [Fact]
    public async Task successful_validation_should_not_sign_in()
    {
        var context = await ValidateAsync(_user.UserName, Password);

        context.Result.IsError.Should().BeFalse();
        await _authentication.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
    }

    private async Task<User> ReloadUserAsync()
    {
        _scope.ServiceProvider.GetRequiredService<IdentityContext>().ChangeTracker.Clear();
        return (await _users.FindByIdAsync(_user.Id.ToString()))!;
    }

    private async Task<ResourceOwnerPasswordValidationContext> ValidateAsync(string? username, string? password)
    {
        var context = new ResourceOwnerPasswordValidationContext { UserName = username, Password = password };
        await _validator.ValidateAsync(context);
        return context;
    }

    private static void AssertInvalidCredentials(ResourceOwnerPasswordValidationContext context)
    {
        context.Result.IsError.Should().BeTrue();
        context.Result.Error.Should().Be("unauthorized_client");
        context.Result.ErrorDescription.Should().Be("Invalid Credentials");
    }

    private sealed class PropertyHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class TrackingUserStore(IdentityContext context)
        : UserStore<User, Role, IdentityContext, Guid, UserClaim, UserRole, UserLogin, UserToken, RoleClaim>(context)
    {
        public int FindByNameCalls { get; set; }

        public override Task<User?> FindByNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken = default
        )
        {
            FindByNameCalls++;
            return base.FindByNameAsync(normalizedUserName, cancellationToken);
        }
    }
}
