using BuildingBlocks.Web;
using Passenger.Extensions.Infrastructure;
using Passenger.Host.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddPassengerHost();

builder.AddPassengerModules();

var app = builder.Build();

// ref: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-7.0#routing-basics
app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UsePassengerHost();
app.MapMinimalEndpoints();

app.Run();

namespace Passenger.Host
{
    public partial class Program { }
}
