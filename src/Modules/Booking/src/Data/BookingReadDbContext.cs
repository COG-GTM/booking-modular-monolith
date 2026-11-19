using BuildingBlocks.Mongo;
using Humanizer;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Booking.Data;

using Booking.Models;

public class BookingReadDbContext : MongoDbContext
{
    public BookingReadDbContext(IOptionsSnapshot<MongoOptions> options)
        : base(Options.Create(options.Get("Booking")))
    {
        Booking = GetCollection<BookingReadModel>(nameof(Booking).Underscore());
    }

    public IMongoCollection<BookingReadModel> Booking { get; }
}
