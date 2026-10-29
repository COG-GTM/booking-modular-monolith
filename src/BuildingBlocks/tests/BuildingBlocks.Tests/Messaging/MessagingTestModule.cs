using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Tests.Messaging;

public sealed class MessagingTestModule;

public class MessagingInboxFilter<T>(
    IPersistMessageProcessor<MessagingTestModule> processor,
    ILogger<ConsumeFilter<MessagingTestModule, T>> logger
) : ConsumeFilter<MessagingTestModule, T>(processor, logger)
    where T : class;
