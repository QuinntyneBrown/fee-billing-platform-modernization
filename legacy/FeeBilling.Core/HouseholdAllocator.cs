using System;
using System.Collections.Generic;
using System.Linq;
using FeeBilling.Domain.ValueObjects;

namespace FeeBilling.Core
{
    public static class HouseholdAllocator
    {
        public static Dictionary<int, decimal> Allocate(decimal householdFee, IList<AccountAum> members)
        {
            var total = members.Sum(m => m.BillableAum);
            return members.ToDictionary(
                m => m.AccountId,
                m => Math.Round(householdFee * m.BillableAum / total, 2, MidpointRounding.AwayFromZero));
            // Sum of allocations can differ from householdFee by ±0.01 × (n-1). Divide-by-zero if total == 0.
        }
    }
}
