using delivery_schedule_management.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace delivery_schedule_management.Infrastructure.Data.Configurations;

public class DeliveryCalendarConfiguration : IEntityTypeConfiguration<DeliveryCalendar>
{
    public void Configure(EntityTypeBuilder<DeliveryCalendar> builder)
    {
        builder.ToTable("DeliveryCalendars");

        builder.Property(c => c.PatientId).IsRequired();

        builder.OwnsOne(c => c.ContractId, o =>
        {
            o.Property(x => x.Value).HasColumnName("ContractId").IsRequired();
            o.HasIndex(x => x.Value).IsUnique();
        });

        builder.OwnsOne(c => c.PlanId, o =>
        {
            o.Property(x => x.Value).HasColumnName("PlanId").IsRequired();
        });

        builder.OwnsOne(c => c.Duration, o =>
        {
            o.Property(x => x.Days).HasColumnName("DurationDays").IsRequired();
        });

        builder.OwnsOne(c => c.StartDate, o =>
        {
            o.Property(x => x.Value).HasColumnName("StartDate").IsRequired();
        });

        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.HasMany(c => c.Days)
            .WithOne()
            .HasForeignKey("DeliveryCalendarId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
