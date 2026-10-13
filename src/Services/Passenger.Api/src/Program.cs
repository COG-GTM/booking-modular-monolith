using BuildingBlocks.Web;
using Passenger;
using Passenger.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceHostInfrastructure<PassengerEventMapper>(typeof(PassengerRoot).Assembly);
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
