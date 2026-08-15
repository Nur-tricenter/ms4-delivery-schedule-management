using delivery_schedule_management.Application.Common.Interfaces;
using delivery_schedule_management.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace delivery_schedule_management.Infrastructure.Data.Repositories;

public class CalendarRepository(ApplicationDbContext context) : ICalendarRepository
{
    public Task<DeliveryCalendar?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        context.Calendars.Include(c => c.Days).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<DeliveryCalendar?> GetByContractIdAsync(Guid contractId, CancellationToken cancellationToken) =>
        context.Calendars.Include(c => c.Days).FirstOrDefaultAsync(c => c.ContractId.Value == contractId, cancellationToken);

    public Task<DeliveryCalendar?> GetByDeliveryDayIdAsync(int dayId, CancellationToken cancellationToken) =>
        context.Calendars.Include(c => c.Days).FirstOrDefaultAsync(c => c.Days.Any(d => d.Id == dayId), cancellationToken);

    public Task<List<DeliveryCalendar>> GetActiveForDeliveryDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        context.Calendars.Include(c => c.Days).Where(c => c.Days.Any(d => d.Date.Value == date)).ToListAsync(cancellationToken);

    public void Add(DeliveryCalendar calendar) => context.Calendars.Add(calendar);
}
