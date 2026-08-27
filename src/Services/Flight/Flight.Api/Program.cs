using BuildingBlocks.Web;
using Flight;
using Flight.Extensions.Infrastructure;
using Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();
builder.AddModuleEventMapper<FlightEventMapper>();

builder.AddFlightModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();

app.UseSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Flight.Api
{
    public partial class Program { }
}
