using FeeBilling.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeeBilling.Infrastructure.Persistence.Configurations;

public class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> entity)
    {
        entity.ToTable("Households");

        entity.HasKey(e => e.Id).HasName("PK_Households");

        entity.HasIndex(e => new { e.FirmId, e.HouseholdCode }, "UQ_Households_Firm_Code").IsUnique();

        entity.Property(e => e.HouseholdCode).HasMaxLength(20).IsUnicode(false);
        entity.Property(e => e.Name).HasMaxLength(200);
        entity.Property(e => e.CreatedOn).HasColumnType("datetime").HasDefaultValueSql("(getdate())");

        entity.HasOne(d => d.Firm).WithMany()
            .HasForeignKey(d => d.FirmId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_Households_Firms");

        // FeeSchedule is not mapped yet (see FeeScheduleConfiguration).
        entity.Ignore(e => e.FeeSchedule);
    }
}
