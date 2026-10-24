using delivery_schedule_management.Domain.Enums;

namespace delivery_schedule_management.Application.Calendars.Common;

public record DeliveryDayDto(
    int Id,
    int DayNumber,
    DateOnly Date,
    DayStatus Status,
    AddressDto? Address,
    TimeOnly? DeliveryHour,
    int? Portions,
    IReadOnlyList<MenuItemDto> Menu);

public record MenuItemDto(string MealTime, int MealTimeOrder, Guid RecipeId, string RecipeName, int Rations);
