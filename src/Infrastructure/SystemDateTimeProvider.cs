using Kart.Wishlist.Application.Common.Interfaces;

namespace Kart.Wishlist.Infrastructure;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
