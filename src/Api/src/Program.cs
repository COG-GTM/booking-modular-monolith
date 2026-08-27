using Api.Extensions;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Web;
using Flight.Extensions.Infrastructure;
using Identity.Extensions.Infrastructure;
using Passenger.Extensions.Infrastructure;
using Shared.ServiceHost;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();

builder.Services.AddApiEventMappers();

builder.AddFlightModules();
builder.AddIdentityModules();
builder.AddPassengerModules();
builder.AddBookingModules();

var app = builder.Build();

// ref: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-7.0#routing-basics
app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();
app.UseIdentityModules();
app.UsePassengerModules();
app.UseBookingModules();

app.UseSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Api
{
    public partial class Program { }
}