using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var jwtSection = builder.Configuration.GetSection("Jwt");

builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwtSection["Authority"];
        options.Audience = jwtSection["Audience"];
        options.RequireHttpsMetadata = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = [jwtSection["Authority"]],
            ValidateAudience = true,
            ValidAudiences = [jwtSection["Audience"]],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(2),
            ValidateIssuerSigningKey = true,
        };

        options.MapInboundClaims = false;
    });

builder.Services.AddAuthorization();

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();

app.MapGet("/", () => "API Gateway is running");

app.Run();

namespace ApiGateway
{
    public partial class Program { }
}
