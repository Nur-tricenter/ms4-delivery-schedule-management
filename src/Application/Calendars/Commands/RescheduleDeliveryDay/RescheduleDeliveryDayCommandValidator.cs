namespace delivery_schedule_management.Application.Calendars.Commands.RescheduleDeliveryDay;

public class RescheduleDeliveryDayCommandValidator : AbstractValidator<RescheduleDeliveryDayCommand>
{
    public RescheduleDeliveryDayCommandValidator()
    {
        RuleFor(x => x.CalendarId).GreaterThan(0);
        RuleFor(x => x.DayId).GreaterThan(0);
        RuleFor(x => x.NewDate).NotEqual(default(DateOnly));
    }
}
