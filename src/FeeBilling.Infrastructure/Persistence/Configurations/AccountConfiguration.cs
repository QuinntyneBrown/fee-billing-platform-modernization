using FeeBilling.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeeBilling.Infrastructure.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> entity)
    {
        entity.ToTable("Accounts");

        entity.HasKey(e => e.Id).HasName("PK_Accounts");

        entity.HasIndex(e => e.AccountNumber, "UQ_Accounts_AccountNumber").IsUnique();
        entity.HasIndex(e => e.FirmId, "IX_Accounts_FirmId");
        entity.HasIndex(e => e.HouseholdId, "IX_Accounts_HouseholdId");

        entity.Property(e => e.AccountNumber).HasMaxLength(12).IsUnicode(false);
        entity.Property(e => e.AccountName).HasMaxLength(200);
        entity.Property(e => e.CustodianCode).HasMaxLength(10).IsUnicode(false);
        entity.Property(e => e.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength().HasDefaultValue("CAD");
        entity.Property(e => e.OpenedOn).HasColumnType("date");
        entity.Property(e => e.ClosedOn).HasColumnType("date");
        entity.Property(e => e.LastValuedAt).HasPrecision(0);
        entity.Property(e => e.CreatedOn).HasColumnType("datetime").HasDefaultValueSql("(getdate())");

        entity.HasOne(d => d.Firm).WithMany()
            .HasForeignKey(d => d.FirmId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_Accounts_Firms");

        entity.HasOne(d => d.Household).WithMany(p => p.Accounts)
            .HasForeignKey(d => d.HouseholdId)
            .HasConstraintName("FK_Accounts_Households");

        // FeeSchedule is not mapped yet (see FeeScheduleConfiguration).
        entity.Ignore(e => e.FeeSchedule);
    }
}
