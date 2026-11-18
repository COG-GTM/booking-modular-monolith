using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Security;

public class TokenEndpointRateLimitTests : IdentityIntegrationTestBase
{
    public TokenEndpointRateLimitTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task token_endpoint_should_return_429_after_permit_limit_is_exceeded()
    {
        var settings = Fixture.ServiceProvider.GetRequiredService<TokenEndpointRateLimitOptions>();
        var client = Fixture.HttpClient;

        HttpStatusCode lastStatus = default;

        for (var i = 0; i <= settings.PermitLimit; i++)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "rate-limit-test-unknown-client",
                ["client_secret"] = "invalid",
                ["username"] = "nobody",
                ["password"] = "invalid",
            });

            if (i < settings.PermitLimit)
            {
                // TestFixture replaces IHttpContextAccessor with a mock, so IdentityServer itself can fail
                // on permitted requests here. Only the limiter's decision matters for this test.
                try
                {
                    using var permitted = await client.PostAsync("/connect/token", content);
                    permitted.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
                }
                catch (NullReferenceException)
                {
                }

                continue;
            }

            using var response = await client.PostAsync("/connect/token", content);
            lastStatus = response.StatusCode;
        }

        lastStatus.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
