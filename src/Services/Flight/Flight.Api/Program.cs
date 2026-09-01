using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using Flight;
using Flight.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceInfrastructure<FlightEventMapper>(TransportType.RabbitMq, typeof(FlightRoot).Assembly);

builder.AddFlightModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();

app.UseServiceDefaults();
app.UseServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Flight.Api
{
    public partial class Program { }
}
