/*
    FeeBilling schema (as found).

    This DDL was reverse-engineered from production in 2019 and has been hand-maintained since.
    It is described by TWO models today:
      - legacy/FeeBilling.Data/FeeBilling.edmx          (EF6 Database-First)
      - src/FeeBilling.Infrastructure (EF Core 10)      (partial: Firms, Households, Accounts)
    Nobody has decided which of them owns schema changes. See docs/handover.md, issue #4.
*/
USE FeeBilling;
GO

-------------------------------------------------------------------------------
-- Reference data
-------------------------------------------------------------------------------
CREATE TABLE dbo.Firms
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_Firms PRIMARY KEY,
    FirmCode        varchar(20)     NOT NULL,
    Name            nvarchar(200)   NOT NULL,
    Province        char(2)         NULL,
    Culture         varchar(10)     NOT NULL CONSTRAINT DF_Firms_Culture DEFAULT ('en-CA'),
    IsActive        bit             NOT NULL CONSTRAINT DF_Firms_IsActive DEFAULT (1),
    CreatedOn       datetime        NOT NULL CONSTRAINT DF_Firms_CreatedOn DEFAULT (GETDATE()),
    CONSTRAINT UQ_Firms_FirmCode UNIQUE (FirmCode)
);
GO

CREATE TABLE dbo.FeeSchedules
(
    Id                  int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_FeeSchedules PRIMARY KEY,
    FirmId              int             NULL,           -- NULL = platform-wide template
    Code                varchar(20)     NOT NULL,
    Name                nvarchar(100)   NOT NULL,
    ScheduleType        varchar(20)     NOT NULL,       -- 'TIERED' | 'BLENDED' | 'FLAT'
    IsHousehold         bit             NOT NULL CONSTRAINT DF_FeeSchedules_IsHousehold DEFAULT (0),
    MinimumAnnualFee    decimal(19,2)   NOT NULL CONSTRAINT DF_FeeSchedules_MinimumAnnualFee DEFAULT (0),
    FlatAnnualFee       decimal(19,2)   NULL,
    Currency            char(3)         NOT NULL CONSTRAINT DF_FeeSchedules_Currency DEFAULT ('CAD'),
    IsActive            bit             NOT NULL CONSTRAINT DF_FeeSchedules_IsActive DEFAULT (1),
    CreatedOn           datetime        NOT NULL CONSTRAINT DF_FeeSchedules_CreatedOn DEFAULT (GETDATE()),
    ModifiedOn          datetime        NULL,
    ModifiedBy          nvarchar(100)   NULL,
    CONSTRAINT UQ_FeeSchedules_Code UNIQUE (Code),
    CONSTRAINT FK_FeeSchedules_Firms FOREIGN KEY (FirmId) REFERENCES dbo.Firms (Id),
    CONSTRAINT CK_FeeSchedules_ScheduleType CHECK (ScheduleType IN ('TIERED', 'BLENDED', 'FLAT'))
);
GO

CREATE TABLE dbo.FeeTiers
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_FeeTiers PRIMARY KEY,
    FeeScheduleId   int             NOT NULL,
    LowerBound      decimal(19,2)   NOT NULL,
    UpperBound      decimal(19,2)   NULL,           -- NULL = no upper bound
    AnnualRate      decimal(9,6)    NOT NULL,       -- 0.007500 = 0.75%
    CONSTRAINT FK_FeeTiers_FeeSchedules FOREIGN KEY (FeeScheduleId) REFERENCES dbo.FeeSchedules (Id)
);
GO

-------------------------------------------------------------------------------
-- Accounts and households
-------------------------------------------------------------------------------
CREATE TABLE dbo.Households
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_Households PRIMARY KEY,
    FirmId          int             NOT NULL,
    HouseholdCode   varchar(20)     NOT NULL,
    Name            nvarchar(200)   NOT NULL,
    FeeScheduleId   int             NULL,
    CreatedOn       datetime        NOT NULL CONSTRAINT DF_Households_CreatedOn DEFAULT (GETDATE()),
    CONSTRAINT UQ_Households_Firm_Code UNIQUE (FirmId, HouseholdCode),
    CONSTRAINT FK_Households_Firms FOREIGN KEY (FirmId) REFERENCES dbo.Firms (Id),
    CONSTRAINT FK_Households_FeeSchedules FOREIGN KEY (FeeScheduleId) REFERENCES dbo.FeeSchedules (Id)
);
GO

