using BuildingBlocks.Web;
using Passenger.Api.Extensions;
using Passenger.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();

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
