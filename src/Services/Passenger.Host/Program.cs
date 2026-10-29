using BuildingBlocks.Web;
using Passenger.Extensions.Infrastructure;
using Passenger.Host.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddPassengerHostInfrastructure();
builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UsePassengerModules();
app.UsePassengerHostInfrastructure();
app.MapMinimalEndpoints();
app.Run();

namespace Passenger.Host
{
    public partial class Program { }
}
