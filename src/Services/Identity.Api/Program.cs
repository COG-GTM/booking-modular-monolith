using BuildingBlocks.Core;
using BuildingBlocks.Web;
using Identity;
using Identity.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure(services =>
    services.AddScoped<IEventMapper>(sp => sp.GetRequiredService<IdentityEventMapper>())
);

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
