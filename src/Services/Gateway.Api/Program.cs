using BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseServiceDefaults();

var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));
app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));

app.MapReverseProxy();

app.Run();

namespace GatewayApi
{
    public partial class Program { }
}
