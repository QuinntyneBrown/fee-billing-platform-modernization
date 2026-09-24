/*
    Creates the two legacy databases.

    FeeBilling           - system of record (accounts, schedules, positions, runs, invoices)
    FeeBillingReporting  - reporting facts. The legacy BillingRunner writes to BOTH inside one
                           TransactionScope, which promotes to MSDTC.
*/
IF DB_ID(N'FeeBilling') IS NULL
    CREATE DATABASE FeeBilling;
GO

IF DB_ID(N'FeeBillingReporting') IS NULL
    CREATE DATABASE FeeBillingReporting;
GO
