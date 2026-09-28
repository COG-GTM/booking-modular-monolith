using BuildingBlocks.Web;
using Identity;
using Identity.Extensions.Infrastructure;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceHost(typeof(IdentityRoot).Assembly, persistMessageConnectionName: "identity-persist-message");
builder.AddIdentityModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseIdentityModules();
app.UseServiceHost();
app.MapMinimalEndpoints();

app.Run();

namespace Identity.Api
{
    public partial class Program { }
}
