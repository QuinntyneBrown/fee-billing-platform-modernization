using System;

namespace FeeBilling.Core
{
    public static class AccountNumberFormatter
    {
        /// <summary>Masks all but the last 4 characters for statements: MRW000001001 -> ********1001.</summary>
        public static string Mask(string accountNumber)
        {
            if (string.IsNullOrEmpty(accountNumber) || accountNumber.Length <= 4)
            {
                return accountNumber;
            }

            return new string('*', accountNumber.Length - 4) + accountNumber.Substring(accountNumber.Length - 4);
        }

        /// <summary>Custodian files pad account numbers to 12 characters.</summary>
        public static string PadForFeed(string accountNumber)
        {
            if (accountNumber == null) throw new ArgumentNullException("accountNumber");
            return accountNumber.Trim().PadRight(12);
        }
    }
}
