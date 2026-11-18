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
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Integration.Test.Identity.Infrastructure;

/// <summary>
/// Docker-free <see cref="IdentityDataSeeder"/> host backed by an in-memory SQLite <see cref="IdentityContext"/>,
/// using the password policy configured in <c>AddCustomIdentityServer</c>.
/// </summary>
internal sealed class IdentitySeederHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    private IdentitySeederHarness(SqliteConnection connection, ServiceProvider provider)
    {
        _connection = connection;
        _provider = provider;
        _scope = provider.CreateAsyncScope();

        Services = _scope.ServiceProvider;
        Context = Services.GetRequiredService<IdentityContext>();
        Events = Services.GetRequiredService<RecordingEventDispatcher>().Events;
        Logger = Services.GetRequiredService<RecordingLogger<IdentityDataSeeder>>();
    }

    public IServiceProvider Services { get; }
    public IdentityContext Context { get; }
    public List<object> Events { get; }
    public RecordingLogger<IdentityDataSeeder> Logger { get; }

    public Microsoft.AspNetCore.Identity.UserManager<User> UserManager =>
        Services.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();

    public IdentityDataSeeder Seeder => Services.GetRequiredService<IdentityDataSeeder>();

    public static async Task<IdentitySeederHarness> CreateAsync(
        IdentitySeedOptions seedOptions,
        string environmentName,
        bool pendingMigrations = false
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
        services.AddSingleton(seedOptions);
        services.AddSingleton<IWebHostEnvironment>(new StubWebHostEnvironment(environmentName));
        services.AddScoped<IdentityDataSeeder>();

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
            .AddEntityFrameworkStores<IdentityContext>();

        var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityContext>().Database.EnsureCreatedAsync();
        }

        return new IdentitySeederHarness(connection, provider);
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class StubWebHostEnvironment(string environmentName) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Identity.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
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
