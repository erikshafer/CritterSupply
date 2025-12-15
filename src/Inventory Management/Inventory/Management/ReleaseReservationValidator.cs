using FluentValidation;

namespace Inventory.Management;

public sealed class ReleaseReservationValidator : AbstractValidator<ReleaseReservation>
{
    public ReleaseReservationValidator()
    {
        RuleFor(x => x.InventoryId).NotEmpty();
        RuleFor(x => x.ReservationId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(256);
    }
}
