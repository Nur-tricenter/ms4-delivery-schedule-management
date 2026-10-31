using delivery_schedule_management.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace delivery_schedule_management.Infrastructure.Data.Configurations;

public class DeliveryDayConfiguration : IEntityTypeConfiguration<DeliveryDay>
{
    public void Configure(EntityTypeBuilder<DeliveryDay> builder)
    {
        builder.ToTable("DeliveryDays");

        builder.OwnsOne(d => d.Date, o =>
        {
            o.Property(x => x.Value).HasColumnName("Date").IsRequired();
        });

        builder.Property(d => d.DayNumber).IsRequired();

        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(d => d.DeliveryHour);

        builder.OwnsOne(d => d.Address, address =>
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

        // Owned collection -> its own table (DeliveryDayMenuItems), keyed by a shadow id. Bound
        // through the _menu backing field; MenuItem keeps a private ctor for EF (CLAUDE.md §5).
        builder.OwnsMany(d => d.Menu, menu =>
        {
            menu.ToTable("DeliveryDayMenuItems");
            menu.WithOwner().HasForeignKey("DeliveryDayId");
            menu.Property<int>("Id");
            menu.HasKey("Id");
            menu.Property(m => m.MealTime).HasMaxLength(64).IsRequired();
            menu.Property(m => m.MealTimeOrder);
            menu.Property(m => m.RecipeId);
            menu.Property(m => m.RecipeName).HasMaxLength(200).IsRequired();
            menu.Property(m => m.Rations);
            menu.HasIndex(m => m.RecipeId);
        });
        builder.Navigation(d => d.Menu).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsOne(d => d.Portions, o =>
        {
            o.Property(x => x.Value).HasColumnName("Portions");
        });

        builder.OwnsOne(d => d.Evidence, o =>
        {
            o.Property(x => x.RecordedAt).HasColumnName("EvidenceRecordedAt");
            o.Property(x => x.PhotoUrl).HasColumnName("EvidencePhotoUrl").HasMaxLength(Domain.ValueObjects.DeliveryEvidence.MaxLength);
            o.Property(x => x.SignatureUrl).HasColumnName("EvidenceSignatureUrl").HasMaxLength(Domain.ValueObjects.DeliveryEvidence.MaxLength);
        });
    }
}
