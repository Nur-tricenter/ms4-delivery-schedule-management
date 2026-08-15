using delivery_schedule_management.Domain.Enums;

namespace delivery_schedule_management.Application.Calendars.Common;

public record DeliveryDayDto(
    int Id,
    DateOnly Date,
    DayStatus Status,
    AddressDto? Address,
    TimeOnly? DeliveryHour,
    string? RecipeRef,
    int? Portions);
