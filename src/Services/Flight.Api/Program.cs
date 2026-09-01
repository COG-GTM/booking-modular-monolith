using BuildingBlocks.Web;
using Flight.Extensions.Infrastructure;
using FlightApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceInfrastructure();

builder.AddFlightModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();

app.UseServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace FlightApi
{
    public partial class Program { }
}
