using BuildingBlocks.Web;
using Flight.Extensions.Infrastructure;
using Flight.Host.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddFlightHostInfrastructure();
builder.AddFlightModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();

app.UseFlightHostInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Flight.Host
{
    public partial class Program { }
}
