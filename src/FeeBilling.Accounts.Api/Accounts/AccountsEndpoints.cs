using FeeBilling.Accounts.Api.Common;
using FeeBilling.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FeeBilling.Accounts.Api.Accounts;

public static class AccountsEndpoints
{
    public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts")
            .RequireAuthorization()
            .WithTags("Accounts");

        group.MapGet("/", ListAccounts);
        group.MapGet("/{id:int}", GetAccount);

        return app;
    }

    private static async Task<Ok<List<AccountDto>>> ListAccounts(
        int firmId,
        string? search,
        FeeBillingDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.Accounts
            .AsNoTracking()
            .Include(a => a.Household)
            .Where(a => a.FirmId == firmId);

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(a => a.AccountNumber.Contains(search) || a.AccountName.Contains(search));
        }

        var accounts = await query.OrderBy(a => a.AccountName).ToListAsync(cancellationToken);
        var scheduleCodes = await FeeScheduleCodes.LoadAsync(db, cancellationToken);

        return TypedResults.Ok(accounts.Select(a => AccountDto.From(a, scheduleCodes)).ToList());
    }

    private static async Task<Results<Ok<AccountDto>, NotFound>> GetAccount(
        int id,
        FeeBillingDbContext db,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts
            .AsNoTracking()
            .Include(a => a.Household)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (account is null)
        {
            return TypedResults.NotFound();
        }

        var scheduleCodes = await FeeScheduleCodes.LoadAsync(db, cancellationToken);
        return TypedResults.Ok(AccountDto.From(account, scheduleCodes));
    }
}
