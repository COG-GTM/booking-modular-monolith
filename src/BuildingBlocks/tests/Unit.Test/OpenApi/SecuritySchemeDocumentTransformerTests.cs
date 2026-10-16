using FluentAssertions;
using Microsoft.OpenApi;
using Xunit;

namespace BuildingBlocks.Unit.Test.OpenApiTests;

public class SecuritySchemeDocumentTransformerTests
{
    [Fact]
    public async Task empty_document_gets_bearer_and_api_key_security_schemes()
    {
        var document = new OpenApiDocument();
        var sut = new SecuritySchemeDocumentTransformer();

        await sut.TransformAsync(document, context: null!, CancellationToken.None);

        var schemes = document.Components!.SecuritySchemes;
        schemes.Should().ContainKeys("Bearer", "ApiKey");

        var bearer = schemes["Bearer"].Should().BeOfType<OpenApiSecurityScheme>().Which;
        bearer.Type.Should().Be(SecuritySchemeType.Http);
        bearer.Scheme.Should().Be("bearer");
        bearer.BearerFormat.Should().Be("JWT");
        bearer.Name.Should().Be("Authorization");
        bearer.In.Should().Be(ParameterLocation.Header);

        var apiKey = schemes["ApiKey"].Should().BeOfType<OpenApiSecurityScheme>().Which;
        apiKey.Type.Should().Be(SecuritySchemeType.ApiKey);
        apiKey.Name.Should().Be("X-API-KEY");
        apiKey.In.Should().Be(ParameterLocation.Header);
    }

    [Fact]
    public async Task pre_existing_bearer_scheme_is_preserved()
    {
        var existing = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "basic",
        };
        var document = new OpenApiDocument
        {
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme> { ["Bearer"] = existing },
            },
        };
        var sut = new SecuritySchemeDocumentTransformer();

        await sut.TransformAsync(document, context: null!, CancellationToken.None);

        document.Components.SecuritySchemes["Bearer"].Should().BeSameAs(existing);
        document.Components.SecuritySchemes.Should().ContainKey("ApiKey");
    }
}
