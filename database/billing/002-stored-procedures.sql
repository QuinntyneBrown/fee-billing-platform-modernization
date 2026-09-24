/*
    The 14 stored procedures called by the legacy code, either through EF6 Database.SqlQuery
    or through ADO.NET SqlDataAdapter.

    Some are hot paths (called once per account per billing run). Some are called from one screen.
    One or two may not be called at all any more. Nobody has checked which is which.
*/
USE FeeBilling;
GO

-------------------------------------------------------------------------------
-- 1. Billable AUM for one account on one date. Called once per account per run.
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_GetBillableAum
    @AccountId  int,
    @AsOfDate   date
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CAST(ISNULL(SUM(p.MarketValue), 0) AS decimal(19,4)) AS BillableAum
    FROM dbo.Positions p
    INNER JOIN dbo.Accounts a ON a.Id = p.AccountId
    WHERE p.AccountId = @AccountId
      AND p.AsOfDate = @AsOfDate
      AND p.IsCashSleeve = 0
      AND NOT EXISTS (SELECT 1
                      FROM dbo.FirmBillingExclusions x
                      WHERE x.FirmId = a.FirmId
                        AND x.SecurityCode = p.SecurityCode);
END
GO

-------------------------------------------------------------------------------
-- 2. Invoice export for a run (feeds the DataSet behind /api/billing/runs/{id}/invoices).
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_GetInvoicesForRun
    @RunId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  i.Id            AS InvoiceId,
            r.Id            AS RunId,
            f.FirmCode,
            r.PeriodEnd,
            a.Id            AS AccountId,
            a.AccountNumber,
            a.AccountName,
            h.HouseholdCode,
            i.Amount,
            i.Status,
            i.InvoiceDate
    FROM dbo.Invoices i
    INNER JOIN dbo.BillingRunQueue r ON r.Id = i.RunId
    INNER JOIN dbo.Firms f ON f.Id = r.FirmId
    INNER JOIN dbo.Accounts a ON a.Id = i.AccountId
    LEFT  JOIN dbo.Households h ON h.Id = i.HouseholdId
    WHERE i.RunId = @RunId
    ORDER BY a.AccountNumber;
END
GO

-------------------------------------------------------------------------------
-- 3. Household members.
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_GetHouseholdMembers
    @HouseholdId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT a.Id, a.AccountNumber, a.AccountName, a.FirmId, a.OpenedOn, a.ClosedOn, a.IsActive
    FROM dbo.Accounts a
    WHERE a.HouseholdId = @HouseholdId
    ORDER BY a.Id;
END
GO

-------------------------------------------------------------------------------
-- 4. Next pending run. (BillingRunner uses a LINQ query instead; kept "just in case".)
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_GetPendingBillingRun
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1) Id, FirmId, PeriodEnd, Status, RequestedBy, RequestedOn
    FROM dbo.BillingRunQueue
    WHERE Status = 'Pending'
    ORDER BY RequestedOn;
END
GO

-------------------------------------------------------------------------------
-- 5. Enqueue a run. No duplicate check.
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_EnqueueBillingRun
    @FirmId      int,
    @PeriodEnd   date,
    @RequestedBy nvarchar(100)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.BillingRunQueue (FirmId, PeriodEnd, Status, RequestedBy, RequestedOn)
    VALUES (@FirmId, @PeriodEnd, 'Pending', @RequestedBy, GETDATE());

    SELECT CAST(SCOPE_IDENTITY() AS int) AS RunId;
END
GO

-------------------------------------------------------------------------------
-- 6. Mark a run complete / failed.
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_CompleteBillingRun
    @RunId          int,
    @Status         varchar(20),
    @ErrorMessage   nvarchar(max) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.BillingRunQueue
    SET Status = @Status,
        CompletedOn = GETDATE(),
        ErrorMessage = @ErrorMessage
    WHERE Id = @RunId;
END
GO

-------------------------------------------------------------------------------
-- 7. Schedule + tiers (two result sets).
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_GetFeeScheduleWithTiers
    @FeeScheduleId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, FirmId, Code, Name, ScheduleType, IsHousehold, MinimumAnnualFee, FlatAnnualFee, Currency, IsActive
    FROM dbo.FeeSchedules
    WHERE Id = @FeeScheduleId;

    SELECT Id, FeeScheduleId, LowerBound, UpperBound, AnnualRate
    FROM dbo.FeeTiers
    WHERE FeeScheduleId = @FeeScheduleId
    ORDER BY LowerBound;
END
GO

