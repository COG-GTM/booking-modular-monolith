namespace Unit.Test.GrpcServer;

using System.Security.Claims;
using global::Passenger;
using BuildingBlocks.Constants;
using FluentAssertions;
using global::Passenger.GrpcServer.Services;
using global::Passenger.Passengers.Dtos;
using global::Passenger.Passengers.Features.GettingPassengerById.V1;
using Grpc.Core;
using Grpc.Core.Testing;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;
using PassengerType = global::Passenger.Passengers.Enums.PassengerType;
using QueryResult = global::Passenger.Passengers.Features.GettingPassengerById.V1.GetPassengerByIdResult;

public class PassengerGrpcServicesTests
{
    private static readonly Guid PassengerId = Guid.CreateVersion7();
    private static readonly Guid OwnerUserId = Guid.CreateVersion7();

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly PassengerGrpcServices _service;

    public PassengerGrpcServicesTests()
    {
        _mediator
            .Send(Arg.Is<GetPassengerById>(q => q.Id == PassengerId), Arg.Any<CancellationToken>())
            .Returns(
                new QueryResult(new PassengerDto(PassengerId, "Sam", "123456789", PassengerType.Male, 30), OwnerUserId)
            );

        _service = new PassengerGrpcServices(_mediator);
    }

    [Fact]
    public void should_require_api_scope_authorization_policy_on_the_grpc_service()
    {
        var authorize = typeof(PassengerGrpcServices)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .Should()
            .ContainSingle()
            .Subject;

        authorize.Policy.Should().Be("ApiScope");
    }

    [Fact]
    public async Task should_return_passenger_to_its_owner()
    {
        var context = CreateContext(
            new Claim("sub", OwnerUserId.ToString()),
            new Claim(ClaimTypes.Role, IdentityConstant.Role.User)
        );

        var result = await _service.GetById(new GetByIdRequest { Id = PassengerId.ToString() }, context);

        result.PassengerDto.Id.Should().Be(PassengerId.ToString());
        result.PassengerDto.Name.Should().Be("Sam");
        result.PassengerDto.PassportNumber.Should().Be("123456789");
        result.PassengerDto.Age.Should().Be(30);
    }

    [Fact]
    public async Task should_return_any_passenger_to_an_admin()
    {
        var context = CreateContext(
            new Claim("sub", Guid.CreateVersion7().ToString()),
            new Claim(ClaimTypes.Role, IdentityConstant.Role.Admin)
        );

        var result = await _service.GetById(new GetByIdRequest { Id = PassengerId.ToString() }, context);

        result.PassengerDto.Id.Should().Be(PassengerId.ToString());
    }

    [Fact]
    public async Task should_throw_permission_denied_when_caller_is_not_the_owner()
    {
        var context = CreateContext(
            new Claim("sub", Guid.CreateVersion7().ToString()),
            new Claim(ClaimTypes.Role, IdentityConstant.Role.User)
        );

        var act = async () => await _service.GetById(new GetByIdRequest { Id = PassengerId.ToString() }, context);

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        exception.StatusCode.Should().Be(StatusCode.PermissionDenied);
        exception.Status.Detail.Should().Contain("not allowed to read this passenger");
    }

    [Fact]
    public async Task should_throw_permission_denied_when_caller_has_no_subject_claim()
    {
        var context = CreateContext(new Claim(ClaimTypes.Role, IdentityConstant.Role.User));

        var act = async () => await _service.GetById(new GetByIdRequest { Id = PassengerId.ToString() }, context);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task should_not_leak_legacy_passenger_without_owner_to_non_admin()
    {
        var legacyPassengerId = Guid.CreateVersion7();
        _mediator
            .Send(Arg.Is<GetPassengerById>(q => q.Id == legacyPassengerId), Arg.Any<CancellationToken>())
            .Returns(
                new QueryResult(
                    new PassengerDto(legacyPassengerId, "Legacy", "987654321", PassengerType.Female, 40),
                    Guid.Empty
                )
            );
        var context = CreateContext(
            new Claim("sub", Guid.Empty.ToString()),
            new Claim(ClaimTypes.Role, IdentityConstant.Role.User)
        );

        var act = async () => await _service.GetById(new GetByIdRequest { Id = legacyPassengerId.ToString() }, context);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    private static ServerCallContext CreateContext(params Claim[] claims)
    {
        var context = TestServerCallContext.Create(
            "method",
            "host",
            DateTime.UtcNow.AddMinutes(1),
            new Metadata(),
            CancellationToken.None,
            "peer",
            null,
            null,
            _ => Task.CompletedTask,
            () => new WriteOptions(),
            _ => { }
        );

        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };
        context.UserState["__HttpContext"] = httpContext;

        return context;
    }
}
