using Booking.Extensions.Infrastructure;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure(TransportType.RabbitMq);

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UserSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Booking.Api
{
    public partial class Program { }
}
