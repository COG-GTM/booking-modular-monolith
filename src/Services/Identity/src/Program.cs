using BuildingBlocks.Web;
using Identity.Extensions.Infrastructure;
using Identity.Host.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.AddIdentityHostInfrastructure();
builder.AddIdentityModules();
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseIdentityModules();
app.UseIdentityHostInfrastructure();
app.MapMinimalEndpoints();
app.Run();

namespace Identity.Host
{
    public partial class Program { }
}
