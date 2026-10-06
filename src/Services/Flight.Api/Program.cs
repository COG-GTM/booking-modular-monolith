using BuildingBlocks.Web;
using Flight;
using Flight.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceHostInfrastructure(typeof(FlightRoot).Assembly);
builder.Services.AddModuleEventMapper<FlightEventMapper>();

builder.AddFlightModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();

app.UseServiceHostInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Flight.Api
{
    public partial class Program { }
}
