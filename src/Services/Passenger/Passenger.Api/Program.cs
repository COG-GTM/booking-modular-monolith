using BuildingBlocks.Web;
using Passenger;
using Passenger.Extensions.Infrastructure;
using Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();
builder.AddModuleEventMapper<PassengerEventMapper>();

builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UseSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Passenger.Api
{
    public partial class Program { }
}
