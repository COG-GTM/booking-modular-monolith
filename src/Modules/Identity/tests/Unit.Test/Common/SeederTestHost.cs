using BuildingBlocks.Core;
using Identity.Configurations;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Fakes;

namespace Unit.Test.Common;

/// <summary>
/// Hosts <see cref="IdentityDataSeeder"/> against an in-memory Sqlite database with a real
/// <see cref="UserManager{TUser}"/>, exposing the collaborators needed to assert on side effects.
/// </summary>
public sealed class SeederTestHost : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    private SeederTestHost(
        SqliteConnection connection,
        ServiceProvider provider,
        IEventDispatcher eventDispatcher,
        CapturingLogger<IdentityDataSeeder> logger
    )
    {
        _connection = connection;
        _provider = provider;
        _scope = provider.CreateAsyncScope();
        EventDispatcher = eventDispatcher;
        Logger = logger;
    }

    public IdentityDataSeeder Seeder => _scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
    public IdentityContext Context => _scope.ServiceProvider.GetRequiredService<IdentityContext>();
    public UserManager<User> UserManager => _scope.ServiceProvider.GetRequiredService<UserManager<User>>();
    public IEventDispatcher EventDispatcher { get; }
    public CapturingLogger<IdentityDataSeeder> Logger { get; }

    public static async Task<SeederTestHost> CreateAsync(string environmentName, IdentitySeedOptions seedOptions)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var eventDispatcher = Substitute.For<IEventDispatcher>();
        var logger = new CapturingLogger<IdentityDataSeeder>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IdentityContext>(options =>
            options
                .UseSqlite(
                    connection,
                    sqlite => sqlite.MigrationsAssembly(typeof(SeederTestHost).Assembly.GetName().Name)
                )
                .UseSnakeCaseNamingConvention()
        );

        services
            .AddIdentityCore<User>(options =>
            {
                options.Password.RequiredLength = 6;
                options.Password.RequireDigit = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<IdentityContext>();

        services.AddSingleton(env);
        services.AddSingleton(seedOptions);
        services.AddSingleton(eventDispatcher);
        services.AddSingleton<ILogger<IdentityDataSeeder>>(logger);
        services.AddScoped<IdentityDataSeeder>();

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityContext>().Database.EnsureCreatedAsync();
        }

        return new SeederTestHost(connection, provider, eventDispatcher, logger);
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
