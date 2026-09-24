using System.Data.Entity;

namespace FeeBilling.Data.Reporting
{
    /// <summary>
    /// Reporting database (FeeBillingReporting). Written by BillingRunner inside the same
    /// TransactionScope as the Invoices insert, which is why production needs MSDTC.
    ///
    /// Code First against an existing database (the EDMX designer kept crashing on the
    /// second database, 2015).
    /// </summary>
    public class ReportingEntities : DbContext
    {
        static ReportingEntities()
        {
            Database.SetInitializer<ReportingEntities>(null);
        }

        public ReportingEntities()
            : base("name=FeeBillingReporting")
        {
        }

        public ReportingEntities(string nameOrConnectionString)
            : base(nameOrConnectionString)
        {
        }

        public virtual DbSet<FeeFact> FeeFacts { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FeeFact>().ToTable("FeeFacts", "dbo");
            modelBuilder.Entity<FeeFact>().Property(f => f.BillableAum).HasPrecision(19, 4);
            modelBuilder.Entity<FeeFact>().Property(f => f.FeeAmount).HasPrecision(19, 2);
            modelBuilder.Entity<FeeFact>().Property(f => f.ScheduleCode).HasMaxLength(20).IsUnicode(false);
        }
    }
}
