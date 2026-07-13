using BuildingBlocks.Web;
using Passenger.Extensions.Infrastructure;
using PassengerApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceInfrastructure();

builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UseServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace PassengerApi
{
    public partial class Program { }
}
