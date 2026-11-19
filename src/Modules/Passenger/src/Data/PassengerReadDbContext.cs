using BuildingBlocks.Mongo;
using Humanizer;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Passenger.Data;

using Passengers.Models;

public class PassengerReadDbContext : MongoDbContext
{
    public PassengerReadDbContext(IOptionsSnapshot<MongoOptions> options)
        : base(Options.Create(options.Get("Passenger")))
    {
        Passenger = GetCollection<PassengerReadModel>(nameof(Passenger).Underscore());
    }

    public IMongoCollection<PassengerReadModel> Passenger { get; }
}
