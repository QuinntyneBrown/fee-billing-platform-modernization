using FeeBilling.Domain.Entities;
using FeeBilling.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace FeeBilling.Infrastructure.Persistence;

/// <summary>
/// EF Core model of the FeeBilling database (the same database the legacy EDMX maps).
/// Reverse-engineered with <c>dotnet ef dbcontext scaffold</c>, then trimmed to what Accounts.Api needs.
/// </summary>
public class FeeBillingDbContext(DbContextOptions<FeeBillingDbContext> options) : DbContext(options)
{
    public DbSet<Firm> Firms => Set<Firm>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Household> Households => Set<Household>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new FirmConfiguration());
        modelBuilder.ApplyConfiguration(new AccountConfiguration());
        modelBuilder.ApplyConfiguration(new HouseholdConfiguration());

        // Scaffolded, not wired yet. Billing.Api will need these:
        //   modelBuilder.ApplyConfiguration(new FeeScheduleConfiguration());
        //   modelBuilder.ApplyConfiguration(new FeeTierConfiguration());
    }
}
