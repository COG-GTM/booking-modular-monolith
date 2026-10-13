using BuildingBlocks.Web;
using Passenger.Extensions.Infrastructure;
using Passenger.Host.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddPassengerServiceHost();
builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UsePassengerServiceHost();
app.MapMinimalEndpoints();

app.Run();

namespace Passenger.Host
{
    public partial class Program { }
}
