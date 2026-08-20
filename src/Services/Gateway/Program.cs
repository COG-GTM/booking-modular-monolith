var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseServiceDefaults();

app.MapReverseProxy();

app.Run();

namespace GatewayService
{
    public partial class Program { }
}
