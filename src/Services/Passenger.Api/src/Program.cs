using BuildingBlocks.Web;
using Microsoft.Extensions.Hosting;
using Passenger;
using Passenger.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceHost(typeof(PassengerRoot).Assembly, persistMessageConnectionName: "passenger-persist-message");
builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();
app.UseServiceHost();
app.MapMinimalEndpoints();

app.Run();

namespace Passenger.Api
{
    public partial class Program { }
}
