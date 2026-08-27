using BuildingBlocks.Web;
using Identity;
using Identity.Extensions.Infrastructure;
using Shared.ServiceHost;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure(typeof(IdentityRoot).Assembly);
builder.Services.AddEventMapper<IdentityEventMapper>();
builder.AddIdentityModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseIdentityModules();
app.UseSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace IdentityService
{
    public partial class Program { }
}
