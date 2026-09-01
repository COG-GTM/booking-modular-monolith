using BuildingBlocks.Core;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using Identity;
using Identity.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMicroserviceInfrastructure();

builder.AddPersistMessageProcessor(connectionName: "identity-persist-message");

builder.Services.AddCustomMassTransit(builder.Environment, TransportType.RabbitMq, typeof(IdentityRoot).Assembly);

builder.Services.AddScoped<IEventMapper, IdentityEventMapper>();

builder.AddIdentityModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseIdentityModules();

app.UseServiceDefaults();
app.UseMicroserviceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Identity.Api
{
    public partial class Program { }
}
