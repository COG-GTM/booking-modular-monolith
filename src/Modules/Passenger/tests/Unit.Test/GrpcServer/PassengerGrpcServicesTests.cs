using FluentAssertions;
using Mapster;
using MediatR;
using NSubstitute;
using Passenger;
using Passenger.Passengers.Dtos;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;
using GetByIdRequest = Contracts.Grpc.Passenger.GetByIdRequest;

namespace Unit.Test.GrpcServer;

public class PassengerGrpcServicesTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly global::Passenger.GrpcServer.Services.PassengerGrpcServices _service;

    public PassengerGrpcServicesTests()
    {
        TypeAdapterConfig.GlobalSettings.Scan(typeof(PassengerRoot).Assembly);
        _service = new global::Passenger.GrpcServer.Services.PassengerGrpcServices(_mediator);
    }

    [Fact]
    public async Task Get_by_id_maps_result_and_sends_the_parsed_guid_to_mediator()
    {
        var id = Guid.NewGuid();
        _mediator
            .Send(Arg.Any<GetPassengerById>(), Arg.Any<CancellationToken>())
            .Returns(new GetPassengerByIdResult(new PassengerDto(id, "Sam", "123456789", PassengerType.Male, 36)));

        var result = await _service.GetById(new GetByIdRequest { Id = id.ToString() }, null!);

        result.PassengerDto.Id.Should().Be(id.ToString());
        result.PassengerDto.Name.Should().Be("Sam");
        result.PassengerDto.PassportNumber.Should().Be("123456789");
        result.PassengerDto.PassengerType.ToString().Should().Be("Male");
        result.PassengerDto.Age.Should().Be(36);
        await _mediator
            .Received(1)
            .Send(Arg.Is<GetPassengerById>(query => query.Id == id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_by_id_throws_for_an_invalid_guid()
    {
        await FluentActions
            .Invoking(() => _service.GetById(new GetByIdRequest { Id = "not-a-guid" }, null!))
            .Should()
            .ThrowAsync<FormatException>();
    }

    [Fact]
    public async Task Get_by_id_propagates_a_missing_passenger_exception()
    {
        _mediator
            .Send(Arg.Any<GetPassengerById>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetPassengerByIdResult>>(_ => throw new PassengerNotFoundException());

        await FluentActions
            .Invoking(() => _service.GetById(new GetByIdRequest { Id = Guid.NewGuid().ToString() }, null!))
            .Should()
            .ThrowAsync<PassengerNotFoundException>();
    }
}
