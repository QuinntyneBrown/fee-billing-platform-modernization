/*
    Seed data for the Q3 2026 golden-master scenarios (training plan, Section 10).

    Period: 2026-07-01 .. 2026-09-30 (92 days). All amounts CAD.

    Firm 1  MAPLE    Maple Ridge Wealth Partners (Ontario)       scenarios S1-S10, accounts 1001-1013
    Firm 2  LAURENT  Groupe Financier Laurentien (Québec)         scenario S11, accounts 2001-2500
                                                                  scenario S12 uses these account numbers
                                                                  (see seed/custodian-files/)

    Account -> scenario map
      1001  S1   Mid-tier               $2,500,000            STD-TIERED
      1002  S2   Exactly on boundary    $1,000,000            STD-TIERED
      1003  S3   Minimum fee            $80,000               STD-TIERED
      1004  S4   Blended                $2,500,000            STD-BLENDED
      1005  S5   Flat                   $750,000              FLAT-500
      1006  S6   Cash exclusion         $1,200,000 incl. $200,000 cash sleeve   STD-TIERED
      1007  S7   Household H-100 / A    $1,500,000            (household HH-TIERED)
      1008  S7   Household H-100 / B    $1,000,000            (household HH-TIERED)
      1009  S7   Household H-100 / C    $500,000              (household HH-TIERED)
      1010  S8   Household H-200 / A    $0                    (household HH-TIERED)
      1011  S8   Household H-200 / B    $0                    (household HH-TIERED)
      1012  S9   Opened 2026-08-15      $1,000,000            STD-TIERED
      1013  S10  $3M on Jul 1, -$1.5M on Aug 31, $1.5M on Sep 30              STD-TIERED
      2001-2500  S11  generated, deterministic, fractional-cent AUMs
*/
USE FeeBilling;
GO

SET NOCOUNT ON;

-------------------------------------------------------------------------------
-- Firms
-------------------------------------------------------------------------------
SET IDENTITY_INSERT dbo.Firms ON;
INSERT INTO dbo.Firms (Id, FirmCode, Name, Province, Culture) VALUES
    (1, 'MAPLE',   N'Maple Ridge Wealth Partners',   'ON', 'en-CA'),
    (2, 'LAURENT', N'Groupe Financier Laurentien',   'QC', 'fr-CA');
SET IDENTITY_INSERT dbo.Firms OFF;
GO

-------------------------------------------------------------------------------
-- Fee schedules (Section 10)
-------------------------------------------------------------------------------
SET IDENTITY_INSERT dbo.FeeSchedules ON;
INSERT INTO dbo.FeeSchedules (Id, FirmId, Code, Name, ScheduleType, IsHousehold, MinimumAnnualFee, FlatAnnualFee) VALUES
    (1, NULL, 'STD-TIERED',  N'Standard tiered',             'TIERED',  0, 1000.00, NULL),
    (2, NULL, 'STD-BLENDED', N'Standard blended',            'BLENDED', 0, 1000.00, NULL),
    (3, NULL, 'FLAT-500',    N'Flat $500 per quarter',       'FLAT',    0,    0.00, 2000.00),
    (4, NULL, 'HH-TIERED',   N'Household tiered',            'TIERED',  1, 2500.00, NULL);
SET IDENTITY_INSERT dbo.FeeSchedules OFF;

-- 1.00% <= $1M; 0.75% $1M-$5M; 0.50% > $5M
INSERT INTO dbo.FeeTiers (FeeScheduleId, LowerBound, UpperBound, AnnualRate) VALUES
    (1,       0.00, 1000000.00, 0.010000),
    (1, 1000000.00, 5000000.00, 0.007500),
    (1, 5000000.00,       NULL, 0.005000),
    (2,       0.00, 1000000.00, 0.010000),
    (2, 1000000.00, 5000000.00, 0.007500),
    (2, 5000000.00,       NULL, 0.005000),
    (4,       0.00, 1000000.00, 0.010000),
    (4, 1000000.00, 5000000.00, 0.007500),
    (4, 5000000.00,       NULL, 0.005000);
GO

-------------------------------------------------------------------------------
-- Households
-------------------------------------------------------------------------------
SET IDENTITY_INSERT dbo.Households ON;
INSERT INTO dbo.Households (Id, FirmId, HouseholdCode, Name, FeeScheduleId) VALUES
    (100, 1, 'H-100', N'Okafor Family',     4),
    (200, 1, 'H-200', N'Lindqvist Family',  4);
SET IDENTITY_INSERT dbo.Households OFF;
GO

