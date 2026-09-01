using BuildingBlocks.Web;
using Flight.Extensions.Infrastructure;
using FlightService.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();

builder.AddFlightModules();

var app = builder.Build();

// ref: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-7.0#routing-basics
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
