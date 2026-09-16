using AutoBogus;

namespace Unit.Test.Fakes;

using global::Passenger.Passengers.Features.GettingPassengerById.V1;
using MassTransit;

public sealed class FakeGetPassengerByIdQuery : AutoFaker<GetPassengerById>
{
    public FakeGetPassengerByIdQuery()
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
    }
}
