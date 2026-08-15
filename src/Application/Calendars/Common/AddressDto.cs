namespace delivery_schedule_management.Application.Calendars.Common;

public record AddressDto(string Street, string City, string? References, string? Notes, double Latitude, double Longitude);
