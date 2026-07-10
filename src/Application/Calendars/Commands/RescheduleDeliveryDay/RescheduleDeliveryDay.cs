using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Application.Calendars.Commands.RescheduleDeliveryDay;

public record RescheduleDeliveryDayCommand(int CalendarId, int DayId, DateOnly NewDate) : IRequest<RescheduleDeliveryDayResult>;

public record RescheduleDeliveryDayResult(int DayId, DayStatus Status);

public class RescheduleDeliveryDayCommandHandler(ICalendarRepository calendarRepository, IApplicationDbContext context, IClock clock)
    : IRequestHandler<RescheduleDeliveryDayCommand, RescheduleDeliveryDayResult>
{
    public async Task<RescheduleDeliveryDayResult> Handle(RescheduleDeliveryDayCommand request, CancellationToken cancellationToken)
    {
        var calendar = await calendarRepository.GetByIdAsync(request.CalendarId, cancellationToken)
            ?? throw new NotFoundException(request.CalendarId.ToString(), nameof(DeliveryCalendar));

        var day = calendar.Days.FirstOrDefault(d => d.Id == request.DayId)
            ?? throw new NotFoundException(request.DayId.ToString(), nameof(DeliveryDay));

        day.Reschedule(new DeliveryDate(request.NewDate), clock.Now);

        await context.SaveChangesAsync(cancellationToken);

        return new RescheduleDeliveryDayResult(day.Id, day.Status);
    }
}
