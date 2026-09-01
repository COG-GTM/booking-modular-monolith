using BuildingBlocks.Core;
using BuildingBlocks.Exception;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using Flight;
using Flight.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMicroserviceInfrastructure();

builder.AddPersistMessageProcessor(connectionName: "flight-persist-message");

builder.Services.AddCustomMassTransit(builder.Environment, TransportType.RabbitMq, typeof(FlightRoot).Assembly);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcExceptionInterceptor>();
});

builder.Services.AddScoped<IEventMapper, FlightEventMapper>();

builder.AddFlightModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();

app.UseServiceDefaults();
app.UseMicroserviceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Flight.Api
{
    public partial class Program { }
}
