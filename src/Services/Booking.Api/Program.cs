using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Core;
using BuildingBlocks.Web;
using SharedServiceExtensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedServiceInfrastructure(typeof(BookingRoot).Assembly);
builder.Services.AddScoped<IEventMapper, BookingEventMapper>();

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UseSharedServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Booking.Api
{
    public partial class Program { }
}
