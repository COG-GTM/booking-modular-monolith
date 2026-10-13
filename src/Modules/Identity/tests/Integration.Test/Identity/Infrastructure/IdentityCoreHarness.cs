using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using Identity.Configurations;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Test.Identity.Infrastructure;

/// <summary>
/// Docker-free ASP.NET Identity setup backed by an in-memory SQLite <see cref="IdentityContext"/>,
/// mirroring the password policy configured in <c>AddCustomIdentityServer</c>.
/// </summary>
internal sealed class IdentityCoreHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    private IdentityCoreHarness(SqliteConnection connection, ServiceProvider provider)
    {
        _connection = connection;
        _provider = provider;
        _scope = provider.CreateAsyncScope();

        Services = _scope.ServiceProvider;
        Context = Services.GetRequiredService<IdentityContext>();
        UserManager = Services.GetRequiredService<UserManager<User>>();
        Events = Services.GetRequiredService<RecordingEventDispatcher>().Events;
        SeederLogger = Services.GetRequiredService<RecordingLogger<IdentityDataSeeder>>();
    }

    public IServiceProvider Services { get; }
    public IdentityContext Context { get; }
    public UserManager<User> UserManager { get; }
    public List<object> Events { get; }
    public RecordingLogger<IdentityDataSeeder> SeederLogger { get; }

    public IdentityDataSeeder Seeder => Services.GetRequiredService<IdentityDataSeeder>();

    // IHttpContextAccessor is AsyncLocal-backed, so the ambient HttpContext SignInManager needs
    // must be installed from the test's own async flow, not from CreateAsync.
    public UserValidator UserValidator
    {
        get
        {
            Services.GetRequiredService<IHttpContextAccessor>().HttpContext ??= new DefaultHttpContext
            {
                RequestServices = Services,
            };
            return Services.GetRequiredService<UserValidator>();
        }
    }

    public static async Task<IdentityCoreHarness> CreateAsync(
        IdentitySeedOptions? seedOptions = null,
        bool pendingMigrations = false,
        Action<IServiceCollection>? configure = null
    )
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RecordingLogger<IdentityDataSeeder>>();
        services.AddSingleton<ILogger<IdentityDataSeeder>>(sp =>
            sp.GetRequiredService<RecordingLogger<IdentityDataSeeder>>()
        );
        services.AddSingleton<RecordingEventDispatcher>();
        services.AddSingleton<IEventDispatcher>(sp => sp.GetRequiredService<RecordingEventDispatcher>());
        services.AddSingleton(seedOptions ?? new IdentitySeedOptions());
        services.AddScoped<IdentityDataSeeder>();
        services.AddScoped<UserValidator>();

        services.AddDbContext<IdentityContext>(options =>
        {
            options.UseSqlite(connection);
            if (!pendingMigrations)
            {
                options.ReplaceService<IMigrationsAssembly, NoMigrationsAssembly>();
            }
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

        services.AddHttpContextAccessor();
        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddScheme<AuthenticationSchemeOptions, NoOpSignInHandler>(IdentityConstants.ApplicationScheme, _ => { });

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityContext>().Database.EnsureCreatedAsync();
        }

        return new IdentityCoreHarness(connection, provider);
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
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

    private sealed class NoOpSignInHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder), IAuthenticationSignInHandler
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());

        public Task SignInAsync(System.Security.Claims.ClaimsPrincipal user, AuthenticationProperties? properties) =>
            Task.CompletedTask;

        public Task SignOutAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }

    internal sealed class RecordingEventDispatcher : IEventDispatcher
    {
        public List<object> Events { get; } = new();

        public Task SendAsync<T>(
            IReadOnlyList<T> events,
            Type? type = null,
            CancellationToken cancellationToken = default
        )
            where T : IEvent
        {
            Events.AddRange(events.Cast<object>());
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T @event, Type? type = null, CancellationToken cancellationToken = default)
            where T : IEvent
        {
            Events.Add(@event!);
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
