using BuildingBlocks.Web;
using Booking.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UserSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Booking.Api
{
    public partial class Program { }
}