-------------------------------------------------------------------------------
-- Firm 1 accounts (S1-S10)
-------------------------------------------------------------------------------
-- LastValuedAt is UTC: the valuation job for Sep 30 finished at 8:30 p.m. Eastern.
DECLARE @valued datetime2(0) = '2026-10-01T00:30:00';

SET IDENTITY_INSERT dbo.Accounts ON;
INSERT INTO dbo.Accounts (Id, FirmId, AccountNumber, AccountName, CustodianCode, HouseholdId, FeeScheduleId, OpenedOn, LastValuedAt) VALUES
    (1001, 1, 'MRW000001001', N'Carter, Janet - RRSP',          'FIDC', NULL, 1, '2014-03-12', @valued),  -- S1
    (1002, 1, 'MRW000001002', N'Nguyen, David - Non-Reg',       'FIDC', NULL, 1, '2016-07-01', @valued),  -- S2
    (1003, 1, 'MRW000001003', N'Singh, Priya - TFSA',           'FIDC', NULL, 1, '2019-01-08', @valued),  -- S3
    (1004, 1, 'MRW000001004', N'Morrison Holdings Ltd.',        'FIDC', NULL, 2, '2013-11-20', @valued),  -- S4
    (1005, 1, 'MRW000001005', N'Abernathy, Paul - RRIF',        'FIDC', NULL, 3, '2018-05-15', @valued),  -- S5
    (1006, 1, 'MRW000001006', N'Chen, Wei - Non-Reg',           'FIDC', NULL, 1, '2015-09-30', @valued),  -- S6
    (1007, 1, 'MRW000001007', N'Okafor, Grace - Non-Reg',       'FIDC',  100, NULL, '2012-02-01', @valued),  -- S7 A
    (1008, 1, 'MRW000001008', N'Okafor, Samuel - RRSP',         'FIDC',  100, NULL, '2012-02-01', @valued),  -- S7 B
    (1009, 1, 'MRW000001009', N'Okafor, Grace - TFSA',          'FIDC',  100, NULL, '2017-04-18', @valued),  -- S7 C
    (1010, 1, 'MRW000001010', N'Lindqvist, Erik - RRSP',        'FIDC',  200, NULL, '2020-06-01', @valued),  -- S8
    (1011, 1, 'MRW000001011', N'Lindqvist, Anna - TFSA',        'FIDC',  200, NULL, '2020-06-01', @valued),  -- S8
    (1012, 1, 'MRW000001012', N'Patel, Anjali - Non-Reg',       'FIDC', NULL, 1, '2026-08-15', @valued),  -- S9
    (1013, 1, 'MRW000001013', N'Fitzgerald Family Trust',       'FIDC', NULL, 1, '2011-10-03', @valued);  -- S10
SET IDENTITY_INSERT dbo.Accounts OFF;

-- Positions as of period end (2026-09-30)
INSERT INTO dbo.Positions (AccountId, AsOfDate, SecurityCode, SecurityName, Quantity, MarketValue, IsCashSleeve) VALUES
    -- S1: 2,500,000
    (1001, '2026-09-30', 'XIC',  N'iShares Core S&P/TSX Capped Composite',  30000.0000, 1500000.0000, 0),
    (1001, '2026-09-30', 'ZAG',  N'BMO Aggregate Bond Index',                70000.0000, 1000000.0000, 0),
    -- S2: 1,000,000 (exactly on the tier boundary)
    (1002, '2026-09-30', 'VCN',  N'Vanguard FTSE Canada All Cap',            20000.0000, 1000000.0000, 0),
    -- S3: 80,000 (fee below minimum)
    (1003, '2026-09-30', 'XEQT', N'iShares Core Equity ETF Portfolio',        2500.0000,   80000.0000, 0),
    -- S4: 2,500,000 blended
    (1004, '2026-09-30', 'XIU',  N'iShares S&P/TSX 60',                      50000.0000, 1750000.0000, 0),
    (1004, '2026-09-30', 'XBB',  N'iShares Core Canadian Universe Bond',     27000.0000,  750000.0000, 0),
    -- S5: flat schedule, AUM is irrelevant
    (1005, '2026-09-30', 'VBAL', N'Vanguard Balanced ETF Portfolio',         25000.0000,  750000.0000, 0),
    -- S6: 1,200,000 including a 200,000 cash sleeve (excluded from billing)
    (1006, '2026-09-30', 'XUS',  N'iShares Core S&P 500',                    18000.0000, 1000000.0000, 0),
    (1006, '2026-09-30', 'CASH', N'Cash sleeve',                            200000.0000,  200000.0000, 1),
    -- S7: household H-100 (A 1.5M, B 1.0M, C 0.5M)
    (1007, '2026-09-30', 'XAW',  N'iShares Core MSCI All Country World ex Canada', 36000.0000, 1500000.0000, 0),
    (1008, '2026-09-30', 'VCN',  N'Vanguard FTSE Canada All Cap',            20000.0000, 1000000.0000, 0),
    (1009, '2026-09-30', 'XEQT', N'iShares Core Equity ETF Portfolio',       15625.0000,  500000.0000, 0),
    -- S8: household H-200, all members zero
    (1010, '2026-09-30', 'CASH', N'Cash',                                        0.0000,       0.0000, 0),
    (1011, '2026-09-30', 'CASH', N'Cash',                                        0.0000,       0.0000, 0),
    -- S9: opened 2026-08-15
    (1012, '2026-09-30', 'VGRO', N'Vanguard Growth ETF Portfolio',           28000.0000, 1000000.0000, 0),
    -- S10: 1,500,000 at period end (after the Aug 31 withdrawal)
    (1013, '2026-09-30', 'XIC',  N'iShares Core S&P/TSX Capped Composite',   30000.0000, 1500000.0000, 0);

