namespace Kart.Wishlist.Application.Common.Interfaces;

/// <summary>Testability seam for "now" — every handler that stamps a timestamp reads this instead
/// of <c>DateTimeOffset.UtcNow</c> directly (kart-cart-service precedent).</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