CREATE TABLE dbo.Accounts
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_Accounts PRIMARY KEY,
    FirmId          int             NOT NULL,
    AccountNumber   varchar(12)     NOT NULL,       -- custodian account number, fixed width in feed files
    AccountName     nvarchar(200)   NOT NULL,
    CustodianCode   varchar(10)     NOT NULL,
    Currency        char(3)         NOT NULL CONSTRAINT DF_Accounts_Currency DEFAULT ('CAD'),
    HouseholdId     int             NULL,
    FeeScheduleId   int             NULL,           -- NULL when billed through a household schedule
    OpenedOn        date            NOT NULL,
    ClosedOn        date            NULL,
    IsActive        bit             NOT NULL CONSTRAINT DF_Accounts_IsActive DEFAULT (1),
    LastValuedAt    datetime2(0)    NULL,           -- written by the nightly valuation job, in UTC
    CreatedOn       datetime        NOT NULL CONSTRAINT DF_Accounts_CreatedOn DEFAULT (GETDATE()),
    CONSTRAINT UQ_Accounts_AccountNumber UNIQUE (AccountNumber),
    CONSTRAINT FK_Accounts_Firms FOREIGN KEY (FirmId) REFERENCES dbo.Firms (Id),
    CONSTRAINT FK_Accounts_Households FOREIGN KEY (HouseholdId) REFERENCES dbo.Households (Id),
    CONSTRAINT FK_Accounts_FeeSchedules FOREIGN KEY (FeeScheduleId) REFERENCES dbo.FeeSchedules (Id)
);
GO

CREATE NONCLUSTERED INDEX IX_Accounts_FirmId ON dbo.Accounts (FirmId);
CREATE NONCLUSTERED INDEX IX_Accounts_HouseholdId ON dbo.Accounts (HouseholdId);
GO

-------------------------------------------------------------------------------
-- Positions, exclusions, flows
-------------------------------------------------------------------------------
CREATE TABLE dbo.Positions
(
    Id              bigint          IDENTITY(1,1) NOT NULL CONSTRAINT PK_Positions PRIMARY KEY,
    AccountId       int             NOT NULL,
    AsOfDate        date            NOT NULL,
    SecurityCode    varchar(20)     NOT NULL,
    SecurityName    nvarchar(200)   NULL,
    Quantity        decimal(19,4)   NOT NULL CONSTRAINT DF_Positions_Quantity DEFAULT (0),
    MarketValue     decimal(19,4)   NOT NULL,       -- 4dp: custodians send fractional cents
    IsCashSleeve    bit             NOT NULL CONSTRAINT DF_Positions_IsCashSleeve DEFAULT (0),
    SourceBatchId   int             NULL,
    CONSTRAINT FK_Positions_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts (Id)
);
GO

CREATE NONCLUSTERED INDEX IX_Positions_Account_AsOf ON dbo.Positions (AccountId, AsOfDate) INCLUDE (SecurityCode, MarketValue, IsCashSleeve);
GO

CREATE TABLE dbo.FirmBillingExclusions
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_FirmBillingExclusions PRIMARY KEY,
    FirmId          int             NOT NULL,
    SecurityCode    varchar(20)     NOT NULL,
    Reason          nvarchar(200)   NULL,
    CONSTRAINT FK_FirmBillingExclusions_Firms FOREIGN KEY (FirmId) REFERENCES dbo.Firms (Id)
);
GO

CREATE TABLE dbo.CashFlows
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_CashFlows PRIMARY KEY,
    AccountId       int             NOT NULL,
    FlowDate        date            NOT NULL,
    Amount          decimal(19,2)   NOT NULL,       -- negative = withdrawal
    FlowType        varchar(20)     NOT NULL,       -- 'DEPOSIT' | 'WITHDRAWAL' | 'TRANSFER'
    CONSTRAINT FK_CashFlows_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts (Id)
);
GO

-------------------------------------------------------------------------------
-- Billing runs and invoices
-------------------------------------------------------------------------------
-- NOTE: no uniqueness on (FirmId, PeriodEnd). Two clicks = two runs.
CREATE TABLE dbo.BillingRunQueue
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_BillingRunQueue PRIMARY KEY,
    FirmId          int             NOT NULL,
    PeriodEnd       date            NOT NULL,
    Status          varchar(20)     NOT NULL CONSTRAINT DF_BillingRunQueue_Status DEFAULT ('Pending'),
    RequestedBy     nvarchar(100)   NULL,
    RequestedOn     datetime        NOT NULL CONSTRAINT DF_BillingRunQueue_RequestedOn DEFAULT (GETDATE()),
    StartedOn       datetime        NULL,
    CompletedOn     datetime        NULL,
    ErrorMessage    nvarchar(max)   NULL,
    CONSTRAINT FK_BillingRunQueue_Firms FOREIGN KEY (FirmId) REFERENCES dbo.Firms (Id)
);
GO

