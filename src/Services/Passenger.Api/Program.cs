using BuildingBlocks.Web;
using Passenger;
using Passenger.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceHostInfrastructure(typeof(PassengerRoot).Assembly);
builder.Services.AddModuleEventMapper<PassengerEventMapper>();

builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UseServiceHostInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Passenger.Api
{
    public partial class Program { }
}
