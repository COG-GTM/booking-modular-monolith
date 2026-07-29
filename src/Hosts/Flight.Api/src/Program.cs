using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using Flight.Extensions.Infrastructure;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure(TransportType.RabbitMq);

builder.AddFlightModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();

app.UserSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Flight.Api
{
    public partial class Program { }
}
