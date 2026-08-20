using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Core;
using BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure(services =>
    services.AddScoped<IEventMapper>(sp => sp.GetRequiredService<BookingEventMapper>())
);

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UseSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Booking.Api
{
    public partial class Program { }
}
