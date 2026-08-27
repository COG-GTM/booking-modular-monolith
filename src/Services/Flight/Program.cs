using Flight;
using Flight.Extensions.Infrastructure;
using BuildingBlocks.Web;
using Shared.ServiceHost;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure(typeof(FlightRoot).Assembly);
builder.Services.AddEventMapper<FlightEventMapper>();
builder.AddFlightModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseFlightModules();
app.UseSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace FlightService
{
    public partial class Program { }
}
