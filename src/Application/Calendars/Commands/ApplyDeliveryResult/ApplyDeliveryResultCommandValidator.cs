namespace delivery_schedule_management.Application.Calendars.Commands.ApplyDeliveryResult;

public class ApplyDeliveryResultCommandValidator : AbstractValidator<ApplyDeliveryResultCommand>
{
    public ApplyDeliveryResultCommandValidator()
    {
        RuleFor(x => x.DayId).GreaterThan(0);
        RuleFor(x => x.Result).IsInEnum();
    }
}
