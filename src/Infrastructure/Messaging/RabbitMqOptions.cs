namespace Kart.Wishlist.Infrastructure.Messaging;

/// <summary>Binds the <c>"RabbitMq"</c> config section — the connection details
/// <c>contracts/message-bus-manifest.json</c> itself doesn't describe.</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    /// <summary>Dedicated non-guest broker credentials — RabbitMQ's default "guest" user only
    /// works over a literal loopback connection.</summary>
    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string ManifestPath { get; set; } = "message-bus-manifest.json";
}
