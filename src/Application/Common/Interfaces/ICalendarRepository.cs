using delivery_schedule_management.Domain.Entities;

namespace delivery_schedule_management.Application.Common.Interfaces;

public interface ICalendarRepository
{
    Task<DeliveryCalendar?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<DeliveryCalendar?> GetByContractIdAsync(Guid contractId, CancellationToken cancellationToken);

    Task<DeliveryCalendar?> GetByDeliveryDayIdAsync(int dayId, CancellationToken cancellationToken);

    Task<List<DeliveryCalendar>> GetActiveForDeliveryDateAsync(DateOnly date, CancellationToken cancellationToken);

    void Add(DeliveryCalendar calendar);
}
