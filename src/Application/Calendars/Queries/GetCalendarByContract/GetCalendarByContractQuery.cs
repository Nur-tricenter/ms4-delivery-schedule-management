using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;

namespace delivery_schedule_management.Application.Calendars.Queries.GetCalendarByContract;

/// <summary>
/// The calendar MS4 created for an MS3 contract. A contract has at most one calendar
/// (<c>POST /schedule</c> is idempotent by <c>contractId</c>), so this returns one calendar or 404.
/// </summary>
public record GetCalendarByContractQuery(Guid ContractId) : IRequest<CalendarVm>;

public class GetCalendarByContractQueryHandler(ICalendarRepository calendarRepository)
    : IRequestHandler<GetCalendarByContractQuery, CalendarVm>
{
    public async Task<CalendarVm> Handle(GetCalendarByContractQuery request, CancellationToken cancellationToken)
    {
        var calendar = await calendarRepository.GetByContractIdAsync(request.ContractId, cancellationToken)
            ?? throw new NotFoundException(request.ContractId.ToString(), nameof(DeliveryCalendar));

        return calendar.ToVm();
    }
}
