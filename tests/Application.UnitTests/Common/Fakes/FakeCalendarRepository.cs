using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;

namespace delivery_schedule_management.Application.UnitTests.Common.Fakes;

public class FakeCalendarRepository : ICalendarRepository
{
    private readonly List<DeliveryCalendar> _calendars = [];
    private int _nextCalendarId = 1;
    private int _nextDayId = 1;

    public IReadOnlyList<DeliveryCalendar> Calendars => _calendars;

    public Task<DeliveryCalendar?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_calendars.FirstOrDefault(c => c.Id == id));

    public Task<DeliveryCalendar?> GetByContractIdAsync(Guid contractId, CancellationToken cancellationToken) =>
        Task.FromResult(_calendars.FirstOrDefault(c => c.ContractId.Value == contractId));

    public Task<DeliveryCalendar?> GetByDeliveryDayIdAsync(int dayId, CancellationToken cancellationToken) =>
        Task.FromResult(_calendars.FirstOrDefault(c => c.Days.Any(d => d.Id == dayId)));

    public Task<List<DeliveryCalendar>> GetActiveForDeliveryDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(_calendars.Where(c => c.Days.Any(d => d.Date.Value == date)).ToList());

    public void Add(DeliveryCalendar calendar)
    {
        calendar.Id = _nextCalendarId++;

        foreach (var day in calendar.Days)
        {
            day.Id = _nextDayId++;
        }

        _calendars.Add(calendar);
    }
}
