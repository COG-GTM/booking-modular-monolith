using BuildingBlocks.Web;
using Flight.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();

builder.AddFlightModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();

app.UserSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace FlightService
{
    public partial class Program { }
}