CREATE TABLE dbo.Invoices
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_Invoices PRIMARY KEY,
    RunId           int             NOT NULL,
    AccountId       int             NOT NULL,
    HouseholdId     int             NULL,
    Amount          decimal(19,2)   NOT NULL,
    InvoiceDate     datetime        NOT NULL CONSTRAINT DF_Invoices_InvoiceDate DEFAULT (GETDATE()),
    Status          varchar(20)     NOT NULL CONSTRAINT DF_Invoices_Status DEFAULT ('Draft'),
    ApprovedBy      nvarchar(100)   NULL,
    ApprovedOn      datetime        NULL,
    CONSTRAINT FK_Invoices_BillingRunQueue FOREIGN KEY (RunId) REFERENCES dbo.BillingRunQueue (Id),
    CONSTRAINT FK_Invoices_Accounts FOREIGN KEY (AccountId) REFERENCES dbo.Accounts (Id)
);
GO

CREATE NONCLUSTERED INDEX IX_Invoices_RunId ON dbo.Invoices (RunId);
GO

CREATE TABLE dbo.FeeDebits
(
    Id                  int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_FeeDebits PRIMARY KEY,
    InvoiceId           int             NOT NULL,
    CustodianCode       varchar(10)     NOT NULL,
    DebitedAmount       decimal(19,2)   NOT NULL,
    DebitedOn           date            NOT NULL,
    CustodianReference  varchar(50)     NULL,
    CONSTRAINT FK_FeeDebits_Invoices FOREIGN KEY (InvoiceId) REFERENCES dbo.Invoices (Id)
);
GO

-------------------------------------------------------------------------------
-- Custodian ingestion staging
-------------------------------------------------------------------------------
-- Payload is a BinaryFormatter-serialized List<Position>. See FeeBilling.Ingestion.Wcf.
CREATE TABLE dbo.StagedBatches
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_StagedBatches PRIMARY KEY,
    CustodianCode   varchar(10)     NULL,
    FileName        nvarchar(260)   NULL,
    Payload         varbinary(max)  NOT NULL,
    Status          varchar(20)     NOT NULL,       -- 'Received' | 'Processed' | 'Failed' | 'Archived'
    ReceivedOn      datetime        NOT NULL CONSTRAINT DF_StagedBatches_ReceivedOn DEFAULT (GETDATE()),
    ProcessedOn     datetime        NULL
);
GO

-------------------------------------------------------------------------------
-- Forms authentication (custom membership)
-------------------------------------------------------------------------------
CREATE TABLE dbo.AppUsers
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppUsers PRIMARY KEY,
    UserName        nvarchar(100)   NOT NULL,
    Email           nvarchar(256)   NULL,
    PasswordHash    varchar(100)    NOT NULL,       -- base64(SHA1(salt + password))
    PasswordSalt    varchar(50)     NOT NULL,
    FirmId          int             NULL,           -- NULL = vendor staff (sees all firms)
    IsLockedOut     bit             NOT NULL CONSTRAINT DF_AppUsers_IsLockedOut DEFAULT (0),
    LastLoginOn     datetime        NULL,
    CreatedOn       datetime        NOT NULL CONSTRAINT DF_AppUsers_CreatedOn DEFAULT (GETDATE()),
    CONSTRAINT UQ_AppUsers_UserName UNIQUE (UserName),
    CONSTRAINT FK_AppUsers_Firms FOREIGN KEY (FirmId) REFERENCES dbo.Firms (Id)
);
GO

CREATE TABLE dbo.AppRoles
(
    Id              int             IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppRoles PRIMARY KEY,
    Name            nvarchar(50)    NOT NULL,
    CONSTRAINT UQ_AppRoles_Name UNIQUE (Name)
);
GO

CREATE TABLE dbo.AppUserRoles
(
    UserId          int             NOT NULL,
    RoleId          int             NOT NULL,
    CONSTRAINT PK_AppUserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_AppUserRoles_AppUsers FOREIGN KEY (UserId) REFERENCES dbo.AppUsers (Id),
    CONSTRAINT FK_AppUserRoles_AppRoles FOREIGN KEY (RoleId) REFERENCES dbo.AppRoles (Id)
);
GO
