/*
    FeeBillingReporting schema.

    FeeFacts is populated by the legacy BillingRunner in the SAME TransactionScope that writes
    Invoices to the FeeBilling database. Two connections in one scope = MSDTC promotion.
*/
USE FeeBillingReporting;
GO

CREATE TABLE dbo.FeeFacts
(
    Id              bigint          IDENTITY(1,1) NOT NULL CONSTRAINT PK_FeeFacts PRIMARY KEY,
    RunId           int             NOT NULL,
    FirmId          int             NOT NULL,
    AccountId       int             NOT NULL,
    HouseholdId     int             NULL,
    PeriodEnd       date            NOT NULL,
    ScheduleCode    varchar(20)     NULL,
    BillableAum     decimal(19,4)   NULL,
    FeeAmount       decimal(19,2)   NOT NULL,
    LoadedOn        datetime        NOT NULL CONSTRAINT DF_FeeFacts_LoadedOn DEFAULT (GETDATE())
);
GO

CREATE NONCLUSTERED INDEX IX_FeeFacts_Firm_Period ON dbo.FeeFacts (FirmId, PeriodEnd);
GO
