using System.Security.Cryptography;
using System.Text;

namespace BuildingBlocks.MassTransit;

// Inbox key: the broker MessageId scoped to the receiving endpoint, so redeliveries of a message to the same
// queue are deduplicated while different consumers (queues) of the same message are tracked independently.
public static class InboxMessageId
{
    public static Guid Create(Guid messageId, Uri? inputAddress)
    {
        var endpoint = inputAddress?.AbsolutePath.Trim('/') ?? string.Empty;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{messageId:N}|{endpoint}"));

        return new Guid(hash.AsSpan(0, 16));
    }
}
