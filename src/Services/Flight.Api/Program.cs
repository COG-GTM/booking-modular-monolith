using BuildingBlocks.Web;
using Flight.Api.Extensions;
using Flight.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();

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
