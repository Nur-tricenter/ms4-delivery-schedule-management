using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.Enums;

namespace delivery_schedule_management.Application.Calendars.Commands.CancelDeliveryDay;

public record CancelDeliveryDayCommand(int CalendarId, int DayId) : IRequest<CancelDeliveryDayResult>;

public record CancelDeliveryDayResult(int DayId, DayStatus Status);

public class CancelDeliveryDayCommandHandler(ICalendarRepository calendarRepository, IApplicationDbContext context, IClock clock)
    : IRequestHandler<CancelDeliveryDayCommand, CancelDeliveryDayResult>
{
    public async Task<CancelDeliveryDayResult> Handle(CancelDeliveryDayCommand request, CancellationToken cancellationToken)
    {
        var calendar = await calendarRepository.GetByIdAsync(request.CalendarId, cancellationToken)
            ?? throw new NotFoundException(request.CalendarId.ToString(), nameof(DeliveryCalendar));

        var day = calendar.Days.FirstOrDefault(d => d.Id == request.DayId)
            ?? throw new NotFoundException(request.DayId.ToString(), nameof(DeliveryDay));

        day.Cancel(clock.Now);

        await context.SaveChangesAsync(cancellationToken);

        return new CancelDeliveryDayResult(day.Id, day.Status);
    }
}
