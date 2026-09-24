using FeeBilling.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeeBilling.Infrastructure.Persistence.Configurations;

// Scaffolded for Billing.Api. NOT applied in FeeBillingDbContext yet.
public class FeeScheduleConfiguration : IEntityTypeConfiguration<FeeSchedule>
{
    public void Configure(EntityTypeBuilder<FeeSchedule> entity)
    {
        entity.ToTable("FeeSchedules");

        entity.HasKey(e => e.Id).HasName("PK_FeeSchedules");

        entity.HasIndex(e => e.Code, "UQ_FeeSchedules_Code").IsUnique();

        entity.Property(e => e.Code).HasMaxLength(20).IsUnicode(false);
        entity.Property(e => e.Name).HasMaxLength(100);
        entity.Property(e => e.ScheduleType)
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasConversion(
                v => v.ToString().ToUpperInvariant(),
                v => Enum.Parse<FeeScheduleType>(v, true));
        entity.Property(e => e.MinimumAnnualFee).HasColumnType("decimal(19, 2)");
        entity.Property(e => e.FlatAnnualFee).HasColumnType("decimal(19, 2)");
        entity.Property(e => e.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength().HasDefaultValue("CAD");
        entity.Property(e => e.CreatedOn).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
        entity.Property(e => e.ModifiedOn).HasColumnType("datetime");
        entity.Property(e => e.ModifiedBy).HasMaxLength(100);

        entity.HasMany(e => e.Tiers).WithOne()
            .HasForeignKey(t => t.FeeScheduleId)
            .HasConstraintName("FK_FeeTiers_FeeSchedules");
    }
}
