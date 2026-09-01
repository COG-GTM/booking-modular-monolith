using BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.UseServiceDefaults();

app.MapReverseProxy();

app.Run();

namespace Gateway
{
    public partial class Program { }
}
