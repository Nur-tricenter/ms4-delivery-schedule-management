using delivery_schedule_management.Domain.Common;
using delivery_schedule_management.Domain.Enums;
using delivery_schedule_management.Domain.ValueObjects;

namespace delivery_schedule_management.Domain.Entities;

public class ProductionOrder : BaseAuditableEntity
{
    private readonly List<ProductionOrderLine> _lines = new();

    public DeliveryDate ProductionDate { get; private set; } = null!;

    public DeliveryDate DeliveryDate { get; private set; } = null!;

    public DateTimeOffset GeneratedAt { get; private set; }

    public ProductionOrderStatus Status { get; private set; }

    public IReadOnlyCollection<ProductionOrderLine> Lines => _lines.AsReadOnly();

    private ProductionOrder() { } // EF Core

    private ProductionOrder(DeliveryDate deliveryDate, IEnumerable<ProductionOrderLine> lines, DateTimeOffset generatedAt)
    {
        DeliveryDate = deliveryDate;
        ProductionDate = deliveryDate.AddDays(-1);
        GeneratedAt = generatedAt;
        Status = ProductionOrderStatus.Generated;
        _lines.AddRange(lines);
    }

    public static ProductionOrder Create(DeliveryDate deliveryDate, IEnumerable<ProductionOrderLine> lines, DateTimeOffset generatedAt)
        => new(deliveryDate, lines, generatedAt);

    public void ReplaceLines(IEnumerable<ProductionOrderLine> lines, DateTimeOffset regeneratedAt)
    {
        _lines.Clear();
        _lines.AddRange(lines);
        GeneratedAt = regeneratedAt;
        Status = ProductionOrderStatus.Generated;
    }
}
