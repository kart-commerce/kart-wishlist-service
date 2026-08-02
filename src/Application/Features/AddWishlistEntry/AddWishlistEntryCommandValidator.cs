using FluentValidation;

namespace Kart.Wishlist.Application.Features.AddWishlistEntry;

public sealed class AddWishlistEntryCommandValidator : AbstractValidator<AddWishlistEntryCommand>
{
    public AddWishlistEntryCommandValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(200);
        RuleFor(x => x.UserId).NotEmpty();
    }
}
