using Booking.Extensions.Infrastructure;
using BookingApi.Extensions;
using BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceInfrastructure();

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UseServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace BookingApi
{
    public partial class Program { }
}
