namespace delivery_schedule_management.Application.Calendars.Commands.CancelDeliveryDay;

public class CancelDeliveryDayCommandValidator : AbstractValidator<CancelDeliveryDayCommand>
{
    public CancelDeliveryDayCommandValidator()
    {
        RuleFor(x => x.CalendarId).GreaterThan(0);
        RuleFor(x => x.DayId).GreaterThan(0);
    }
}
