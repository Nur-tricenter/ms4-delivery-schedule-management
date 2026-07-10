using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Application.Calendars.Commands.CreateSchedule;

public record CreateScheduleCommand(Guid ContractId, Guid PatientId, Guid PlanId, int Duration, DateOnly StartDate)
    : IRequest<CreateScheduleResult>;

public record CreateScheduleResult(int CalendarId, List<DeliveryDayDto> Days);

public class CreateScheduleCommandHandler(ICalendarRepository calendarRepository, IApplicationDbContext context)
    : IRequestHandler<CreateScheduleCommand, CreateScheduleResult>
{
    public async Task<CreateScheduleResult> Handle(CreateScheduleCommand request, CancellationToken cancellationToken)
    {
        // Idempotent by contractId: replay of the same request returns the existing calendar
        // instead of creating a duplicate.
        var existing = await calendarRepository.GetByContractIdAsync(request.ContractId, cancellationToken);
        if (existing is not null)
        {
            return ToResult(existing);
        }

        var calendar = DeliveryCalendar.Create(
            request.PatientId,
            new ContractReference(request.ContractId),
            new PlanReference(request.PlanId),
            new PlanDuration(request.Duration),
            new DeliveryDate(request.StartDate));

        calendarRepository.Add(calendar);

        await context.SaveChangesAsync(cancellationToken);

        return ToResult(calendar);
    }

    private static CreateScheduleResult ToResult(DeliveryCalendar calendar) =>
        new(calendar.Id, calendar.Days.Select(d => d.ToDto()).ToList());
}
