using System.Text.Json;
using Kart.Wishlist.Application.Features.EvaluatePriceDropAlert;
using Kart.Wishlist.Application.Features.MarkEntriesStaleOnProductDiscontinued;
using Kart.Shared.Messaging;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kart.Wishlist.Infrastructure.Messaging;

/// <summary>
/// Consumes <c>wishlist.product-events.queue</c> — bound to both <c>product.price.changed</c> and
/// <c>product.product.discontinued</c> (message-bus-manifest.json). Hand-rolled rather than
/// derived from <c>Kart.Shared.Messaging.RabbitMqConsumerHostedServiceBase</c>, because that base
/// only hands a consumer the raw message body, not the routing key this queue's two distinct
/// event types need to be told apart by — the same situation kart-product-service's own
/// <c>CatalogProjectionConsumerHostedService</c> resolves with its local
/// <see cref="RetryLadderDispatcher"/>, reused here verbatim in shape.
/// </summary>
public sealed class ProductEventsConsumerHostedService(
    IServiceScopeFactory scopeFactory,
    IConnectionFactory connectionFactory,
    MessageBusManifest manifest,
    ILogger<ProductEventsConsumerHostedService> logger) : BackgroundService
{
    private const string QueueName = "wishlist.product-events.queue";
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var connection = connectionFactory.CreateConnection();
                using var channel = connection.CreateModel();

                RabbitMqTopologyProvisioner.Declare(channel, manifest);
                channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);

                var queue = manifest.GetQueue(QueueName);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += async (_, delivery) => await OnMessageAsync(channel, queue, delivery, stoppingToken);

                channel.BasicConsume(QueueName, autoAck: false, consumer);
                logger.LogInformation("Product events consumer listening on {Queue}.", QueueName);

                await WaitWhileConnectedAsync(connection, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Product events consumer lost its RabbitMQ connection; reconnecting in {Delay}.", ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private static async Task WaitWhileConnectedAsync(IConnection connection, CancellationToken stoppingToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConnectionShutdown += (_, _) => tcs.TrySetResult();
        using var registration = stoppingToken.Register(() => tcs.TrySetCanceled(stoppingToken));
        await tcs.Task;
    }

    private async Task OnMessageAsync(IModel channel, QueueDefinition queue, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        try
        {
            var routingKey = RetryLadderDispatcher.GetEffectiveRoutingKey(delivery);

            using var scope = scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            switch (routingKey)
            {
                case "product.price.changed":
                {
                    var payload = JsonSerializer.Deserialize<ProductPriceChangedEventPayload>(delivery.Body.Span, SerializerOptions)
                        ?? throw new InvalidOperationException("ProductPriceChanged payload deserialized to null.");
                    await sender.Send(new EvaluatePriceDropAlertCommand(payload.Sku, payload.OldPrice, payload.NewPrice, payload.OccurredAt), cancellationToken);
                    break;
                }

                case "product.product.discontinued":
                {
                    var payload = JsonSerializer.Deserialize<ProductDiscontinuedEventPayload>(delivery.Body.Span, SerializerOptions)
                        ?? throw new InvalidOperationException("ProductDiscontinued payload deserialized to null.");
                    await sender.Send(new MarkEntriesStaleOnProductDiscontinuedCommand(payload.Sku, payload.DiscontinuedAt), cancellationToken);
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unrecognized routing key '{routingKey}' on {QueueName}.");
            }

            channel.BasicAck(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception exception)
        {
            RetryLadderDispatcher.HandleFailure(channel, delivery, queue, logger, exception);
        }
    }
}
