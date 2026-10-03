-- Generated from 202610030014_CustomerProfiles.cs. Review on a disposable restored database before deployment.
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @migrationLock int;
EXEC @migrationLock = sys.sp_getapplock @Resource=N'EnterpriseCrm.SchemaMigration', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=15000;
IF @migrationLock < 0 THROW 51200, 'Migration lock could not be acquired.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory',N'U') IS NULL THROW 51201, 'EF migration history is required; apply the baseline first.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030013_DealerIncentives') THROW 51202, 'Required baseline migration is missing.', 1;
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId]=N'202610030014_CustomerProfiles')
BEGIN
ALTER TABLE [crm].[CustomerContacts] ADD [Extension] nvarchar(10) NULL;
ALTER TABLE [crm].[CustomerContacts] ADD [FirstName] nvarchar(100) NULL;
ALTER TABLE [crm].[CustomerContacts] ADD [LastName] nvarchar(100) NULL;
ALTER TABLE [crm].[CustomerContacts] ADD [Mobile] nvarchar(20) NULL;
ALTER TABLE [crm].[CustomerContacts] ADD [Notes] nvarchar(1000) NULL;
ALTER TABLE [crm].[CustomerContacts] ADD [Title] nvarchar(40) NULL;
CREATE TABLE [crm].[CustomerLogos] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [ContentType] nvarchar(40) NOT NULL,
    [Content] varbinary(max) NOT NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_CustomerLogos] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustomerLogos_Customers_Id] FOREIGN KEY ([Id]) REFERENCES [crm].[Customers] ([Id])
);
CREATE TABLE [crm].[CustomerProfiles] (
    [Id] uniqueidentifier NOT NULL,
    [CompanyId] nvarchar(32) NOT NULL,
    [ActivityType] nvarchar(80) NULL,
    [Title] nvarchar(40) NULL,
    [FirstName] nvarchar(100) NULL,
    [LastName] nvarchar(100) NULL,
    [Position] nvarchar(80) NULL,
    [Mobile1] nvarchar(20) NULL,
    [Mobile2] nvarchar(20) NULL,
    [Phone1] nvarchar(20) NULL,
    [Phone2] nvarchar(20) NULL,
    [Province] nvarchar(60) NULL,
    [Notes] nvarchar(2000) NULL,
    [LegalName] nvarchar(200) NULL,
    [EconomicCode] nvarchar(14) NULL,
    [RegistrationNumber] nvarchar(20) NULL,
    [RepresentativeNationalCode] nvarchar(10) NULL,
    [BirthCertificateNumber] nvarchar(20) NULL,
    [BirthDate] date NULL,
    [EmployeeCount] nvarchar(40) NULL,
    [AccountingCode] nvarchar(40) NULL,
    [AcquaintanceDate] date NULL,
    [SoftwarePurchase] nvarchar(200) NULL,
    [BranchSubscriptionCode] nvarchar(40) NULL,
    [ReferralSource] nvarchar(120) NULL,
    [Website] nvarchar(200) NULL,
    [LogoContentType] nvarchar(40) NULL,
    [CreatedAtUtc] datetimeoffset(3) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(3) NOT NULL,
    [Version] bigint NOT NULL,
    CONSTRAINT [PK_CustomerProfiles] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_CustomerProfiles_Customers_Id] FOREIGN KEY ([Id]) REFERENCES [crm].[Customers] ([Id])
);
EXEC(N'CREATE INDEX [IX_CustomerContacts_CompanyId_Mobile] ON [crm].[CustomerContacts] ([CompanyId], [Mobile]);');
CREATE INDEX [IX_CustomerProfiles_CompanyId_AccountingCode] ON [crm].[CustomerProfiles] ([CompanyId], [AccountingCode]);
CREATE INDEX [IX_CustomerProfiles_CompanyId_Mobile1] ON [crm].[CustomerProfiles] ([CompanyId], [Mobile1]);
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId],[ProductVersion]) VALUES (N'202610030014_CustomerProfiles',N'10.0.12');
END;
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
THROW;
END CATCH;
