using BuildingBlocks.Web;
using Identity.Extensions.Infrastructure;
using IdentityApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceInfrastructure();

builder.AddIdentityModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseIdentityModules();

app.UseServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace IdentityApi
{
    public partial class Program { }
}
