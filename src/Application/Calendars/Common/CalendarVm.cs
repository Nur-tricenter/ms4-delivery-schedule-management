using delivery_schedule_management.Domain.Enums;

namespace delivery_schedule_management.Application.Calendars.Common;

public record CalendarVm(
    int Id,
    Guid PatientId,
    Guid ContractId,
    Guid PlanId,
    int Duration,
    DateOnly StartDate,
    CalendarStatus Status,
    List<DeliveryDayDto> Days);
