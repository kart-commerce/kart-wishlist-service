using Kart.Wishlist.Domain.Entities;
using Kart.Wishlist.Domain.Enums;

namespace Kart.Wishlist.UnitTests.Domain;

public sealed class WishlistEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_sets_active_status_and_reference_price_at_add_time()
    {
        var entry = WishlistEntry.Create(Guid.NewGuid(), "sku-1", 100m, Now, "user-1");

        Assert.Equal(WishlistEntryStatus.Active, entry.Status);
        Assert.Equal(100m, entry.ReferencePrice);
        Assert.Null(entry.LastAlertedAt);
        Assert.Equal(Now, entry.AddedAt);
    }

    [Theory]
    [InlineData(95.01, false)] // 4.99% drop - not alert-worthy
    [InlineData(95.00, true)]  // exactly 5% drop - alert-worthy
    [InlineData(50.00, true)]  // well past threshold
    [InlineData(100.00, false)] // no drop at all
    [InlineData(120.00, false)] // price increase
    public void IsAlertWorthy_requires_at_least_5_percent_drop_from_reference_price(decimal newPrice, bool expected)
    {
        var entry = WishlistEntry.Create(Guid.NewGuid(), "sku-1", 100m, Now, "user-1");

        Assert.Equal(expected, entry.IsAlertWorthy(newPrice));
    }

    [Fact]
    public void IsCooldownActive_is_false_when_never_alerted()
    {
        var entry = WishlistEntry.Create(Guid.NewGuid(), "sku-1", 100m, Now, "user-1");

        Assert.False(entry.IsCooldownActive(Now));
    }

    [Fact]
    public void IsCooldownActive_is_true_within_24_hours_of_last_alert()
    {
        var entry = WishlistEntry.Create(Guid.NewGuid(), "sku-1", 100m, Now, "user-1");
        entry.ResetReferencePriceAfterAlert(90m, Now, "system:wishlist-digest-flush");

        Assert.True(entry.IsCooldownActive(Now.AddHours(23)));
        Assert.False(entry.IsCooldownActive(Now.AddHours(24).AddSeconds(1)));
    }

    [Fact]
    public void ResetReferencePriceAfterAlert_updates_baseline_and_last_alerted_at()
    {
        var entry = WishlistEntry.Create(Guid.NewGuid(), "sku-1", 100m, Now, "user-1");
        var alertTime = Now.AddMinutes(30);

        entry.ResetReferencePriceAfterAlert(90m, alertTime, "system:wishlist-digest-flush");

        Assert.Equal(90m, entry.ReferencePrice);
        Assert.Equal(alertTime, entry.LastAlertedAt);
        Assert.Equal("system:wishlist-digest-flush", entry.UpdatedBy);
    }

    [Fact]
    public void MarkStale_transitions_active_entry_to_stale()
    {
        var entry = WishlistEntry.Create(Guid.NewGuid(), "sku-1", 100m, Now, "user-1");

        entry.MarkStale(Now.AddDays(1), "system:wishlist-reconciliation-job");

        Assert.Equal(WishlistEntryStatus.Stale, entry.Status);
    }

    [Fact]
    public void MarkStale_is_idempotent_and_does_not_bump_updated_at_again()
    {
        var entry = WishlistEntry.Create(Guid.NewGuid(), "sku-1", 100m, Now, "user-1");
        var firstStaleAt = Now.AddDays(1);
        entry.MarkStale(firstStaleAt, "system:wishlist-discontinuation-consumer");

        entry.MarkStale(Now.AddDays(2), "system:wishlist-reconciliation-job");

        Assert.Equal(WishlistEntryStatus.Stale, entry.Status);
        Assert.Equal(firstStaleAt, entry.UpdatedAt);
    }
}
