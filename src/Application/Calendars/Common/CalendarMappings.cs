using delivery_schedule_management.Domain.Entities;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Application.Calendars.Common;

public static class CalendarMappings
{
    public static AddressDto ToDto(this DeliveryAddress address) =>
        new(address.Street, address.City, address.References, address.Notes, address.Coordinates.Latitude, address.Coordinates.Longitude);

    public static DeliveryDayDto ToDto(this DeliveryDay day) =>
        new(
            day.Id,
            day.DayNumber,
            day.Date.Value,
            day.Status,
            day.Address?.ToDto(),
            day.DeliveryHour,
            day.Portions?.Value,
            day.Menu
                .OrderBy(item => item.MealTimeOrder)
                .Select(item => new MenuItemDto(item.MealTime, item.MealTimeOrder, item.RecipeId, item.RecipeName, item.Rations))
                .ToList());

    public static CalendarVm ToVm(this DeliveryCalendar calendar) =>
        new(
            calendar.Id,
            calendar.PatientId,
            calendar.ContractId.Value,
            calendar.PlanId.Value,
            calendar.Duration.Days,
            calendar.StartDate.Value,
            calendar.Status,
            calendar.Days.Select(d => d.ToDto()).ToList());
}
