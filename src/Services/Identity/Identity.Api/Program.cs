using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using Identity;
using Identity.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceInfrastructure<IdentityEventMapper>(TransportType.RabbitMq, typeof(IdentityRoot).Assembly);

builder.AddIdentityModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseIdentityModules();

app.UseServiceDefaults();
app.UseServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Identity.Api
{
    public partial class Program { }
}
