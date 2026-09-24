using FeeBilling.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeeBilling.Infrastructure.Persistence.Configurations;

// Scaffolded for Billing.Api. NOT applied in FeeBillingDbContext yet.
public class FeeTierConfiguration : IEntityTypeConfiguration<FeeTier>
{
    public void Configure(EntityTypeBuilder<FeeTier> entity)
    {
        entity.ToTable("FeeTiers");

        entity.HasKey(e => e.Id).HasName("PK_FeeTiers");

        entity.Property(e => e.LowerBound).HasPrecision(19, 2);
        entity.Property(e => e.UpperBound).HasPrecision(19, 2);
        entity.Property(e => e.AnnualRate).HasPrecision(18, 2);
    }
}
