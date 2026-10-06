using Booking.Host.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddBookingHost();

var app = builder.Build();

app.UseBookingHost();

app.Run();

namespace Booking.Host
{
    public partial class Program;
}
