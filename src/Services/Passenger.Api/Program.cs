using BuildingBlocks.Core;
using BuildingBlocks.Web;
using Passenger;
using Passenger.Extensions.Infrastructure;
using SharedServiceExtensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedServiceInfrastructure(typeof(PassengerRoot).Assembly);
builder.Services.AddScoped<IEventMapper, PassengerEventMapper>();

builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UseSharedServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Passenger.Api
{
    public partial class Program { }
}
