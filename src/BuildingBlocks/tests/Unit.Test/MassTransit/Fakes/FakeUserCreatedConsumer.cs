namespace Unit.Test.MassTransit.Fakes;

using global::MassTransit;

public record FakeUserCreated(Guid Id);

public class FakeUserCreatedConsumer : IConsumer<FakeUserCreated>
{
    public Task Consume(ConsumeContext<FakeUserCreated> context) => Task.CompletedTask;
}
