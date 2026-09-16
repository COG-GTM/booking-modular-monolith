using BuildingBlocks.Mongo;
using Humanizer;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Payments.FPS.OutboundPayments.Models;

namespace Payments.FPS.Data;

public class PaymentsReadDbContext : MongoDbContext
{
    public PaymentsReadDbContext(IOptions<MongoOptions> options)
        : base(options)
    {
        OutboundPayment = GetCollection<OutboundPaymentReadModel>(nameof(OutboundPayment).Underscore());
    }

    public IMongoCollection<OutboundPaymentReadModel> OutboundPayment { get; }
}
