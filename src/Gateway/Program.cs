var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapGet("/", x => x.Response.WriteAsync("Booking-Gateway"));
app.MapReverseProxy();

app.Run();

namespace Gateway
{
    public partial class Program { }
}