-------------------------------------------------------------------------------
-- 8. Account search box on the Accounts screen.
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_SearchAccounts
    @FirmId int,
    @Search nvarchar(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (200) a.Id, a.AccountNumber, a.AccountName, a.HouseholdId, a.FeeScheduleId, a.OpenedOn
    FROM dbo.Accounts a
    WHERE a.FirmId = @FirmId
      AND (a.AccountNumber LIKE '%' + @Search + '%' OR a.AccountName LIKE '%' + @Search + '%')
    ORDER BY a.AccountName;
END
GO

-------------------------------------------------------------------------------
-- 9. Positions for one account on one date.
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_GetAccountPositions
    @AccountId  int,
    @AsOfDate   date
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, AccountId, AsOfDate, SecurityCode, SecurityName, Quantity, MarketValue, IsCashSleeve
    FROM dbo.Positions
    WHERE AccountId = @AccountId
      AND AsOfDate = @AsOfDate
    ORDER BY SecurityCode;
END
GO

-------------------------------------------------------------------------------
-- 10. Upsert one position from a processed custodian batch. Called once per file line.
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_UpsertPosition
    @AccountNumber  varchar(12),
    @AsOfDate       date,
    @SecurityCode   varchar(20),
    @MarketValue    decimal(19,4),
    @SourceBatchId  int
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @AccountId int = (SELECT Id FROM dbo.Accounts WHERE AccountNumber = @AccountNumber);
    IF @AccountId IS NULL
    BEGIN
        RAISERROR('Unknown account number %s', 16, 1, @AccountNumber);
        RETURN;
    END

    MERGE dbo.Positions AS target
    USING (SELECT @AccountId AS AccountId, @AsOfDate AS AsOfDate, @SecurityCode AS SecurityCode) AS src
        ON target.AccountId = src.AccountId
       AND target.AsOfDate = src.AsOfDate
       AND target.SecurityCode = src.SecurityCode
    WHEN MATCHED THEN
        UPDATE SET MarketValue = @MarketValue, SourceBatchId = @SourceBatchId
    WHEN NOT MATCHED THEN
        INSERT (AccountId, AsOfDate, SecurityCode, MarketValue, SourceBatchId)
        VALUES (@AccountId, @AsOfDate, @SecurityCode, @MarketValue, @SourceBatchId);
END
GO

-------------------------------------------------------------------------------
-- 11. Nightly housekeeping.
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_ArchiveStagedBatches
    @OlderThanDays int = 90
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.StagedBatches
    SET Status = 'Archived'
    WHERE Status = 'Processed'
      AND ReceivedOn < DATEADD(DAY, -@OlderThanDays, GETDATE());

    SELECT @@ROWCOUNT AS ArchivedCount;
END
GO

-------------------------------------------------------------------------------
-- 12. Firm dashboard summary. Scans every position for the firm on the date.
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_FirmBillingSummary
    @FirmId     int,
    @PeriodEnd  date
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  f.FirmCode,
            @PeriodEnd AS PeriodEnd,
            (SELECT COUNT(*) FROM dbo.Accounts a WHERE a.FirmId = f.Id AND a.IsActive = 1) AS ActiveAccounts,
            (SELECT ISNULL(SUM(p.MarketValue), 0)
               FROM dbo.Positions p
               INNER JOIN dbo.Accounts a ON a.Id = p.AccountId
              WHERE a.FirmId = f.Id AND p.AsOfDate = @PeriodEnd AND p.IsCashSleeve = 0) AS TotalBillableAum,
            (SELECT ISNULL(SUM(i.Amount), 0)
               FROM dbo.Invoices i
               INNER JOIN dbo.BillingRunQueue r ON r.Id = i.RunId
              WHERE r.FirmId = f.Id AND r.PeriodEnd = @PeriodEnd AND r.Status = 'Complete') AS TotalInvoiced
    FROM dbo.Firms f
    WHERE f.Id = @FirmId;
END
GO

-------------------------------------------------------------------------------
-- 13. Billed vs. debited by the custodian.
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_ReconcileFeeDebits
    @RunId int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  i.Id                        AS InvoiceId,
            a.AccountNumber,
            i.Amount                    AS InvoicedAmount,
            ISNULL(SUM(d.DebitedAmount), 0) AS DebitedAmount,
            i.Amount - ISNULL(SUM(d.DebitedAmount), 0) AS Difference
    FROM dbo.Invoices i
    INNER JOIN dbo.Accounts a ON a.Id = i.AccountId
    LEFT  JOIN dbo.FeeDebits d ON d.InvoiceId = i.Id
    WHERE i.RunId = @RunId
    GROUP BY i.Id, a.AccountNumber, i.Amount
    HAVING i.Amount <> ISNULL(SUM(d.DebitedAmount), 0);
END
GO

-------------------------------------------------------------------------------
-- 14. Forms-auth login lookup (FeeBillingMembershipProvider).
-------------------------------------------------------------------------------
CREATE PROCEDURE dbo.usp_ValidateUser
    @UserName nvarchar(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, UserName, PasswordHash, PasswordSalt, FirmId, IsLockedOut
    FROM dbo.AppUsers
    WHERE UserName = @UserName;
END
GO
