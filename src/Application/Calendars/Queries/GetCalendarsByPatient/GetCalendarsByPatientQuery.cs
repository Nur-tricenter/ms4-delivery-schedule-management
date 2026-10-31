using delivery_schedule_management.Application.Calendars.Common;
using delivery_schedule_management.Application.Common.Interfaces;

namespace delivery_schedule_management.Application.Calendars.Queries.GetCalendarsByPatient;

/// <summary>
/// Every calendar of a patient, newest first. A patient can hold several contracts, so this is a
/// list; an unknown patient is an empty list, not a 404.
/// </summary>
public record GetCalendarsByPatientQuery(Guid PatientId) : IRequest<List<CalendarVm>>;

public class GetCalendarsByPatientQueryHandler(ICalendarRepository calendarRepository)
    : IRequestHandler<GetCalendarsByPatientQuery, List<CalendarVm>>
{
    public async Task<List<CalendarVm>> Handle(GetCalendarsByPatientQuery request, CancellationToken cancellationToken)
    {
        var calendars = await calendarRepository.GetByPatientIdAsync(request.PatientId, cancellationToken);

        return calendars.Select(c => c.ToVm()).ToList();
    }
}
