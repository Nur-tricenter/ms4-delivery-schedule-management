using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;

namespace delivery_schedule_management.Application.Calendars.Queries.GetCalendar;

public record GetCalendarQuery(int CalendarId) : IRequest<CalendarVm>;

public class GetCalendarQueryHandler(ICalendarRepository calendarRepository) : IRequestHandler<GetCalendarQuery, CalendarVm>
{
    public async Task<CalendarVm> Handle(GetCalendarQuery request, CancellationToken cancellationToken)
    {
        var calendar = await calendarRepository.GetByIdAsync(request.CalendarId, cancellationToken)
            ?? throw new NotFoundException(request.CalendarId.ToString(), nameof(DeliveryCalendar));

        return calendar.ToVm();
    }
}
