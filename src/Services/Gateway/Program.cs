var builder = WebApplication.CreateBuilder(args);

var reverseProxyConfiguration = builder.Configuration.GetSection("ReverseProxy");

builder.Services.AddReverseProxy().LoadFromConfig(reverseProxyConfiguration);

var app = builder.Build();

app.MapReverseProxy();

app.Run();

namespace GatewayService
{
    public partial class Program { }
}
