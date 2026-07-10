namespace delivery_schedule_management.Application.Calendars.Commands.SetDeliveryAddress;

public class SetDeliveryAddressCommandValidator : AbstractValidator<SetDeliveryAddressCommand>
{
    public SetDeliveryAddressCommandValidator()
    {
        RuleFor(x => x.CalendarId).GreaterThan(0);
        RuleFor(x => x.DayId).GreaterThan(0);
        RuleFor(x => x.Street).NotEmpty();
        RuleFor(x => x.City).NotEmpty();
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
    }
}
