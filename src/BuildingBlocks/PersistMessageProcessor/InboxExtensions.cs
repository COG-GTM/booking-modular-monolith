using MassTransit;

namespace BuildingBlocks.PersistMessageProcessor;

public static class InboxExtensions
{
    // The filter must bind the receiving module's processor. Call before ConfigureEndpoints.
    public static void UsePersistMessageInbox(
        this IConsumePipeConfigurator configurator,
        IRegistrationContext context,
        Type moduleFilterType
    )
    {
        configurator.UseConsumeFilter(moduleFilterType, context);
    }
}
