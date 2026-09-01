using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using Passenger;
using Passenger.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceInfrastructure<PassengerEventMapper>(TransportType.RabbitMq, typeof(PassengerRoot).Assembly);

builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UseServiceDefaults();
app.UseServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Passenger.Api
{
    public partial class Program { }
}
