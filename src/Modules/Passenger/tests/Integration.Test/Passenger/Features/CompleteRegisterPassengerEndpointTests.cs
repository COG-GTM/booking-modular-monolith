using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BuildingBlocks.Web;
using FluentAssertions;
using Mapster;
using MapsterMapper;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Integration.Test.Passenger.Features;

using PassengerFeature = global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using PassengerModule = global::Passenger;

public class CompleteRegisterPassengerEndpointTests
{
    private const string AuthenticationScheme = "Test";
    private static readonly IMapper Mapper = CreateMapper();

    [Fact]
    public async Task Should_forbid_when_authenticated_user_has_no_id_claim()
    {
        var mediator = Substitute.For<IMediator>();
        await using var app = await CreateAppAsync(mediator);
        using var client = app.GetTestClient();

        using var response = await PostAsync(client, "name=test", CreateRequest());

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        await mediator
            .DidNotReceive()
            .Send(Arg.Any<PassengerFeature.CompleteRegisterPassenger>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_forbid_when_sub_claim_is_not_a_guid()
    {
        var mediator = Substitute.For<IMediator>();
        await using var app = await CreateAppAsync(mediator);
        using var client = app.GetTestClient();

        using var response = await PostAsync(client, "sub=not-a-guid", CreateRequest());

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
        await mediator
            .DidNotReceive()
            .Send(Arg.Any<PassengerFeature.CompleteRegisterPassenger>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_send_user_id_and_request_values_and_return_passenger_dto()
    {
        var userId = Guid.NewGuid();
        var expectedDto = new PassengerModule.Passengers.Dtos.PassengerDto(
            Guid.NewGuid(),
            "Test Passenger",
            "P-123",
            PassengerModule.Passengers.Enums.PassengerType.Female,
            35
        );
        PassengerFeature.CompleteRegisterPassenger? capturedCommand = null;
        var mediator = Substitute.For<IMediator>();
        mediator
            .Send(Arg.Any<PassengerFeature.CompleteRegisterPassenger>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedCommand = callInfo.Arg<PassengerFeature.CompleteRegisterPassenger>();
                return Task.FromResult(new PassengerFeature.CompleteRegisterPassengerResult(expectedDto));
            });

        await using var app = await CreateAppAsync(mediator);
        using var client = app.GetTestClient();
        using var response = await PostAsync(
            client,
            $"sub={userId}",
            new
            {
                passportNumber = "P-123",
                passengerType = PassengerModule.Passengers.Enums.PassengerType.Male,
                age = 42,
            }
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedCommand.Should().NotBeNull();
        capturedCommand!.UserId.Should().Be(userId);
        capturedCommand.PassportNumber.Should().Be("P-123");
        capturedCommand.PassengerType.Should().Be(PassengerModule.Passengers.Enums.PassengerType.Male);
        capturedCommand.Age.Should().Be(42);

        using var responseJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var passengerDto = responseJson.RootElement.GetProperty("passengerDto");
        passengerDto.GetProperty("id").GetGuid().Should().Be(expectedDto.Id);
        passengerDto.GetProperty("name").GetString().Should().Be(expectedDto.Name);
        passengerDto.GetProperty("passportNumber").GetString().Should().Be(expectedDto.PassportNumber);
        passengerDto.GetProperty("passengerType").GetInt32().Should().Be((int)expectedDto.PassengerType);
        passengerDto.GetProperty("age").GetInt32().Should().Be(expectedDto.Age);
    }

    [Fact]
    public async Task Should_use_name_identifier_when_sub_is_absent()
    {
        var userId = Guid.NewGuid();
        var mediator = CreateMediator();
        await using var app = await CreateAppAsync(mediator);
        using var client = app.GetTestClient();

        using var response = await PostAsync(client, $"{ClaimTypes.NameIdentifier}={userId}", CreateRequest());

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await mediator
            .Received(1)
            .Send(
                Arg.Is<PassengerFeature.CompleteRegisterPassenger>(command => command.UserId == userId),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Should_prefer_sub_when_both_user_id_claims_are_present()
    {
        var subUserId = Guid.NewGuid();
        var nameIdentifier = Guid.NewGuid();
        var mediator = CreateMediator();
        await using var app = await CreateAppAsync(mediator);
        using var client = app.GetTestClient();

        using var response = await PostAsync(
            client,
            $"sub={subUserId};{ClaimTypes.NameIdentifier}={nameIdentifier}",
            CreateRequest()
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await mediator
            .Received(1)
            .Send(
                Arg.Is<PassengerFeature.CompleteRegisterPassenger>(command => command.UserId == subUserId),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Should_ignore_user_id_supplied_in_request_body()
    {
        var callerId = Guid.NewGuid();
        var bodyUserId = Guid.NewGuid();
        var mediator = CreateMediator();
        await using var app = await CreateAppAsync(mediator);
        using var client = app.GetTestClient();

        using var response = await PostAsync(
            client,
            $"sub={callerId}",
            new
            {
                passportNumber = "P-123",
                passengerType = PassengerModule.Passengers.Enums.PassengerType.Male,
                age = 42,
                userId = bodyUserId,
            }
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await mediator
            .Received(1)
            .Send(
                Arg.Is<PassengerFeature.CompleteRegisterPassenger>(command => command.UserId == callerId),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Should_return_unauthorized_when_authentication_header_is_absent()
    {
        var mediator = Substitute.For<IMediator>();
        await using var app = await CreateAppAsync(mediator);
        using var client = app.GetTestClient();

        using var response = await PostAsync(client, null, CreateRequest());

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        await mediator
            .DidNotReceive()
            .Send(Arg.Any<PassengerFeature.CompleteRegisterPassenger>(), Arg.Any<CancellationToken>());
    }

    private static object CreateRequest() =>
        new
        {
            passportNumber = "P-123",
            passengerType = PassengerModule.Passengers.Enums.PassengerType.Male,
            age = 42,
        };

    private static IMediator CreateMediator()
    {
        var expectedDto = new PassengerModule.Passengers.Dtos.PassengerDto(
            Guid.NewGuid(),
            "Test Passenger",
            "P-123",
            PassengerModule.Passengers.Enums.PassengerType.Male,
            42
        );
        var mediator = Substitute.For<IMediator>();
        mediator
            .Send(Arg.Any<PassengerFeature.CompleteRegisterPassenger>(), Arg.Any<CancellationToken>())
            .Returns(new PassengerFeature.CompleteRegisterPassengerResult(expectedDto));

        return mediator;
    }

    private static IMapper CreateMapper()
    {
        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(typeof(PassengerModule.Passengers.Features.PassengerMappings).Assembly);
        return new Mapper(config);
    }

    private static async Task<WebApplication> CreateAppAsync(IMediator mediator)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder
            .Services.AddAuthentication(AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(AuthenticationScheme, _ => { });
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("ApiScope", policy => policy.RequireAuthenticatedUser())
        );
        builder.Services.AddCustomVersioning();
        builder.Services.AddSingleton(Mapper);
        builder.Services.AddSingleton(mediator);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        new PassengerFeature.CompleteRegisterPassengerEndpoint().MapEndpoint(app);
        await app.StartAsync();

        return app;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string? claims, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/passenger/complete-registration")
        {
            Content = JsonContent.Create(body),
        };
        if (claims is not null)
        {
            request.Headers.Add("X-Test-Claims", claims);
        }

        return await client.SendAsync(request);
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder
        )
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Claims", out var claimHeader))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = claimHeader
                .ToString()
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(value =>
                {
                    var separator = value.IndexOf('=', StringComparison.Ordinal);
                    return new Claim(value[..separator], value[(separator + 1)..]);
                });
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
