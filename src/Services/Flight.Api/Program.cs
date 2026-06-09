using BuildingBlocks.Core;
using BuildingBlocks.Web;
using Flight;
using Flight.Extensions.Infrastructure;
using SharedServiceExtensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedServiceInfrastructure(typeof(FlightRoot).Assembly);
builder.Services.AddScoped<IEventMapper, FlightEventMapper>();

builder.AddFlightModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();

app.UseSharedServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Flight.Api
{
    public partial class Program { }
}
