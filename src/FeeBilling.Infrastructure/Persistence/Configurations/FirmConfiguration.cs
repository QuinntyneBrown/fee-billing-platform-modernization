using FeeBilling.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeeBilling.Infrastructure.Persistence.Configurations;

public class FirmConfiguration : IEntityTypeConfiguration<Firm>
{
    public void Configure(EntityTypeBuilder<Firm> entity)
    {
        entity.ToTable("Firms");

        entity.HasKey(e => e.Id).HasName("PK_Firms");

        entity.HasIndex(e => e.FirmCode, "UQ_Firms_FirmCode").IsUnique();

        entity.Property(e => e.FirmCode).HasMaxLength(20).IsUnicode(false);
        entity.Property(e => e.Name).HasMaxLength(200);
        entity.Property(e => e.Province).HasMaxLength(2).IsUnicode(false).IsFixedLength();
        entity.Property(e => e.Culture).HasMaxLength(10).IsUnicode(false).HasDefaultValue("en-CA");
        entity.Property(e => e.CreatedOn).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
    }
}
