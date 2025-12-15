using FluentValidation;

namespace Inventory.Management;

public sealed class CommitReservationValidator : AbstractValidator<CommitReservation>
{
    public CommitReservationValidator()
    {
        RuleFor(x => x.InventoryId).NotEmpty();
        RuleFor(x => x.ReservationId).NotEmpty();
    }
}