-- S10: 3,000,000 at the start of the period
INSERT INTO dbo.Positions (AccountId, AsOfDate, SecurityCode, SecurityName, Quantity, MarketValue, IsCashSleeve) VALUES
    (1013, '2026-07-01', 'XIC',  N'iShares Core S&P/TSX Capped Composite',   60000.0000, 3000000.0000, 0);

-- Flows
INSERT INTO dbo.CashFlows (AccountId, FlowDate, Amount, FlowType) VALUES
    (1012, '2026-08-15',  1000000.00, 'DEPOSIT'),      -- S9 initial funding
    (1013, '2026-08-31', -1500000.00, 'WITHDRAWAL');   -- S10 large withdrawal

-- Firm-level exclusions (proprietary funds the firm does not bill on)
INSERT INTO dbo.FirmBillingExclusions (FirmId, SecurityCode, Reason) VALUES
    (1, 'MRW-PRIV-CORE', N'Proprietary pooled fund - fee charged at fund level'),
    (2, 'LFG-PRIV-POOL', N'Fonds commun exclusif - non facturable');
GO

-------------------------------------------------------------------------------
-- Firm 2 accounts (S11): 500 generated accounts
--
-- Deterministic: re-running this script produces byte-identical data.
--   * most AUMs are pseudo-random with fractional cents (4 dp, as custodians send them)
--   * every 5th account is placed exactly on a half-cent boundary of a tier:
--       tier 1 (1.00%):  AUM = 100,002 + 4k          ->  AUM x 1.00% / 4 ends in .xx5
--       tier 2 (0.75%):  AUM = 1,000,008 + 16k       ->  span x 0.75% / 4 ends in .xx5
--   * every 7th account has a cash sleeve; every 50th holds the excluded proprietary pool
-------------------------------------------------------------------------------
DECLARE @lastNames TABLE (i int PRIMARY KEY, name nvarchar(50));
INSERT INTO @lastNames VALUES
    (0, N'Tremblay'), (1, N'Gagnon'), (2, N'Roy'), (3, N'Côté'), (4, N'Bouchard'),
    (5, N'Gauthier'), (6, N'Morin'), (7, N'Lavoie'), (8, N'Fortin'), (9, N'Gagné'),
    (10, N'Ouellet'), (11, N'Pelletier'), (12, N'Bélanger'), (13, N'Lévesque'), (14, N'Bergeron'),
    (15, N'Leblanc'), (16, N'Paquette'), (17, N'Girard'), (18, N'Simard'), (19, N'Boucher');

DECLARE @firstNames TABLE (i int PRIMARY KEY, name nvarchar(50));
INSERT INTO @firstNames VALUES
    (0, N'Marie'), (1, N'Jean'), (2, N'Pierre'), (3, N'Hélène'), (4, N'François'),
    (5, N'Josée'), (6, N'André'), (7, N'Geneviève'), (8, N'Stéphane'), (9, N'Élise'),
    (10, N'Luc'), (11, N'Chantal'), (12, N'Réjean'), (13, N'Sylvie'), (14, N'Benoît'),
    (15, N'Nathalie'), (16, N'Mathieu');

DECLARE @plans TABLE (i int PRIMARY KEY, name nvarchar(20));
INSERT INTO @plans VALUES (0, N'REER'), (1, N'CELI'), (2, N'Non enregistré'), (3, N'FERR');

