using BuildingBlocks.Web;
using Flight.Extensions.Infrastructure;
using Flight.Host.Extensions;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
builder.AddFlightHostInfrastructure();
builder.AddFlightModules();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseFlightModules();
app.UseFlightHostInfrastructure();
app.MapMinimalEndpoints();
app.Run();

public partial class Program { }
