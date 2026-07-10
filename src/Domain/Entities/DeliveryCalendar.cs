using delivery_schedule_management.Domain.Common;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Domain.Entities;

public class DeliveryCalendar : BaseAuditableEntity
{
    private readonly List<DeliveryDay> _days = new();

    public Guid PatientId { get; private set; }

    public ContractReference ContractId { get; private set; } = null!;

    public PlanReference PlanId { get; private set; } = null!;

    public PlanDuration Duration { get; private set; } = null!;

    public DeliveryDate StartDate { get; private set; } = null!;

    public CalendarStatus Status { get; private set; }

    public IReadOnlyCollection<DeliveryDay> Days => _days.AsReadOnly();

    private DeliveryCalendar() { } // EF Core

    private DeliveryCalendar(Guid patientId, ContractReference contractId, PlanReference planId, PlanDuration duration, DeliveryDate startDate)
    {
        PatientId = patientId;
        ContractId = contractId;
        PlanId = planId;
        Duration = duration;
        StartDate = startDate;
        Status = CalendarStatus.Active;

        for (var i = 0; i < duration.Days; i++)
        {
            _days.Add(new DeliveryDay(startDate.AddDays(i)));
        }
    }

    public static DeliveryCalendar Create(Guid patientId, ContractReference contractId, PlanReference planId, PlanDuration duration, DeliveryDate startDate)
        => new(patientId, contractId, planId, duration, startDate);
}
