using delivery_schedule_management.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace delivery_schedule_management.Infrastructure.Data.Configurations;

public class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.ToTable("ProductionOrders");

        builder.OwnsOne(o => o.ProductionDate, x =>
        {
            x.Property(p => p.Value).HasColumnName("ProductionDate").IsRequired();
        });

        builder.OwnsOne(o => o.DeliveryDate, x =>
        {
            x.Property(p => p.Value).HasColumnName("DeliveryDate").IsRequired();
            x.HasIndex(p => p.Value).IsUnique();
        });

        builder.Property(o => o.GeneratedAt).IsRequired();

        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey("ProductionOrderId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
