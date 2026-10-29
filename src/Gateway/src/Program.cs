using Gateway.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddGateway();

var app = builder.Build();

app.UseGateway();

app.Run();

namespace Gateway
{
    public partial class Program { }
}
