namespace delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;

public class CreateScheduleCommandValidator : AbstractValidator<CreateScheduleCommand>
{
    public CreateScheduleCommandValidator()
    {
        RuleFor(x => x.ContractId).NotEmpty();
        RuleFor(x => x.PatientId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Duration).Must(d => d is 15 or 30).WithMessage("Duration must be 15 or 30 days.");
        RuleFor(x => x.StartDate).NotEqual(default(DateOnly));
    }
}
