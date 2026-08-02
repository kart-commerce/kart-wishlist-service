using System.Text;
using Kart.Shared.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kart.Wishlist.Infrastructure.Messaging;

/// <summary>
/// Shared retry/DLQ mechanics for this service's one hand-rolled, multi-routing-key consumer
/// (<see cref="ProductEventsConsumerHostedService"/>) — the shared
/// <c>Kart.Shared.Messaging.RabbitMqConsumerHostedServiceBase</c> doesn't fit there because it
/// hands a consumer only the raw message body, not the routing key a single queue bound to
/// multiple routing keys needs to distinguish <c>ProductPriceChanged</c> from
/// <c>ProductDiscontinued</c> — the same "hand-roll the mechanics locally" precedent
/// <c>kart-product-service</c>'s own <c>RetryLadderDispatcher</c> establishes for its identical
/// multi-event-type-per-queue situation. <see cref="UserEventsConsumerHostedService"/> (a single
/// routing key) uses the shared base directly instead.
/// </summary>
public static class RetryLadderDispatcher
{
    private const string RetryCountHeader = "x-wishlist-retry-count";

    /// <summary>A TTL-ladder retry bounces a message through the default exchange with the
    /// routing key set to the retry-tier queue's own name, so by the time RabbitMQ redelivers it
    /// to the main queue, <see cref="BasicDeliverEventArgs.RoutingKey"/> no longer reflects the
    /// routing key it originally arrived with — this header preserves it.</summary>
    private const string OriginalRoutingKeyHeader = "x-wishlist-original-routing-key";

    public static string GetEffectiveRoutingKey(BasicDeliverEventArgs delivery)
    {
        if (delivery.BasicProperties.Headers is not null
            && delivery.BasicProperties.Headers.TryGetValue(OriginalRoutingKeyHeader, out var value)
            && value is byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return delivery.RoutingKey;
    }

    public static void HandleFailure(IModel channel, BasicDeliverEventArgs delivery, QueueDefinition queue, ILogger logger, Exception exception)
    {
        channel.BasicAck(delivery.DeliveryTag, multiple: false);

        var retryCount = GetRetryCount(delivery.BasicProperties);
        var tiers = queue.RetryLadder!.Tiers;

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = delivery.BasicProperties.ContentType;
        properties.Headers = new Dictionary<string, object>
        {
            [RetryCountHeader] = retryCount + 1,
            [OriginalRoutingKeyHeader] = GetEffectiveRoutingKey(delivery),
        };

        if (retryCount < tiers.Count)
        {
            var tier = tiers[retryCount];
            channel.BasicPublish(exchange: string.Empty, routingKey: tier.Name, basicProperties: properties, body: delivery.Body.ToArray());
            logger.LogWarning(exception, "Retrying message from {Queue} via tier {Tier} (attempt {Attempt})", queue.Name, tier.Name, retryCount + 1);
        }
        else
        {
            channel.BasicPublish(exchange: queue.DeadLetter!.Exchange, routingKey: queue.DeadLetter.RoutingKey, basicProperties: properties, body: delivery.Body.ToArray());
            logger.LogCritical(exception, "Exhausted retry ladder for {Queue} - routed to dead-letter queue via {Exchange}/{RoutingKey}", queue.Name, queue.DeadLetter.Exchange, queue.DeadLetter.RoutingKey);
        }
    }

    private static int GetRetryCount(IBasicProperties properties)
    {
        if (properties.Headers is not null && properties.Headers.TryGetValue(RetryCountHeader, out var value))
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes => int.Parse(Encoding.UTF8.GetString(bytes)),
                _ => 0,
            };
        }

        return 0;
    }
}
