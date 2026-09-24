using FeeBilling.Accounts.Api.Common;
using FeeBilling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FeeBilling.Accounts.Api.Households;

public static class HouseholdsEndpoints
{
    public static IEndpointRouteBuilder MapHouseholdsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/households")
            .RequireAuthorization()
            .WithTags("Households");

        group.MapGet("/", ListHouseholds);
        group.MapGet("/{id:int}", GetHousehold);

        return app;
    }

    private static async Task<Ok<List<HouseholdDto>>> ListHouseholds(
        int firmId,
        FeeBillingDbContext db,
        CancellationToken cancellationToken)
    {
        var households = await db.Households
            .AsNoTracking()
            .Include(h => h.Accounts)
            .Where(h => h.FirmId == firmId)
            .OrderBy(h => h.HouseholdCode)
            .ToListAsync(cancellationToken);

        var scheduleCodes = await FeeScheduleCodes.LoadAsync(db, cancellationToken);

        return TypedResults.Ok(households.Select(h => HouseholdDto.From(h, scheduleCodes, includeMembers: false)).ToList());
    }

    private static async Task<Results<Ok<HouseholdDto>, NotFound>> GetHousehold(
        int id,
        FeeBillingDbContext db,
        CancellationToken cancellationToken)
    {
        var household = await db.Households
            .AsNoTracking()
            .Include(h => h.Accounts)
            .SingleOrDefaultAsync(h => h.Id == id, cancellationToken);

        if (household is null)
        {
            return TypedResults.NotFound();
        }

        var scheduleCodes = await FeeScheduleCodes.LoadAsync(db, cancellationToken);
        return TypedResults.Ok(HouseholdDto.From(household, scheduleCodes, includeMembers: true));
    }
}
