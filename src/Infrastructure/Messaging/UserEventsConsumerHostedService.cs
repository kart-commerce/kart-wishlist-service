using System.Text.Json;
using System.Text.Json.Serialization;
using Kart.Wishlist.Application.Features.EraseUserWishlistDataOnUserDataErased;
using Kart.Shared.Messaging;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Kart.Wishlist.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>kart-user-service</c>'s <c>UserDataErased</c> (<c>user.exchange</c> /
/// <c>user.data-erased</c>) — WL-8's trigger (ADR-0016). Compliance-critical tier: 5x retry,
/// exponential backoff, on-call paging on DLQ exhaustion (event-contract.md). A single routing
/// key on this queue, so — unlike <see cref="ProductEventsConsumerHostedService"/> — the shared
/// <c>Kart.Shared.Messaging.RabbitMqConsumerHostedServiceBase</c> fits directly
/// (kart-cart-service's own <c>UserDataErasedConsumerHostedService</c> precedent).
/// </summary>
public sealed class UserEventsConsumerHostedService(
    IConnectionFactory connectionFactory,
    MessageBusManifest manifest,
    IServiceScopeFactory scopeFactory,
    ILogger<UserEventsConsumerHostedService> logger)
    : RabbitMqConsumerHostedServiceBase(connectionFactory, manifest, scopeFactory, logger, "x-wishlist-service-retry-count")
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override string QueueName => "wishlist.user-events.queue";

    protected override async Task ProcessAsync(ReadOnlyMemory<byte> body, IServiceProvider scopedProvider, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<UserDataErasedEventPayload>(body.Span, SerializerOptions)
            ?? throw new InvalidOperationException("UserDataErased payload deserialized to null.");

        var sender = scopedProvider.GetRequiredService<ISender>();
        await sender.Send(new EraseUserWishlistDataOnUserDataErasedCommand(payload.UserId), cancellationToken);
    }

    private sealed record UserDataErasedEventPayload(
        [property: JsonPropertyName("userId")] Guid UserId,
        [property: JsonPropertyName("erasedAt")] DateTimeOffset ErasedAt);
}
