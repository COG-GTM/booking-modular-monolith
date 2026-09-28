using BuildingBlocks.Web;
using Figgle.Fonts;

var builder = WebApplication.CreateBuilder(args);

var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

builder.AddServiceDefaults();

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseServiceDefaults();

app.UseCorrelationId();

app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));

app.MapReverseProxy();

app.Run();

namespace Api
{
    public partial class Program { }
}
