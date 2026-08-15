using delivery_schedule_management.Domain.Common;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Domain.Entities;

public class ProductionOrderLine : BaseEntity
{
    public int DeliveryDayId { get; private set; }

    public Guid PatientId { get; private set; }

    public DeliveryAddress Address { get; private set; } = null!;

    public Coordinates Coordinates { get; private set; } = null!;

    public PlanReference PlanRef { get; private set; } = null!;

    public TimeOnly? Hour { get; private set; }

    private ProductionOrderLine() { } // EF Core

    public ProductionOrderLine(int deliveryDayId, Guid patientId, DeliveryAddress address, PlanReference planRef, TimeOnly? hour)
    {
        DeliveryDayId = deliveryDayId;
        PatientId = patientId;
        Address = address;
        Coordinates = new Coordinates(address.Coordinates.Latitude, address.Coordinates.Longitude);
        PlanRef = planRef;
        Hour = hour;
    }
}
