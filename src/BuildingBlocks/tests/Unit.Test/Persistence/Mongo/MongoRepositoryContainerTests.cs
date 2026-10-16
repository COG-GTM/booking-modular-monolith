using BuildingBlocks.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Testcontainers.MongoDb;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class MongoRepositoryContainerTests : IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder().WithImage("mongo:latest").Build();

    public Task InitializeAsync() => _mongo.StartAsync();

    public Task DisposeAsync() => _mongo.DisposeAsync().AsTask();

    private MongoRepository<PersistMongoFlight, Guid> CreateRepository()
    {
        var context = new PersistMongoDbContext(
            Options.Create(
                new MongoOptions
                {
                    ConnectionString = _mongo.GetConnectionString(),
                    DatabaseName = $"test_{Guid.NewGuid():N}",
                }
            )
        );
        return new MongoRepository<PersistMongoFlight, Guid>(context);
    }

    [Fact]
    public async Task add_find_and_get_all_round_trip()
    {
        var repository = CreateRepository();
        var flight = new PersistMongoFlight { Id = Guid.NewGuid(), Name = "alpha" };
        var other = new PersistMongoFlight { Id = Guid.NewGuid(), Name = "beta" };

        await repository.AddAsync(flight);
        await repository.AddAsync(other);

        (await repository.FindByIdAsync(flight.Id))!.Name.Should().Be("alpha");
        (await repository.FindOneAsync(e => e.Name == "beta"))!.Id.Should().Be(other.Id);
        (await repository.FindAsync(e => e.Name.StartsWith("a"))).Should().ContainSingle();
        (await repository.GetAllAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task update_replaces_document()
    {
        var repository = CreateRepository();
        var flight = new PersistMongoFlight { Id = Guid.NewGuid(), Name = "before" };
        await repository.AddAsync(flight);

        flight.Name = "after";
        await repository.UpdateAsync(flight);

        (await repository.FindByIdAsync(flight.Id))!.Name.Should().Be("after");
    }

    [Fact]
    public async Task delete_variants_remove_documents()
    {
        var repository = CreateRepository();
        var a = new PersistMongoFlight { Id = Guid.NewGuid(), Name = "a" };
        var b = new PersistMongoFlight { Id = Guid.NewGuid(), Name = "b" };
        var c = new PersistMongoFlight { Id = Guid.NewGuid(), Name = "c" };
        var d = new PersistMongoFlight { Id = Guid.NewGuid(), Name = "d" };
        await repository.AddAsync(a);
        await repository.AddAsync(b);
        await repository.AddAsync(c);
        await repository.AddAsync(d);

        await repository.DeleteByIdAsync(a.Id);
        await repository.DeleteAsync(b);
        await repository.DeleteAsync(e => e.Id == c.Id);
        await repository.DeleteRangeAsync([d]);

        (await repository.GetAllAsync()).Should().BeEmpty();
    }
}
