using BuildingBlocks.Web;
using Passenger.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();

builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UserSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace PassengerService
{
    public partial class Program { }
}
