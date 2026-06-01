using BuildingBlocks.Core;
using BuildingBlocks.Exception;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using Passenger;
using Passenger.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMicroserviceInfrastructure();

builder.AddPersistMessageProcessor(connectionName: "passenger-persist-message");

builder.Services.AddCustomMassTransit(builder.Environment, TransportType.RabbitMq, typeof(PassengerRoot).Assembly);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcExceptionInterceptor>();
});

builder.Services.AddScoped<IEventMapper, PassengerEventMapper>();

builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UseServiceDefaults();
app.UseMicroserviceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Passenger.Api
{
    public partial class Program { }
}
