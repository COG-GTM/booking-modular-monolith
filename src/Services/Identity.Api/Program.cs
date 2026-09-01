using BuildingBlocks.Core;
using BuildingBlocks.Web;
using Identity;
using Identity.Extensions.Infrastructure;
using SharedServiceExtensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedServiceInfrastructure(typeof(IdentityRoot).Assembly);
builder.Services.AddScoped<IEventMapper, IdentityEventMapper>();

builder.AddIdentityModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseIdentityModules();

app.UseSharedServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Identity.Api
{
    public partial class Program { }
}
