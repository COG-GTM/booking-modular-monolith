using MassTransit;

namespace BuildingBlocks.PersistMessageProcessor;

public static class InboxExtensions
{
    // Adds the persist-message inbox to every consumer on the bus. Call before ConfigureEndpoints.
    public static void UsePersistMessageInbox(this IConsumePipeConfigurator configurator, IRegistrationContext context)
    {
        configurator.UseConsumeFilter(typeof(BuildingBlocks.MassTransit.ConsumeFilter<>), context);
    }
}
