using BuildingBlocks.Web;
using Identity.Extensions.Infrastructure;
using Identity.Host.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddIdentityServiceInfrastructure();
builder.AddIdentityModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseIdentityModules();

app.UseIdentityServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Identity.Host
{
    public partial class Program { }
}
