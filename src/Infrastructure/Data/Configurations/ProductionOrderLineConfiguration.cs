using delivery_schedule_management.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace delivery_schedule_management.Infrastructure.Data.Configurations;

public class ProductionOrderLineConfiguration : IEntityTypeConfiguration<ProductionOrderLine>
{
    public void Configure(EntityTypeBuilder<ProductionOrderLine> builder)
    {
        builder.ToTable("ProductionOrderLines");

        builder.Property(l => l.DeliveryDayId).IsRequired();
        builder.Property(l => l.PatientId).IsRequired();
        builder.Property(l => l.Hour);

        builder.OwnsOne(l => l.Address, address =>
        {
            address.Property(a => a.Street).HasColumnName("AddressStreet").HasMaxLength(200);
            address.Property(a => a.City).HasColumnName("AddressCity").HasMaxLength(120);
            address.Property(a => a.References).HasColumnName("AddressReferences").HasMaxLength(500);
            address.Property(a => a.Notes).HasColumnName("AddressNotes").HasMaxLength(500);

            address.OwnsOne(a => a.Coordinates, c =>
            {
                c.Property(x => x.Latitude).HasColumnName("AddressLatitude");
                c.Property(x => x.Longitude).HasColumnName("AddressLongitude");
            });
        });

        builder.OwnsOne(l => l.Coordinates, c =>
        {
            c.Property(x => x.Latitude).HasColumnName("Latitude");
            c.Property(x => x.Longitude).HasColumnName("Longitude");
        });

        builder.OwnsOne(l => l.PlanRef, o =>
        {
            o.Property(x => x.Value).HasColumnName("PlanRef").IsRequired();
        });
    }
}
