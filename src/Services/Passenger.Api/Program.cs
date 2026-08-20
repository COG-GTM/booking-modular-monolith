using BuildingBlocks.Core;
using BuildingBlocks.Web;
using Passenger;
using Passenger.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure(services =>
    services.AddScoped<IEventMapper>(sp => sp.GetRequiredService<PassengerEventMapper>())
);

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
