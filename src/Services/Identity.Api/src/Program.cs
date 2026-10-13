using BuildingBlocks.Web;
using Identity;
using Identity.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceHostInfrastructure<IdentityEventMapper>(typeof(IdentityRoot).Assembly);
builder.AddIdentityModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseIdentityModules();

app.UseServiceHostInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Identity.Api
{
    public partial class Program { }
}
