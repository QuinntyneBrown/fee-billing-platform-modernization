using FeeBilling.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FeeBilling.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "FeeBilling";

    public static IServiceCollection AddFeeBillingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<FeeBillingDbContext>(options => options.UseSqlServer(connectionString));

        services.AddHealthChecks()
            .AddDbContextCheck<FeeBillingDbContext>("feebilling-db");

        return services;
    }
}