DECLARE @valued2 datetime2(0) = '2026-10-01T00:30:00';
DECLARE @n int = 1;
DECLARE @id int, @seed bigint, @scheduleId int, @aum decimal(19,4), @first decimal(19,4), @name nvarchar(200);

SET IDENTITY_INSERT dbo.Accounts ON;

WHILE @n <= 500
BEGIN
    SET @id = 2000 + @n;
    SET @seed = (CAST(@n AS bigint) * 1103515245 + 12345) % 2147483648;

    SET @scheduleId = CASE
                          WHEN @n % 10 IN (6, 7) THEN 2   -- STD-BLENDED
                          WHEN @n % 10 = 8       THEN 3   -- FLAT-500
                          ELSE 1                          -- STD-TIERED
                      END;

    IF @n % 10 = 0
        SET @aum = 100002 + 4 * (@seed % 224000);
    ELSE IF @n % 10 = 5
        SET @aum = 1000008 + 16 * (@seed % 250000);
    ELSE
        SET @aum = 25000 + (@seed % 7500000) + CAST((@seed / 7) % 10000 AS decimal(19,4)) / 10000;

    SELECT @name = l.name + N', ' + f.name + N' - ' + p.name
    FROM @lastNames l, @firstNames f, @plans p
    WHERE l.i = @n % 20 AND f.i = (@n / 3) % 17 AND p.i = (@n / 7) % 4;

    INSERT INTO dbo.Accounts (Id, FirmId, AccountNumber, AccountName, CustodianCode, HouseholdId, FeeScheduleId, OpenedOn, LastValuedAt)
    VALUES (@id, 2, 'LFG' + RIGHT('000000000' + CAST(@id AS varchar(9)), 9), @name, 'NBIN', NULL, @scheduleId,
            DATEADD(DAY, @n * 7, '2012-01-15'), @valued2);

    -- Split the billable AUM across two holdings (the split must not lose fractional cents)
    SET @first = ROUND(@aum * 0.6, 4);
    INSERT INTO dbo.Positions (AccountId, AsOfDate, SecurityCode, SecurityName, Quantity, MarketValue, IsCashSleeve) VALUES
        (@id, '2026-09-30', 'ZCN', N'BMO S&P/TSX Capped Composite', ROUND(@first / 32.17, 4), @first, 0),
        (@id, '2026-09-30', 'ZAG', N'BMO Aggregate Bond Index',     ROUND((@aum - @first) / 13.91, 4), @aum - @first, 0);

    IF @n % 7 = 3
        INSERT INTO dbo.Positions (AccountId, AsOfDate, SecurityCode, SecurityName, Quantity, MarketValue, IsCashSleeve)
        VALUES (@id, '2026-09-30', 'CASH', N'Encaisse', 0, 10000 + (@n * 13) % 50000 + 0.1234, 1);

    IF @n % 50 = 25
        INSERT INTO dbo.Positions (AccountId, AsOfDate, SecurityCode, SecurityName, Quantity, MarketValue, IsCashSleeve)
        VALUES (@id, '2026-09-30', 'LFG-PRIV-POOL', N'Fonds commun Laurentien', 1000, 125000 + @n * 3.3333, 0);

    SET @n = @n + 1;
END

SET IDENTITY_INSERT dbo.Accounts OFF;
GO

-------------------------------------------------------------------------------
-- Users (Forms auth). Password for both: Billing2026!
-- Hash = base64(SHA1(UTF-16LE(salt + password))), as FeeBillingMembershipProvider computes it.
-------------------------------------------------------------------------------
SET IDENTITY_INSERT dbo.AppRoles ON;
INSERT INTO dbo.AppRoles (Id, Name) VALUES
    (1, N'BillingAdmin'),
    (2, N'BillingReviewer'),
    (3, N'ScheduleEditor');
SET IDENTITY_INSERT dbo.AppRoles OFF;

SET IDENTITY_INSERT dbo.AppUsers ON;
INSERT INTO dbo.AppUsers (Id, UserName, Email, PasswordHash, PasswordSalt, FirmId) VALUES
    (1, N'admin',     N'admin@feebilling.example',     'QsKfSff1OdW36ePwL0/07xazRao=', 'Q3rT8vLm2X', NULL),
    (2, N'maple.ops', N'ops@mapleridge.example',       'VJXtwTFInMENXdYZZmJPUWIYVEI=', 'Zp7Nw4Kd9F', 1);
SET IDENTITY_INSERT dbo.AppUsers OFF;

INSERT INTO dbo.AppUserRoles (UserId, RoleId) VALUES
    (1, 1), (1, 2), (1, 3),
    (2, 2);
GO
