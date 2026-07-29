using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using Identity.Extensions.Infrastructure;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure(TransportType.RabbitMq);

builder.AddIdentityModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseIdentityModules();

app.UserSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Identity.Api
{
    public partial class Program { }
}
