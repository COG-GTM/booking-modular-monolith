using BuildingBlocks.Web;
using Identity;
using Identity.Extensions.Infrastructure;
using Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();
builder.AddModuleEventMapper<IdentityEventMapper>();

builder.AddIdentityModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseIdentityModules();

app.UseSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Identity.Api
{
    public partial class Program { }
}
