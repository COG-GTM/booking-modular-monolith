using Api.Extensions;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Web;
using Flight.Extensions.Infrastructure;
using Identity.Extensions.Infrastructure;
using Passenger.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();

builder.AddFlightModules();
builder.AddIdentityModules();
builder.AddPassengerModules();
builder.AddBookingModules();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    await next();
});

// ref: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-7.0#routing-basics
app.UseAuthentication();
app.UseAuthorization();

app.UseFlightModules();
app.UseIdentityModules();
app.UsePassengerModules();
app.UseBookingModules();

app.UserSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Api
{
    public partial class Program { }
}