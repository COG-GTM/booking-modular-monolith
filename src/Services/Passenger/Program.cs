using BuildingBlocks.Web;
using Passenger;
using Passenger.Extensions.Infrastructure;
using Shared.ServiceHost;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure(typeof(PassengerRoot).Assembly);

builder.Services.AddEventMapper<PassengerEventMapper>();

builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UseSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace PassengerService
{
    public partial class Program { }
}
