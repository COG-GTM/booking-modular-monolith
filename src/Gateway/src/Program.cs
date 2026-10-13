using Gateway.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddGatewayInfrastructure();

var app = builder.Build();

app.UseGatewayInfrastructure();

app.Run();

namespace Gateway
{
    public partial class Program { }
}
