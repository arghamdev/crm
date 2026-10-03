using Crm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Crm.Infrastructure.Migrations;

/// <summary>Customer form: individual/legal profile (one row per customer), logo bytes kept apart from the profile, and structured contact person fields.</summary>
[DbContext(typeof(CrmDbContext))]
[Migration("202610030014_CustomerProfiles")]
public sealed class CustomerProfiles : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
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
""");
    protected override void Down(MigrationBuilder m) => m.Sql("""
DROP TABLE [crm].[CustomerProfiles];
DROP TABLE [crm].[CustomerLogos];
DROP INDEX [IX_CustomerContacts_CompanyId_Mobile] ON [crm].[CustomerContacts];
ALTER TABLE [crm].[CustomerContacts] DROP COLUMN [Title], [FirstName], [LastName], [Mobile], [Extension], [Notes];
""");
}
