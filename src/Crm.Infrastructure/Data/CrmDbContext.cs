using Crm.Domain.Commercial;
using Crm.Domain.SelfService;
using Crm.Domain.Channel;
using Crm.Domain.Common;
using Crm.Domain.Customers;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Domain.Sales;
using Crm.Domain.Service;
using Crm.Domain.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Crm.Infrastructure.Data;

public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsIdGenerator, CompatibleMigrationsIdGenerator>();
    }
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<CustomerLogo> CustomerLogos => Set<CustomerLogo>();
    public DbSet<CustomerTimelineEvent> CustomerTimelineEvents => Set<CustomerTimelineEvent>();
    public DbSet<CustomerOwnershipHistory> CustomerOwnershipHistory => Set<CustomerOwnershipHistory>();
    public DbSet<CustomerDuplicateCandidate> CustomerDuplicateCandidates => Set<CustomerDuplicateCandidate>();
    public DbSet<CustomerMergeOperation> CustomerMergeOperations => Set<CustomerMergeOperation>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadStatusHistory> LeadStatusHistory => Set<LeadStatusHistory>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<OpportunityStageHistory> OpportunityStageHistory => Set<OpportunityStageHistory>();
    public DbSet<OpportunityActivity> OpportunityActivities => Set<OpportunityActivity>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();
    public DbSet<QuoteStatusHistory> QuoteStatusHistory => Set<QuoteStatusHistory>();
    public DbSet<QuoteApprovalDecision> QuoteApprovalDecisions => Set<QuoteApprovalDecision>();
    public DbSet<OrderRequest> OrderRequests => Set<OrderRequest>();
    public DbSet<OrderCreditDecision> OrderCreditDecisions => Set<OrderCreditDecision>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<OrderIntegrationMessage> OrderIntegrationMessages => Set<OrderIntegrationMessage>();
    public DbSet<OrderIntegrationAttempt> OrderIntegrationAttempts => Set<OrderIntegrationAttempt>();
    public DbSet<Dealer> Dealers => Set<Dealer>();
    public DbSet<DealerContract> DealerContracts => Set<DealerContract>();
    public DbSet<DealerTerritoryAssignment> DealerTerritoryAssignments => Set<DealerTerritoryAssignment>();
    public DbSet<DealerCustomerAssignment> DealerCustomerAssignments => Set<DealerCustomerAssignment>();
    public DbSet<DealerTarget> DealerTargets => Set<DealerTarget>();
    public DbSet<DealerFinancialSnapshot> DealerFinancialSnapshots => Set<DealerFinancialSnapshot>();
    public DbSet<DealerPerformanceSnapshot> DealerPerformanceSnapshots => Set<DealerPerformanceSnapshot>();
    public DbSet<DealerStatusHistory> DealerStatusHistory => Set<DealerStatusHistory>();
    public DbSet<CrmWorkItem> WorkItems => Set<CrmWorkItem>();
    public DbSet<CrmUser> Users => Set<CrmUser>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();
    public DbSet<Territory> Territories => Set<Territory>();
    public DbSet<OrganizationChange> OrganizationChanges => Set<OrganizationChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureSelfService(modelBuilder);
        ConfigureServiceDesk(modelBuilder);
        ConfigureRoleCatalog(modelBuilder);
        ConfigureDealerIncentives(modelBuilder);
        ConfigureCompany(modelBuilder.Entity<Company>());
        ConfigureOrganizationUnit(modelBuilder.Entity<OrganizationUnit>());
        ConfigureTerritory(modelBuilder.Entity<Territory>());
        ConfigureOrganizationChange(modelBuilder.Entity<OrganizationChange>());
        ConfigureCustomer(modelBuilder.Entity<Customer>());
        ConfigureCustomerContact(modelBuilder.Entity<CustomerContact>());
        ConfigureCustomerAddress(modelBuilder.Entity<CustomerAddress>());
        ConfigureCustomerProfile(modelBuilder);
        ConfigureNotifications(modelBuilder);
        ConfigureAccountFile(modelBuilder);
        ConfigureFollowUps(modelBuilder);
        ConfigureCustomerTimeline(modelBuilder.Entity<CustomerTimelineEvent>());
        ConfigureCustomerOwnership(modelBuilder.Entity<CustomerOwnershipHistory>());
        ConfigureCustomerDuplicate(modelBuilder.Entity<CustomerDuplicateCandidate>());
        ConfigureCustomerMerge(modelBuilder.Entity<CustomerMergeOperation>());
        ConfigureLead(modelBuilder.Entity<Lead>());
        ConfigureLeadStatusHistory(modelBuilder.Entity<LeadStatusHistory>());
        ConfigureOpportunity(modelBuilder.Entity<Opportunity>());
        ConfigureOpportunityStageHistory(modelBuilder.Entity<OpportunityStageHistory>());
        ConfigureOpportunityActivity(modelBuilder.Entity<OpportunityActivity>());
        ConfigureQuote(modelBuilder.Entity<Quote>());
        ConfigureQuoteLine(modelBuilder.Entity<QuoteLine>());
        ConfigureQuoteStatusHistory(modelBuilder.Entity<QuoteStatusHistory>());
        ConfigureQuoteApprovalDecision(modelBuilder.Entity<QuoteApprovalDecision>());
        ConfigureOrderRequest(modelBuilder.Entity<OrderRequest>());
        ConfigureOrderCreditDecision(modelBuilder.Entity<OrderCreditDecision>());
        ConfigureOrderStatusHistory(modelBuilder.Entity<OrderStatusHistory>());
        ConfigureOrderIntegrationMessage(modelBuilder.Entity<OrderIntegrationMessage>());
        ConfigureOrderIntegrationAttempt(modelBuilder.Entity<OrderIntegrationAttempt>());
        ConfigureDealer(modelBuilder.Entity<Dealer>());
        ConfigureDealerContract(modelBuilder.Entity<DealerContract>());
        ConfigureDealerTerritory(modelBuilder.Entity<DealerTerritoryAssignment>());
        ConfigureDealerCustomer(modelBuilder.Entity<DealerCustomerAssignment>());
        ConfigureDealerTarget(modelBuilder.Entity<DealerTarget>());
        ConfigureDealerFinancial(modelBuilder.Entity<DealerFinancialSnapshot>());
        ConfigureDealerPerformance(modelBuilder.Entity<DealerPerformanceSnapshot>());
        ConfigureDealerStatusHistory(modelBuilder.Entity<DealerStatusHistory>());
        ConfigureWorkItem(modelBuilder.Entity<CrmWorkItem>());
        ConfigureUser(modelBuilder.Entity<CrmUser>());
        ConfigureExternalIdentity(modelBuilder.Entity<ExternalIdentity>());
        ConfigureSession(modelBuilder.Entity<UserSession>());
        ConfigureRoleAssignment(modelBuilder.Entity<UserRoleAssignment>());
        ConfigureAudit(modelBuilder.Entity<SecurityAuditEvent>());
    }

    private static void ConfigureCompany(EntityTypeBuilder<Company> entity)
    {
        ConfigureEntity(entity, "Companies", "org");
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.TimeZoneId).HasMaxLength(80).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        entity.HasIndex(x => x.CompanyId).IsUnique();
        entity.HasIndex(x => x.Code).IsUnique();
    }

    private static void ConfigureOrganizationUnit(EntityTypeBuilder<OrganizationUnit> entity)
    {
        ConfigureEntity(entity, "OrganizationUnits", "org");
        entity.Property(x => x.UnitId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.ParentUnitId).HasMaxLength(32);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        entity.HasIndex(x => new { x.CompanyId, x.UnitId }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.ParentUnitId });
    }

    private static void ConfigureTerritory(EntityTypeBuilder<Territory> entity)
    {
        ConfigureEntity(entity, "Territories", "org");
        entity.Property(x => x.TerritoryId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Dimension).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.ValidFromUtc).HasPrecision(3);
        entity.Property(x => x.ValidToUtc).HasPrecision(3);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        entity.HasIndex(x => new { x.CompanyId, x.TerritoryId }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.ValidFromUtc });
    }

    private static void ConfigureOrganizationChange(EntityTypeBuilder<OrganizationChange> entity)
    {
        entity.Property(x => x.ActorUserId);
        ConfigureEntity(entity, "OrganizationChanges", "org");
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
        entity.Property(x => x.EntityBusinessId).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Action).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Summary).HasMaxLength(500).IsRequired();
        entity.Property(x => x.OccurredAtUtc).HasPrecision(3);
        entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.BeforeValue).HasColumnType("nvarchar(max)");
        entity.Property(x => x.AfterValue).HasColumnType("nvarchar(max)");
        entity.HasIndex(x => new { x.CompanyId, x.OccurredAtUtc });
    }

    private static void ConfigureCustomer(EntityTypeBuilder<Customer> entity)
    {
        ConfigureEntity(entity, "Customers", "crm");
        entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.City).HasMaxLength(100);
        entity.Property(x => x.Owner).HasMaxLength(150);
        Scope(entity);
        entity.Property(x => x.Branch).HasMaxLength(150);
        entity.Property(x => x.Segment).HasMaxLength(80);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.CreditLimit).HasPrecision(18, 2);
        entity.Property(x => x.Balance).HasPrecision(18, 2);
        entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.NationalId).HasMaxLength(32);
        entity.Property(x => x.PrimaryPhone).HasMaxLength(40);
        entity.Property(x => x.PrimaryEmail).HasMaxLength(256);
        entity.Property(x => x.DataSource).HasMaxLength(80).IsRequired();
        entity.Property(x => x.LastSynchronizedAtUtc).HasPrecision(3);
        entity.Property(x => x.RelationshipType).HasConversion<string>().HasMaxLength(16).HasDefaultValue(Crm.Domain.Accounts.AccountRelationship.Customer);
        entity.Property(x => x.Tags).HasMaxLength(400);
        entity.Ignore(x => x.TagList);
        entity.HasIndex(x => x.ParentCustomerId);
        entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.ParentCustomerId).OnDelete(DeleteBehavior.NoAction);
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.NationalId }).IsUnique().HasFilter("[NationalId] IS NOT NULL");
        entity.HasIndex(x => new { x.CompanyId, x.BranchId, x.TerritoryId });
    }

    private static void ConfigureCustomerContact(EntityTypeBuilder<CustomerContact> entity)
    {
        ConfigureEntity(entity, "CustomerContacts", "crm");
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Role).HasMaxLength(100);
        entity.Property(x => x.Phone).HasMaxLength(40);
        entity.Property(x => x.Email).HasMaxLength(256);
        entity.Property(x => x.ConsentStatus).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.Title).HasMaxLength(40);
        entity.Property(x => x.FirstName).HasMaxLength(100);
        entity.Property(x => x.LastName).HasMaxLength(100);
        entity.Property(x => x.Mobile).HasMaxLength(20);
        entity.Property(x => x.Extension).HasMaxLength(10);
        entity.Property(x => x.Notes).HasMaxLength(1000);
        entity.HasIndex(x => new { x.CompanyId, x.CustomerId, x.IsActive });
        entity.HasIndex(x => new { x.CompanyId, x.Mobile });
        entity.HasIndex(x => new { x.CompanyId, x.Phone });
        entity.HasIndex(x => new { x.CompanyId, x.Email });
    }

    private static void ConfigureCustomerProfile(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerProfile>(entity =>
        {
            ConfigureEntity(entity, "CustomerProfiles", "crm");
            entity.Ignore(x => x.CustomerId);
            entity.Ignore(x => x.PersonName);
            entity.Ignore(x => x.Data);
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ActivityType).HasMaxLength(80);
            entity.Property(x => x.Title).HasMaxLength(40);
            entity.Property(x => x.FirstName).HasMaxLength(100);
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.Position).HasMaxLength(80);
            entity.Property(x => x.Mobile1).HasMaxLength(20);
            entity.Property(x => x.Mobile2).HasMaxLength(20);
            entity.Property(x => x.Phone1).HasMaxLength(20);
            entity.Property(x => x.Phone2).HasMaxLength(20);
            entity.Property(x => x.Province).HasMaxLength(60);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.LegalName).HasMaxLength(200);
            entity.Property(x => x.EconomicCode).HasMaxLength(14);
            entity.Property(x => x.RegistrationNumber).HasMaxLength(20);
            entity.Property(x => x.RepresentativeNationalCode).HasMaxLength(10);
            entity.Property(x => x.BirthCertificateNumber).HasMaxLength(20);
            entity.Property(x => x.EmployeeCount).HasMaxLength(40);
            entity.Property(x => x.AccountingCode).HasMaxLength(40);
            entity.Property(x => x.SoftwarePurchase).HasMaxLength(200);
            entity.Property(x => x.BranchSubscriptionCode).HasMaxLength(40);
            entity.Property(x => x.ReferralSource).HasMaxLength(120);
            entity.Property(x => x.Website).HasMaxLength(200);
            entity.Property(x => x.LogoContentType).HasMaxLength(40);
            entity.HasIndex(x => new { x.CompanyId, x.Mobile1 });
            entity.HasIndex(x => new { x.CompanyId, x.AccountingCode });
            entity.HasOne<Customer>().WithOne().HasForeignKey<CustomerProfile>(x => x.Id).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CustomerLogo>(entity =>
        {
            ConfigureEntity(entity, "CustomerLogos", "crm");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Content).HasColumnType("varbinary(max)").IsRequired();
            entity.HasOne<Customer>().WithOne().HasForeignKey<CustomerLogo>(x => x.Id).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureCustomerAddress(EntityTypeBuilder<CustomerAddress> entity)
    {
        ConfigureEntity(entity, "CustomerAddresses", "crm");
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.Title).HasMaxLength(120).IsRequired();
        entity.Property(x => x.Province).HasMaxLength(100);
        entity.Property(x => x.City).HasMaxLength(100);
        entity.Property(x => x.AddressLine).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.PostalCode).HasMaxLength(20);
        entity.HasIndex(x => new { x.CompanyId, x.CustomerId, x.IsActive });
    }

    private static void ConfigureCustomerTimeline(EntityTypeBuilder<CustomerTimelineEvent> entity)
    {
        entity.Property(x => x.ActorUserId);
        ConfigureEntity(entity, "CustomerTimelineEvents", "crm");
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Description).HasMaxLength(2000);
        entity.Property(x => x.OccurredAtUtc).HasPrecision(3);
        entity.Property(x => x.Source).HasMaxLength(80).IsRequired();
        entity.Property(x => x.SourceReference).HasMaxLength(120);
        entity.HasIndex(x => new { x.CompanyId, x.CustomerId, x.OccurredAtUtc });
    }

    private static void ConfigureCustomerOwnership(EntityTypeBuilder<CustomerOwnershipHistory> entity)
    {
        entity.Property(x => x.ChangedByUserId);
        ConfigureEntity(entity, "CustomerOwnershipHistory", "crm");
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.BranchId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.TerritoryId).HasMaxLength(32);
        entity.Property(x => x.Owner).HasMaxLength(150).IsRequired();
        entity.Property(x => x.ValidFromUtc).HasPrecision(3);
        entity.Property(x => x.ValidToUtc).HasPrecision(3);
        entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        entity.HasIndex(x => new { x.CompanyId, x.CustomerId, x.ValidToUtc });
    }

    private static void ConfigureCustomerDuplicate(EntityTypeBuilder<CustomerDuplicateCandidate> entity)
    {
        entity.Property(x => x.Score);
        ConfigureEntity(entity, "CustomerDuplicateCandidates", "crm");
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Reasons).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.DetectedAtUtc).HasPrecision(3);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.ReviewedAtUtc).HasPrecision(3);
        entity.Property(x => x.ReviewNote).HasMaxLength(1000);
        entity.HasIndex(x => new { x.CompanyId, x.Status, x.DetectedAtUtc });
        entity.HasIndex(x => new { x.CustomerId, x.PossibleDuplicateCustomerId });
    }

    private static void ConfigureLead(EntityTypeBuilder<Lead> entity)
    {
        ConfigureEntity(entity, "Leads", "sales");
        entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
        entity.Property(x => x.Contact).HasMaxLength(200);
        entity.Property(x => x.Source).HasMaxLength(80);
        entity.Property(x => x.Owner).HasMaxLength(150);
        entity.Property(x => x.Phone).HasMaxLength(40);
        entity.Property(x => x.Email).HasMaxLength(256);
        entity.Property(x => x.AssignedAtUtc).HasPrecision(3);
        entity.Property(x => x.FirstContactDueAtUtc).HasPrecision(3);
        entity.Property(x => x.FirstContactAtUtc).HasPrecision(3);
        entity.Property(x => x.LastActivityAtUtc).HasPrecision(3);
        entity.Property(x => x.NextAction).HasMaxLength(300);
        entity.Property(x => x.NextActionAtUtc).HasPrecision(3);
        entity.Property(x => x.StatusReason).HasMaxLength(1000);
        Scope(entity);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.BranchId, x.TerritoryId });
        entity.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.Status, x.FirstContactDueAtUtc });
        entity.HasIndex(x => new { x.CompanyId, x.CustomerId });
        entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Opportunity>().WithMany().HasForeignKey(x => x.ConvertedOpportunityId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureLeadStatusHistory(EntityTypeBuilder<LeadStatusHistory> entity)
    {
        ConfigureEntity(entity, "LeadStatusHistory", "sales");
        Scope(entity);
        entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.ChangedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.CompanyId, x.LeadId, x.ChangedAtUtc });
        entity.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOpportunity(EntityTypeBuilder<Opportunity> entity)
    {
        ConfigureEntity(entity, "Opportunities", "sales");
        entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
        entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired().HasDefaultValue("IRR");
        entity.HasIndex(x => x.ContactId);
        entity.Property(x => x.Title).HasMaxLength(250).IsRequired();
        entity.Property(x => x.Customer).HasMaxLength(200);
        entity.Property(x => x.Value).HasPrecision(18, 2);
        entity.Property(x => x.Owner).HasMaxLength(150);
        entity.Property(x => x.ExpectedCloseAtUtc).HasPrecision(3);
        entity.Property(x => x.Source).HasMaxLength(80).IsRequired();
        entity.Property(x => x.NextAction).HasMaxLength(300);
        entity.Property(x => x.NextActionAtUtc).HasPrecision(3);
        entity.Property(x => x.LastActivityAtUtc).HasPrecision(3);
        entity.Property(x => x.Competitor).HasMaxLength(200);
        entity.Property(x => x.RiskLevel).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.OutcomeReason).HasMaxLength(1000);
        entity.Property(x => x.ClosedAtUtc).HasPrecision(3);
        Scope(entity);
        entity.Property(x => x.Stage).HasConversion<string>().HasMaxLength(24);
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.BranchId, x.TerritoryId });
        entity.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.Stage, x.ExpectedCloseAtUtc });
        entity.HasIndex(x => new { x.CompanyId, x.CustomerId });
        entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Lead>().WithMany().HasForeignKey(x => x.OriginLeadId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOpportunityStageHistory(EntityTypeBuilder<OpportunityStageHistory> entity)
    {
        entity.Property(x => x.Probability);
        ConfigureEntity(entity, "OpportunityStageHistory", "sales");
        Scope(entity);
        entity.Property(x => x.FromStage).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.ToStage).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.ChangedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.CompanyId, x.OpportunityId, x.ChangedAtUtc });
        entity.HasOne<Opportunity>().WithMany().HasForeignKey(x => x.OpportunityId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOpportunityActivity(EntityTypeBuilder<OpportunityActivity> entity)
    {
        ConfigureEntity(entity, "OpportunityActivities", "sales");
        Scope(entity);
        entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.Subject).HasMaxLength(250).IsRequired();
        entity.Property(x => x.Outcome).HasMaxLength(2000).IsRequired();
        entity.Property(x => x.OccurredAtUtc).HasPrecision(3);
        entity.Property(x => x.NextAction).HasMaxLength(300);
        entity.Property(x => x.NextActionAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.CompanyId, x.OpportunityId, x.OccurredAtUtc });
        entity.HasOne<Opportunity>().WithMany().HasForeignKey(x => x.OpportunityId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureQuote(EntityTypeBuilder<Quote> entity)
    {
        ConfigureEntity(entity, "Quotes", "commercial");
        entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Customer).HasMaxLength(200);
        entity.Property(x => x.Opportunity).HasMaxLength(250);
        entity.Property(x => x.CurrencyCode).HasMaxLength(8).IsRequired();
        entity.Property(x => x.PaymentTerms).HasMaxLength(500).IsRequired();
        entity.Property(x => x.ValidUntilUtc).HasPrecision(3);
        entity.Property(x => x.SubmittedAtUtc).HasPrecision(3);
        entity.Property(x => x.ApprovedAtUtc).HasPrecision(3);
        entity.Property(x => x.SentAtUtc).HasPrecision(3);
        entity.Property(x => x.AcceptedAtUtc).HasPrecision(3);
        entity.Property(x => x.Amount).HasPrecision(18, 2);
        entity.Property(x => x.GrossAmount).HasPrecision(18, 2);
        entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        entity.Property(x => x.DiscountPercent).HasPrecision(5, 2);
        entity.Property(x => x.CostAmount).HasPrecision(18, 2);
        entity.Property(x => x.NetAmount).HasPrecision(18, 2);
        entity.Property(x => x.MarginPercent).HasPrecision(5, 2);
        Scope(entity);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.ApprovalLevel).HasConversion<string>().HasMaxLength(32);
        entity.Ignore(x => x.IsEditable);
        entity.HasIndex(x => new { x.CompanyId, x.Code, x.Revision }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.BranchId, x.TerritoryId });
        entity.HasIndex(x => new { x.CompanyId, x.CustomerId });
        entity.HasIndex(x => x.OpportunityId);
        entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Opportunity>().WithMany().HasForeignKey(x => x.OpportunityId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Quote>().WithMany().HasForeignKey(x => x.ParentQuoteId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureQuoteLine(EntityTypeBuilder<QuoteLine> entity)
    {
        ConfigureEntity(entity, "QuoteLines", "commercial");
        Scope(entity);
        entity.Property(x => x.ProductCode).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ProductName).HasMaxLength(250).IsRequired();
        entity.Property(x => x.Unit).HasMaxLength(32).IsRequired();
        entity.Property(x => x.Quantity).HasPrecision(18, 3);
        entity.Property(x => x.ListUnitPrice).HasPrecision(18, 2);
        entity.Property(x => x.StandardUnitCost).HasPrecision(18, 2);
        entity.Property(x => x.DiscountPercent).HasPrecision(5, 2);
        entity.Property(x => x.PriceSource).HasMaxLength(200).IsRequired();
        entity.Property(x => x.PriceEffectiveAtUtc).HasPrecision(3);
        entity.Ignore(x => x.GrossAmount);
        entity.Ignore(x => x.DiscountAmount);
        entity.Ignore(x => x.NetAmount);
        entity.Ignore(x => x.CostAmount);
        entity.Ignore(x => x.MarginPercent);
        entity.HasIndex(x => new { x.QuoteId, x.ProductCode }).IsUnique();
        entity.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureQuoteStatusHistory(EntityTypeBuilder<QuoteStatusHistory> entity)
    {
        ConfigureEntity(entity, "QuoteStatusHistory", "commercial");
        Scope(entity);
        entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.ChangedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.CompanyId, x.QuoteId, x.ChangedAtUtc });
        entity.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureQuoteApprovalDecision(EntityTypeBuilder<QuoteApprovalDecision> entity)
    {
        ConfigureEntity(entity, "QuoteApprovalDecisions", "commercial");
        Scope(entity);
        entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Decision).HasConversion<string>().HasMaxLength(16);
        entity.Property(x => x.Comment).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.DecidedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.QuoteId, x.Role }).IsUnique();
        entity.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOrderRequest(EntityTypeBuilder<OrderRequest> entity)
    {
        ConfigureEntity(entity, "OrderRequests", "commercial");
        Scope(entity);
        entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
        entity.Property(x => x.QuoteCode).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Customer).HasMaxLength(200).IsRequired();
        entity.Property(x => x.CurrencyCode).HasMaxLength(8).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.SubmissionIdempotencyKey).HasMaxLength(100).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.NetAmount).HasPrecision(18, 2);
        entity.Property(x => x.CreditLimit).HasPrecision(18, 2);
        entity.Property(x => x.CreditUsed).HasPrecision(18, 2);
        entity.Property(x => x.OverdueAmount).HasPrecision(18, 2);
        entity.Property(x => x.AvailableCredit).HasPrecision(18, 2);
        entity.Property(x => x.CreditReason).HasMaxLength(1000);
        entity.Property(x => x.CreditSource).HasMaxLength(200);
        entity.Property(x => x.CreditSnapshotAtUtc).HasPrecision(3);
        entity.Property(x => x.CreditOverrideExpiresAtUtc).HasPrecision(3);
        entity.Property(x => x.ErpOrderNumber).HasMaxLength(100);
        entity.Property(x => x.DeliveryReference).HasMaxLength(100);
        entity.Property(x => x.InvoiceNumber).HasMaxLength(100);
        entity.Property(x => x.PaymentReference).HasMaxLength(100);
        entity.Property(x => x.LastIntegrationError).HasMaxLength(2000);
        entity.Property(x => x.SubmittedAtUtc).HasPrecision(3);
        entity.Property(x => x.LastSynchronizedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => x.QuoteId).IsUnique();
        entity.HasIndex(x => x.SubmissionIdempotencyKey).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.BranchId, x.TerritoryId, x.Status });
        entity.HasIndex(x => new { x.CompanyId, x.CustomerId });
        entity.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Opportunity>().WithMany().HasForeignKey(x => x.OpportunityId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOrderCreditDecision(EntityTypeBuilder<OrderCreditDecision> entity)
    {
        ConfigureEntity(entity, "OrderCreditDecisions", "commercial");
        Scope(entity);
        entity.Property(x => x.Decision).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.CreditLimit).HasPrecision(18, 2);
        entity.Property(x => x.CreditUsed).HasPrecision(18, 2);
        entity.Property(x => x.OverdueAmount).HasPrecision(18, 2);
        entity.Property(x => x.AvailableCredit).HasPrecision(18, 2);
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.Source).HasMaxLength(200).IsRequired();
        entity.Property(x => x.DecidedAtUtc).HasPrecision(3);
        entity.Property(x => x.ExpiresAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.CompanyId, x.OrderRequestId, x.DecidedAtUtc });
        entity.HasOne<OrderRequest>().WithMany().HasForeignKey(x => x.OrderRequestId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOrderStatusHistory(EntityTypeBuilder<OrderStatusHistory> entity)
    {
        ConfigureEntity(entity, "OrderStatusHistory", "commercial");
        Scope(entity);
        entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        entity.Property(x => x.Source).HasMaxLength(200).IsRequired();
        entity.Property(x => x.ChangedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.CompanyId, x.OrderRequestId, x.ChangedAtUtc });
        entity.HasOne<OrderRequest>().WithMany().HasForeignKey(x => x.OrderRequestId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOrderIntegrationMessage(EntityTypeBuilder<OrderIntegrationMessage> entity)
    {
        ConfigureEntity(entity, "OrderIntegrationMessages", "integration");
        Scope(entity);
        entity.Property(x => x.MessageType).HasMaxLength(80).IsRequired();
        entity.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.PayloadFingerprint).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.NextAttemptAtUtc).HasPrecision(3);
        entity.Property(x => x.LastError).HasMaxLength(2000);
        entity.Property(x => x.ExternalReference).HasMaxLength(100);
        entity.Property(x => x.CompletedAtUtc).HasPrecision(3);
        entity.HasIndex(x => x.OrderRequestId).IsUnique();
        entity.HasIndex(x => x.IdempotencyKey).IsUnique();
        entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        entity.HasOne<OrderRequest>().WithMany().HasForeignKey(x => x.OrderRequestId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureOrderIntegrationAttempt(EntityTypeBuilder<OrderIntegrationAttempt> entity)
    {
        ConfigureEntity(entity, "OrderIntegrationAttempts", "integration");
        Scope(entity);
        entity.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Detail).HasMaxLength(2000).IsRequired();
        entity.Property(x => x.ExternalReference).HasMaxLength(100);
        entity.Property(x => x.AttemptedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.MessageId, x.AttemptNumber }).IsUnique();
        entity.HasIndex(x => new { x.OrderRequestId, x.AttemptedAtUtc });
        entity.HasOne<OrderIntegrationMessage>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<OrderRequest>().WithMany().HasForeignKey(x => x.OrderRequestId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureDealer(EntityTypeBuilder<Dealer> entity)
    {
        ConfigureEntity(entity, "Dealers", "channel");
        Scope(entity);
        entity.Property(x => x.DealerId).HasMaxLength(64).IsRequired();
        entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
        entity.Property(x => x.LegalName).HasMaxLength(250).IsRequired();
        entity.Property(x => x.TradeName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.City).HasMaxLength(100).IsRequired();
        entity.Property(x => x.NationalId).HasMaxLength(32);
        entity.Property(x => x.Phone).HasMaxLength(40);
        entity.Property(x => x.Email).HasMaxLength(256);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.StatusReason).HasMaxLength(1000);
        entity.Property(x => x.ActivatedAtUtc).HasPrecision(3);
        entity.Property(x => x.TerminatedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.CompanyId, x.DealerId }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        entity.HasIndex(x => new { x.CompanyId, x.NationalId }).IsUnique().HasFilter("[NationalId] IS NOT NULL");
        entity.HasIndex(x => new { x.CompanyId, x.BranchId, x.TerritoryId, x.Status });
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ChannelManagerUserId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureDealerContract(EntityTypeBuilder<DealerContract> entity)
    {
        ConfigureEntity(entity, "DealerContracts", "channel");
        Scope(entity);
        entity.Property(x => x.ContractNumber).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ValidFromUtc).HasPrecision(3);
        entity.Property(x => x.ValidToUtc).HasPrecision(3);
        entity.Property(x => x.AnnualTarget).HasPrecision(18, 2);
        entity.Property(x => x.PaymentTerms).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.ApprovedAtUtc).HasPrecision(3);
        entity.Property(x => x.DecisionReason).HasMaxLength(1000);
        entity.HasIndex(x => new { x.CompanyId, x.ContractNumber }).IsUnique();
        entity.HasIndex(x => new { x.DealerId, x.Status, x.ValidToUtc });
        entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureDealerTerritory(EntityTypeBuilder<DealerTerritoryAssignment> entity)
    {
        entity.Property(x => x.IsExclusive);
        ConfigureEntity(entity, "DealerTerritoryAssignments", "channel");
        Scope(entity);
        entity.Property(x => x.TerritoryId).IsRequired();
        entity.Property(x => x.ValidFromUtc).HasPrecision(3);
        entity.Property(x => x.ValidToUtc).HasPrecision(3);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.ApprovedAtUtc).HasPrecision(3);
        entity.Property(x => x.Reason).HasMaxLength(1000);
        entity.HasIndex(x => new { x.CompanyId, x.TerritoryId, x.Status, x.ValidFromUtc });
        entity.HasIndex(x => new { x.DealerId, x.TerritoryId, x.Status });
        entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureDealerCustomer(EntityTypeBuilder<DealerCustomerAssignment> entity)
    {
        ConfigureEntity(entity, "DealerCustomerAssignments", "channel");
        Scope(entity);
        entity.Property(x => x.ValidFromUtc).HasPrecision(3);
        entity.Property(x => x.ValidToUtc).HasPrecision(3);
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.EndReason).HasMaxLength(1000);
        entity.Ignore(x => x.IsActive);
        entity.HasIndex(x => new { x.CompanyId, x.CustomerId, x.ValidToUtc });
        entity.HasIndex(x => new { x.DealerId, x.ValidToUtc });
        entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.EndedByUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureDealerTarget(EntityTypeBuilder<DealerTarget> entity)
    {
        ConfigureEntity(entity, "DealerTargets", "channel");
        Scope(entity);
        entity.Property(x => x.PeriodFromUtc).HasPrecision(3);
        entity.Property(x => x.PeriodToUtc).HasPrecision(3);
        entity.Property(x => x.Amount).HasPrecision(18, 2);
        entity.Property(x => x.Source).HasMaxLength(200).IsRequired();
        entity.HasIndex(x => new { x.DealerId, x.PeriodFromUtc, x.PeriodToUtc }).IsUnique();
        entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.SetByUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureDealerFinancial(EntityTypeBuilder<DealerFinancialSnapshot> entity)
    {
        ConfigureEntity(entity, "DealerFinancialSnapshots", "channel");
        Scope(entity);
        entity.Property(x => x.CreditLimit).HasPrecision(18, 2);
        entity.Property(x => x.CreditUsed).HasPrecision(18, 2);
        entity.Property(x => x.Balance).HasPrecision(18, 2);
        entity.Property(x => x.OverdueAmount).HasPrecision(18, 2);
        entity.Ignore(x => x.AvailableCredit);
        entity.Property(x => x.Source).HasMaxLength(200).IsRequired();
        entity.Property(x => x.SynchronizedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.DealerId, x.SynchronizedAtUtc }).IsUnique();
        entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureDealerPerformance(EntityTypeBuilder<DealerPerformanceSnapshot> entity)
    {
        entity.Property(x => x.OrderCount);
        ConfigureEntity(entity, "DealerPerformanceSnapshots", "channel");
        Scope(entity);
        entity.Property(x => x.PeriodFromUtc).HasPrecision(3);
        entity.Property(x => x.PeriodToUtc).HasPrecision(3);
        entity.Property(x => x.NetSales).HasPrecision(18, 2);
        entity.Property(x => x.Source).HasMaxLength(200).IsRequired();
        entity.Property(x => x.SynchronizedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.DealerId, x.PeriodFromUtc, x.PeriodToUtc, x.SynchronizedAtUtc }).IsUnique();
        entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureDealerStatusHistory(EntityTypeBuilder<DealerStatusHistory> entity)
    {
        ConfigureEntity(entity, "DealerStatusHistory", "channel");
        Scope(entity);
        entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32);
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.ChangedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.DealerId, x.ChangedAtUtc });
        entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.Cascade);
        entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.NoAction);
    }

    private static void ConfigureCustomerMerge(EntityTypeBuilder<CustomerMergeOperation> entity)
    {
        entity.Property(x => x.SurvivorCustomerId);
        entity.Property(x => x.MergedByUserId);
        ConfigureEntity(entity, "CustomerMergeOperations", "crm");
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.MergedCustomerPreviousStatus).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.TransferManifestJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.MergedAtUtc).HasPrecision(3);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.RevertedAtUtc).HasPrecision(3);
        entity.Property(x => x.RevertReason).HasMaxLength(1000);
        entity.HasIndex(x => new { x.CompanyId, x.MergedAtUtc });
        entity.HasIndex(x => new { x.MergedCustomerId, x.Status });
        entity.HasIndex(x => x.DuplicateCandidateId).IsUnique()
            .HasDatabaseName("UX_CustomerMergeOperations_DuplicateCandidateId_Active").HasFilter("[Status] = N'Merged'");
        entity.HasIndex(x => x.MergedCustomerId).IsUnique()
            .HasDatabaseName("UX_CustomerMergeOperations_MergedCustomerId_Active").HasFilter("[Status] = N'Merged'");
    }

    private static void ConfigureWorkItem(EntityTypeBuilder<CrmWorkItem> entity)
    {
        ConfigureEntity(entity, "WorkItems", "crm");
        entity.Property(x => x.Title).HasMaxLength(250).IsRequired();
        entity.Property(x => x.Priority).HasMaxLength(24).IsRequired();
        entity.Property(x => x.DueAtUtc).HasPrecision(3);
        Scope(entity);
        entity.HasIndex(x => new { x.CompanyId, x.AssignedToUserId, x.IsDone });
    }

    private static void ConfigureUser(EntityTypeBuilder<CrmUser> entity)
    {
        ConfigureEntity(entity, "Users", "iam");
        entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        entity.Property(x => x.UserName).HasMaxLength(100).IsRequired();
        entity.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        entity.Property(x => x.EmployeeNumber).HasMaxLength(64);
        entity.Property(x => x.Culture).HasMaxLength(20);
        entity.Property(x => x.TimeZoneId).HasMaxLength(80);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.AccessValidFromUtc).HasPrecision(3);
        entity.Property(x => x.AccessValidToUtc).HasPrecision(3);
        entity.Property(x => x.LastLoginAtUtc).HasPrecision(3);
        entity.HasIndex(x => x.UserName).IsUnique();
        entity.HasIndex(x => x.NormalizedEmail).IsUnique();
    }

    private static void ConfigureExternalIdentity(EntityTypeBuilder<ExternalIdentity> entity)
    {
        ConfigureEntity(entity, "ExternalIdentities", "iam");
        entity.Property(x => x.ProviderKey).HasMaxLength(80).IsRequired();
        entity.Property(x => x.Issuer).HasMaxLength(300).IsRequired();
        entity.Property(x => x.Subject).HasMaxLength(300).IsRequired();
        entity.Property(x => x.EmailAtLink).HasMaxLength(256);
        entity.Property(x => x.LinkedAtUtc).HasPrecision(3);
        entity.Property(x => x.LastSeenAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        entity.HasIndex(x => x.CrmUserId);
    }

    private static void ConfigureSession(EntityTypeBuilder<UserSession> entity)
    {
        entity.Property(x => x.SecurityVersionAtIssue);
        ConfigureEntity(entity, "UserSessions", "iam");
        entity.Property(x => x.IssuedAtUtc).HasPrecision(3);
        entity.Property(x => x.LastSeenAtUtc).HasPrecision(3);
        entity.Property(x => x.IdleExpiresAtUtc).HasPrecision(3);
        entity.Property(x => x.AbsoluteExpiresAtUtc).HasPrecision(3);
        entity.Property(x => x.RevokedAtUtc).HasPrecision(3);
        entity.Property(x => x.RevokeReason).HasMaxLength(300);
        entity.Property(x => x.IpHash).HasMaxLength(128);
        entity.Property(x => x.UserAgentSummary).HasMaxLength(300);
        entity.Property(x => x.SelectedCompanyId).HasMaxLength(32);
        entity.Property(x => x.SelectedBranchId).HasMaxLength(32);
        entity.Property(x => x.SelectedTerritoryId).HasMaxLength(32);
        entity.HasIndex(x => new { x.CrmUserId, x.RevokedAtUtc, x.AbsoluteExpiresAtUtc });
    }

    private static void ConfigureRoleAssignment(EntityTypeBuilder<UserRoleAssignment> entity)
    {
        entity.Property(x => x.AssignedByUserId);
        ConfigureEntity(entity, "UserRoleAssignments", "iam");
        entity.Property(x => x.RoleKey).HasMaxLength(80).IsRequired();
        entity.Property(x => x.RoleLabel).HasMaxLength(150).IsRequired();
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.ScopeType).HasMaxLength(32).IsRequired();
        entity.Property(x => x.ScopeId).HasMaxLength(64).IsRequired();
        entity.Property(x => x.ScopeLabel).HasMaxLength(200).IsRequired();
        entity.Property(x => x.ValidFromUtc).HasPrecision(3);
        entity.Property(x => x.ValidToUtc).HasPrecision(3);
        entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        entity.Property(x => x.RevokedAtUtc).HasPrecision(3);
        entity.HasIndex(x => new { x.CrmUserId, x.CompanyId, x.Status });
        entity.HasIndex(x => new { x.CompanyId, x.ScopeType, x.ScopeId });
    }

    private static void ConfigureAudit(EntityTypeBuilder<SecurityAuditEvent> entity)
    {
        entity.Property(x => x.SessionId);
        ConfigureEntity(entity, "SecurityAuditEvents", "iam");
        entity.Property(x => x.OccurredAtUtc).HasPrecision(3);
        entity.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
        entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        entity.Property(x => x.Reason).HasMaxLength(1000);
        entity.Property(x => x.IpHash).HasMaxLength(128);
        entity.Property(x => x.UserAgentSummary).HasMaxLength(300);
        entity.HasIndex(x => new { x.TargetUserId, x.OccurredAtUtc });
        entity.HasIndex(x => new { x.ActorUserId, x.OccurredAtUtc });
        entity.HasIndex(x => x.OccurredAtUtc);
        entity.HasIndex(x => new { x.EventType, x.OccurredAtUtc });
    }

    public DbSet<PortalRequest> PortalRequests => Set<PortalRequest>();
    public DbSet<MobileVisit> MobileVisits => Set<MobileVisit>();
    public DbSet<MobileOperationReceipt> MobileOperationReceipts => Set<MobileOperationReceipt>();

    private static void ConfigureSelfService(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PortalRequest>(entity => {
            ConfigureEntity(entity, "PortalRequests", "portal"); Scope(entity);
            entity.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Subject).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.PublicReply).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.ProductCode).HasMaxLength(64);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Source).HasMaxLength(200);
            entity.Property(x => x.PriceAtUtc).HasPrecision(3);
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.Property(x => x.ProtectedUntilUtc).HasPrecision(3);
            entity.Property(x => x.ProtectionKey).HasMaxLength(64);
            entity.HasIndex(x => new { x.CompanyId, x.ProtectionKey }).IsUnique().HasFilter("[ProtectionKey] IS NOT NULL");
            entity.HasIndex(x => new { x.CompanyId, x.LinkedRecordId }).IsUnique().HasFilter("[Kind] = N'Order' AND [LinkedRecordId] IS NOT NULL");
            entity.HasIndex(x => new { x.CompanyId, x.CreatedByUserId, x.OperationId }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.DealerId, x.Status });
            entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<MobileVisit>(entity => {
            ConfigureEntity(entity, "MobileVisits", "mobile"); Scope(entity);
            entity.Property(x => x.PlannedAtUtc).HasPrecision(3);
            entity.Property(x => x.Purpose).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.CheckedInAtUtc).HasPrecision(3);
            entity.Property(x => x.CompletedAtUtc).HasPrecision(3);
            entity.Property(x => x.LastReceivedAtUtc).HasPrecision(3);
            entity.Property(x => x.Latitude).HasPrecision(9, 6);
            entity.Property(x => x.Longitude).HasPrecision(9, 6);
            entity.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.PlannedAtUtc });
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<MobileOperationReceipt>(entity => {
            ConfigureEntity(entity, "OperationReceipts", "mobile");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ReceivedAtUtc).HasPrecision(3);
            entity.HasIndex(x => new { x.CompanyId, x.ActorUserId, x.OperationId }).IsUnique();
            entity.HasOne<MobileVisit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    public DbSet<DealerCommissionPlan> DealerCommissionPlans => Set<DealerCommissionPlan>();
    public DbSet<DealerCommissionStatement> DealerCommissionStatements => Set<DealerCommissionStatement>();
    public DbSet<DealerEvaluation> DealerEvaluations => Set<DealerEvaluation>();
    public DbSet<DealerGuarantee> DealerGuarantees => Set<DealerGuarantee>();
    public DbSet<DealerTraining> DealerTrainings => Set<DealerTraining>();
    public DbSet<CommissionPayoutMessage> CommissionPayoutMessages => Set<CommissionPayoutMessage>();
    public DbSet<Crm.Domain.Notifications.NotificationPreference> NotificationPreferences => Set<Crm.Domain.Notifications.NotificationPreference>();
    public DbSet<Crm.Domain.Notifications.NotificationMessage> NotificationMessages => Set<Crm.Domain.Notifications.NotificationMessage>();

    public DbSet<Crm.Domain.Accounts.CrmActivity> CrmActivities => Set<Crm.Domain.Accounts.CrmActivity>();
    public DbSet<Crm.Domain.Accounts.ActivityParticipant> ActivityParticipants => Set<Crm.Domain.Accounts.ActivityParticipant>();
    public DbSet<Crm.Domain.Accounts.ClientOperation> ClientOperations => Set<Crm.Domain.Accounts.ClientOperation>();
    public DbSet<Crm.Domain.Accounts.AccountNote> AccountNotes => Set<Crm.Domain.Accounts.AccountNote>();
    public DbSet<Crm.Domain.Accounts.CrmDocument> CrmDocuments => Set<Crm.Domain.Accounts.CrmDocument>();
    public DbSet<Crm.Domain.Accounts.DocumentContent> DocumentContents => Set<Crm.Domain.Accounts.DocumentContent>();
    public DbSet<Crm.Domain.Accounts.DocumentLink> DocumentLinks => Set<Crm.Domain.Accounts.DocumentLink>();
    public DbSet<Crm.Domain.Accounts.AccountPayment> AccountPayments => Set<Crm.Domain.Accounts.AccountPayment>();
    public DbSet<Crm.Domain.Accounts.AccountBankAccount> AccountBankAccounts => Set<Crm.Domain.Accounts.AccountBankAccount>();
    public DbSet<Crm.Domain.Accounts.AccountContract> AccountContracts => Set<Crm.Domain.Accounts.AccountContract>();
    public DbSet<Crm.Domain.Accounts.AccountProject> AccountProjects => Set<Crm.Domain.Accounts.AccountProject>();
    public DbSet<Crm.Domain.Accounts.AccountParticipation> AccountParticipations => Set<Crm.Domain.Accounts.AccountParticipation>();
    public DbSet<Crm.Domain.Accounts.AccountAllocation> AccountAllocations => Set<Crm.Domain.Accounts.AccountAllocation>();
    public DbSet<Crm.Domain.Accounts.Campaign> Campaigns => Set<Crm.Domain.Accounts.Campaign>();
    public DbSet<Crm.Domain.Accounts.CampaignMember> CampaignMembers => Set<Crm.Domain.Accounts.CampaignMember>();
    public DbSet<Crm.Domain.Accounts.TargetList> TargetLists => Set<Crm.Domain.Accounts.TargetList>();
    public DbSet<Crm.Domain.Accounts.TargetListMember> TargetListMembers => Set<Crm.Domain.Accounts.TargetListMember>();
    public DbSet<Crm.Domain.Accounts.Survey> Surveys => Set<Crm.Domain.Accounts.Survey>();
    public DbSet<Crm.Domain.Accounts.SurveyResponse> SurveyResponses => Set<Crm.Domain.Accounts.SurveyResponse>();

    public DbSet<Crm.Domain.FollowUps.SlaPolicy> FollowUpSlaPolicies => Set<Crm.Domain.FollowUps.SlaPolicy>();
    public DbSet<Crm.Domain.FollowUps.FollowUpTemplate> FollowUpTemplates => Set<Crm.Domain.FollowUps.FollowUpTemplate>();
    public DbSet<Crm.Domain.FollowUps.FollowUpTemplateStage> FollowUpTemplateStages => Set<Crm.Domain.FollowUps.FollowUpTemplateStage>();
    public DbSet<Crm.Domain.FollowUps.FollowUpQueue> FollowUpQueues => Set<Crm.Domain.FollowUps.FollowUpQueue>();
    public DbSet<Crm.Domain.FollowUps.FollowUpQueueMember> FollowUpQueueMembers => Set<Crm.Domain.FollowUps.FollowUpQueueMember>();
    public DbSet<Crm.Domain.FollowUps.FollowUpCase> FollowUpCases => Set<Crm.Domain.FollowUps.FollowUpCase>();
    public DbSet<Crm.Domain.FollowUps.FollowUpStage> FollowUpStages => Set<Crm.Domain.FollowUps.FollowUpStage>();
    public DbSet<Crm.Domain.FollowUps.FollowUpChecklistItem> FollowUpChecklistItems => Set<Crm.Domain.FollowUps.FollowUpChecklistItem>();
    public DbSet<Crm.Domain.FollowUps.FollowUpItem> FollowUpItems => Set<Crm.Domain.FollowUps.FollowUpItem>();
    public DbSet<Crm.Domain.FollowUps.FollowUpReferral> FollowUpReferrals => Set<Crm.Domain.FollowUps.FollowUpReferral>();
    public DbSet<Crm.Domain.FollowUps.FollowUpDocument> FollowUpDocuments => Set<Crm.Domain.FollowUps.FollowUpDocument>();
    public DbSet<Crm.Domain.FollowUps.FollowUpApproval> FollowUpApprovals => Set<Crm.Domain.FollowUps.FollowUpApproval>();
    public DbSet<Crm.Domain.FollowUps.FollowUpEvent> FollowUpEvents => Set<Crm.Domain.FollowUps.FollowUpEvent>();
    public DbSet<Crm.Domain.FollowUps.FollowUpActivityLink> FollowUpActivityLinks => Set<Crm.Domain.FollowUps.FollowUpActivityLink>();
    public DbSet<Crm.Domain.FollowUps.FollowUpSavedView> FollowUpSavedViews => Set<Crm.Domain.FollowUps.FollowUpSavedView>();
    public DbSet<Crm.Domain.FollowUps.FollowUpDraft> FollowUpDrafts => Set<Crm.Domain.FollowUps.FollowUpDraft>();

    /// <summary>Follow-up center (مرکز پیگیری): cases, stages, referrals, approvals, templates, SLA policies and queues; schema "followup".</summary>
    private static void ConfigureFollowUps(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Crm.Domain.FollowUps.SlaPolicy>(entity => {
            ConfigureEntity(entity, "SlaPolicies", "followup");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Priority).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.CaseType).HasMaxLength(40);
            entity.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.WorkDays).HasMaxLength(100).IsRequired();
            entity.Property(x => x.WorkStart).HasMaxLength(5).IsRequired();
            entity.Property(x => x.WorkEnd).HasMaxLength(5).IsRequired();
            entity.Property(x => x.Holidays).HasMaxLength(2000);
            entity.Property(x => x.Escalations).HasMaxLength(400).IsRequired();
            entity.HasIndex(x => new { x.CompanyId, x.IsActive, x.Priority });
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpTemplate>(entity => {
            ConfigureEntity(entity, "Templates", "followup");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.CaseType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.Scope).HasMaxLength(32).IsRequired();
            entity.Property(x => x.OwnerUnit).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.ClosingCriteria).HasMaxLength(500);
            entity.Property(x => x.Rules).HasMaxLength(2000);
            entity.Property(x => x.DefaultPriority).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.PublishedAtUtc).HasPrecision(3);
            entity.HasIndex(x => new { x.CompanyId, x.Code, x.TemplateVersion }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.CaseType, x.Status });
            entity.HasOne<Crm.Domain.FollowUps.SlaPolicy>().WithMany().HasForeignKey(x => x.SlaPolicyId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpTemplateStage>(entity => {
            ConfigureEntity(entity, "TemplateStages", "followup");
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ResponsibleRole).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Checklist).HasMaxLength(2000);
            entity.Property(x => x.Condition).HasMaxLength(200);
            entity.HasIndex(x => new { x.TemplateId, x.Order });
            entity.HasOne<Crm.Domain.FollowUps.FollowUpTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpQueue>(entity => {
            ConfigureEntity(entity, "Queues", "followup");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Country).HasMaxLength(60).IsRequired();
            entity.Property(x => x.BranchId).HasMaxLength(32);
            entity.Property(x => x.CaseType).HasMaxLength(40);
            entity.Property(x => x.PartFamily).HasMaxLength(80);
            entity.Property(x => x.Language).HasMaxLength(16);
            entity.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Ordering).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.CompanyId, x.IsActive, x.RuleOrder });
            entity.HasOne<Crm.Domain.FollowUps.FollowUpQueue>().WithMany().HasForeignKey(x => x.OverflowQueueId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpQueueMember>(entity => {
            ConfigureEntity(entity, "QueueMembers", "followup");
            entity.Property(x => x.AvailabilityNote).HasMaxLength(200);
            entity.Property(x => x.Skills).HasMaxLength(200);
            entity.Property(x => x.LastAssignedAtUtc).HasPrecision(3);
            entity.HasIndex(x => new { x.QueueId, x.UserId }).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasOne<Crm.Domain.FollowUps.FollowUpQueue>().WithMany().HasForeignKey(x => x.QueueId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpCase>(entity => {
            ConfigureEntity(entity, "Cases", "followup");
            entity.Property(x => x.Code).HasMaxLength(24).IsRequired();
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.BranchId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.TerritoryId).HasMaxLength(32);
            entity.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.ExpectedOutcome).HasMaxLength(500);
            entity.Property(x => x.CaseType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.ExtraFields).HasMaxLength(2000);
            entity.Property(x => x.Priority).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.PriorityReason).HasMaxLength(300);
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.Language).HasMaxLength(16).IsRequired();
            entity.Property(x => x.PartFamily).HasMaxLength(80);
            entity.Property(x => x.RelatedKind).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.RelatedCode).HasMaxLength(40);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.OpenedAtUtc).HasPrecision(3);
            entity.Property(x => x.FirstResponseDueAtUtc).HasPrecision(3);
            entity.Property(x => x.FirstRespondedAtUtc).HasPrecision(3);
            entity.Property(x => x.ResolutionDueAtUtc).HasPrecision(3);
            entity.Property(x => x.PausedSinceUtc).HasPrecision(3);
            entity.Property(x => x.NextAction).HasMaxLength(200);
            entity.Property(x => x.NextActionAtUtc).HasPrecision(3);
            entity.Property(x => x.ReviewAtUtc).HasPrecision(3);
            entity.Property(x => x.WaitReason).HasMaxLength(500);
            entity.Property(x => x.WaitingOn).HasMaxLength(120);
            entity.Property(x => x.Outcome).HasMaxLength(200);
            entity.Property(x => x.OutcomeNote).HasMaxLength(2000);
            entity.Property(x => x.ClosedAtUtc).HasPrecision(3);
            entity.Property(x => x.CancelReason).HasMaxLength(500);
            entity.Ignore(x => x.IsOpen);
            entity.Ignore(x => x.IsWaiting);
            entity.Ignore(x => x.IsPaused);
            entity.Ignore(x => x.NearestDueUtc);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.NextActionAtUtc });
            entity.HasIndex(x => new { x.OwnerUserId, x.Status });
            entity.HasIndex(x => new { x.CustomerId, x.Status });
            entity.HasIndex(x => new { x.RelatedKind, x.RelatedId });
            entity.HasIndex(x => x.QueueId);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Crm.Domain.FollowUps.FollowUpTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpStage>(entity => {
            ConfigureEntity(entity, "Stages", "followup");
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ResponsibleRole).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.Condition).HasMaxLength(200);
            entity.Property(x => x.DueAtUtc).HasPrecision(3);
            entity.Property(x => x.StartedAtUtc).HasPrecision(3);
            entity.Property(x => x.CompletedAtUtc).HasPrecision(3);
            entity.Property(x => x.ReturnReason).HasMaxLength(500);
            entity.Property(x => x.SkipReason).HasMaxLength(500);
            entity.Ignore(x => x.IsWorking);
            entity.HasIndex(x => new { x.CaseId, x.Order });
            entity.HasIndex(x => new { x.ResponsibleUserId, x.Status });
            entity.HasOne<Crm.Domain.FollowUps.FollowUpCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpChecklistItem>(entity => {
            ConfigureEntity(entity, "ChecklistItems", "followup");
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.DoneAtUtc).HasPrecision(3);
            entity.HasIndex(x => new { x.CaseId, x.StageId, x.Order });
            entity.HasOne<Crm.Domain.FollowUps.FollowUpStage>().WithMany().HasForeignKey(x => x.StageId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpItem>(entity => {
            ConfigureEntity(entity, "Items", "followup");
            entity.Property(x => x.PartCode).HasMaxLength(60).IsRequired();
            entity.Property(x => x.AlternateCode).HasMaxLength(60);
            entity.Property(x => x.Description).HasMaxLength(200);
            entity.Property(x => x.Compatibility).HasMaxLength(200);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.Unit).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Warehouse).HasMaxLength(60);
            entity.Property(x => x.SerialOrBatch).HasMaxLength(60);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.DeliveredQuantity).HasPrecision(18, 3);
            entity.Property(x => x.Note).HasMaxLength(300);
            entity.HasIndex(x => x.CaseId);
            entity.HasOne<Crm.Domain.FollowUps.FollowUpCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpReferral>(entity => {
            ConfigureEntity(entity, "Referrals", "followup");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Scope).HasConversion<string>().HasMaxLength(8);
            entity.Property(x => x.ToBranchId).HasMaxLength(32);
            entity.Property(x => x.ToTeam).HasMaxLength(120);
            entity.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            entity.Property(x => x.SentAtUtc).HasPrecision(3);
            entity.Property(x => x.AcceptDueAtUtc).HasPrecision(3);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.RespondedAtUtc).HasPrecision(3);
            entity.Property(x => x.ResponseNote).HasMaxLength(500);
            entity.HasIndex(x => new { x.CaseId, x.Status });
            entity.HasIndex(x => new { x.ToUserId, x.Status });
            entity.HasOne<Crm.Domain.FollowUps.FollowUpCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpDocument>(entity => {
            ConfigureEntity(entity, "Documents", "followup");
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.ReviewedAtUtc).HasPrecision(3);
            entity.Property(x => x.ReviewNote).HasMaxLength(500);
            entity.HasIndex(x => new { x.CaseId, x.Kind });
            entity.HasOne<Crm.Domain.FollowUps.FollowUpCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Crm.Domain.Accounts.CrmDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpApproval>(entity => {
            ConfigureEntity(entity, "Approvals", "followup");
            entity.Property(x => x.ApproverRole).HasMaxLength(60).IsRequired();
            entity.Property(x => x.ReviewItems).HasMaxLength(1000);
            entity.Property(x => x.PassedItems).HasMaxLength(1000);
            entity.Property(x => x.RequestedAtUtc).HasPrecision(3);
            entity.Property(x => x.Decision).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.DecisionNote).HasMaxLength(1000);
            entity.Property(x => x.CorrectionDueAtUtc).HasPrecision(3);
            entity.Property(x => x.DecidedAtUtc).HasPrecision(3);
            entity.HasIndex(x => x.CaseId);
            entity.HasIndex(x => new { x.ApproverUserId, x.Decision });
            entity.HasOne<Crm.Domain.FollowUps.FollowUpCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpEvent>(entity => {
            ConfigureEntity(entity, "Events", "followup");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Detail).HasMaxLength(2000);
            entity.Property(x => x.AtUtc).HasPrecision(3);
            entity.HasIndex(x => new { x.CaseId, x.AtUtc });
            entity.HasOne<Crm.Domain.FollowUps.FollowUpCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpActivityLink>(entity => {
            ConfigureEntity(entity, "ActivityLinks", "followup");
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.PreChecklist).HasMaxLength(1000);
            entity.Property(x => x.ResultCode).HasMaxLength(32);
            entity.HasIndex(x => new { x.CaseId, x.StageId });
            entity.HasOne<Crm.Domain.FollowUps.FollowUpCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Crm.Domain.Accounts.CrmActivity>().WithOne().HasForeignKey<Crm.Domain.FollowUps.FollowUpActivityLink>(x => x.Id).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpSavedView>(entity => {
            ConfigureEntity(entity, "SavedViews", "followup");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Query).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.CompanyId, x.Name }).IsUnique();
        });
        modelBuilder.Entity<Crm.Domain.FollowUps.FollowUpDraft>(entity => {
            ConfigureEntity(entity, "Drafts", "followup");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Payload).HasMaxLength(Crm.Domain.FollowUps.FollowUpDraft.MaxPayload).IsRequired();
            entity.Property(x => x.SavedAtUtc).HasPrecision(3);
            entity.HasIndex(x => new { x.UserId, x.CompanyId, x.Kind }).IsUnique();
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    /// <summary>Account file (پرونده حساب): activities, notes, documents and account-owned records; schema "account".</summary>
    private static void ConfigureAccountFile(ModelBuilder modelBuilder)
    {
        void Owned<T>(EntityTypeBuilder<T> entity, string table) where T : Crm.Domain.Accounts.AccountRecord
        {
            ConfigureEntity(entity, table, "account");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.CustomerId);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
        }
        modelBuilder.Entity<Crm.Domain.Accounts.CrmActivity>(entity => {
            ConfigureEntity(entity, "Activities", "account");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.StartAtUtc).HasPrecision(3);
            entity.Property(x => x.EndAtUtc).HasPrecision(3);
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.Location).HasMaxLength(300);
            entity.Property(x => x.Priority).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.RelatedKind).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.CallResult).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Outcome).HasMaxLength(4000);
            entity.Property(x => x.CompletedAtUtc).HasPrecision(3);
            entity.Property(x => x.CancelReason).HasMaxLength(500);
            entity.Ignore(x => x.DueAtUtc);
            entity.Ignore(x => x.ReminderAtUtc);
            entity.HasIndex(x => new { x.CustomerId, x.Status, x.StartAtUtc });
            entity.HasIndex(x => new { x.OwnerUserId, x.Status, x.StartAtUtc });
            entity.HasIndex(x => x.ContactId);
            entity.HasIndex(x => new { x.RelatedKind, x.RelatedId });
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.ActivityParticipant>(entity => {
            ConfigureEntity(entity, "ActivityParticipants", "account");
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(8);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.ActivityId, x.Kind, x.ParticipantId }).IsUnique();
            entity.HasOne<Crm.Domain.Accounts.CrmActivity>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.ClientOperation>(entity => {
            ConfigureEntity(entity, "ClientOperations", "account");
            entity.Property(x => x.Kind).HasMaxLength(40).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.OperationId }).IsUnique();
        });
        modelBuilder.Entity<Crm.Domain.Accounts.AccountNote>(entity => {
            ConfigureEntity(entity, "Notes", "account");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(8000).IsRequired();
            entity.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(12);
            entity.HasIndex(x => new { x.CustomerId, x.IsDeleted, x.CreatedAtUtc });
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.CrmDocument>(entity => {
            ConfigureEntity(entity, "Documents", "account");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FileName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.CompanyId, x.IsDeleted, x.Title });
            entity.HasIndex(x => x.NoteId);
            entity.HasOne<Crm.Domain.Accounts.AccountNote>().WithMany().HasForeignKey(x => x.NoteId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.DocumentContent>(entity => {
            ConfigureEntity(entity, "DocumentContents", "account");
            entity.Property(x => x.Bytes).HasColumnType("varbinary(max)").IsRequired();
            entity.HasOne<Crm.Domain.Accounts.CrmDocument>().WithOne().HasForeignKey<Crm.Domain.Accounts.DocumentContent>(x => x.Id).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.DocumentLink>(entity => {
            ConfigureEntity(entity, "DocumentLinks", "account");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.DocumentId, x.CustomerId }).IsUnique();
            entity.HasIndex(x => x.CustomerId);
            entity.HasOne<Crm.Domain.Accounts.CrmDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.AccountPayment>(entity => {
            Owned(entity, "Payments");
            entity.Property(x => x.Direction).HasConversion<string>().HasMaxLength(8);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Method).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Reference).HasMaxLength(80);
            entity.Property(x => x.InvoiceReference).HasMaxLength(40);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.DecidedAtUtc).HasPrecision(3);
            entity.Property(x => x.DecisionNote).HasMaxLength(500);
            entity.Ignore(x => x.CountsInTotals);
            entity.HasIndex(x => new { x.CustomerId, x.Status, x.CurrencyCode });
        });
        modelBuilder.Entity<Crm.Domain.Accounts.AccountBankAccount>(entity => {
            Owned(entity, "BankAccounts");
            entity.Property(x => x.BankName).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Iban).HasMaxLength(26).IsRequired();
            entity.Property(x => x.AccountNumber).HasMaxLength(30);
            entity.Property(x => x.HolderName).HasMaxLength(150).IsRequired();
        });
        modelBuilder.Entity<Crm.Domain.Accounts.AccountContract>(entity => {
            Owned(entity, "Contracts");
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(8);
            entity.Property(x => x.Number).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.ServiceLevel).HasMaxLength(120);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.TerminationReason).HasMaxLength(500);
            entity.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
            entity.HasIndex(x => x.OpportunityId);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.AccountProject>(entity => {
            Owned(entity, "Projects");
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Budget).HasPrecision(18, 2);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        });
        modelBuilder.Entity<Crm.Domain.Accounts.AccountParticipation>(entity => {
            Owned(entity, "Participations");
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(80);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.Notes).HasMaxLength(500);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.AccountAllocation>(entity => {
            Owned(entity, "Allocations");
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.ItemCode).HasMaxLength(40);
            entity.Property(x => x.ItemName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.Notes).HasMaxLength(500);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.SurveyResponse>(entity => {
            Owned(entity, "SurveyResponses");
            entity.Property(x => x.Comment).HasMaxLength(1000);
            entity.HasIndex(x => x.SurveyId);
            entity.HasOne<Crm.Domain.Accounts.Survey>().WithMany().HasForeignKey(x => x.SurveyId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.Campaign>(entity => {
            ConfigureEntity(entity, "Campaigns", "marketing");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.Ignore(x => x.AcceptsMembers);
            entity.HasIndex(x => new { x.CompanyId, x.Status });
        });
        modelBuilder.Entity<Crm.Domain.Accounts.CampaignMember>(entity => {
            ConfigureEntity(entity, "CampaignMembers", "marketing");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            entity.HasIndex(x => new { x.CampaignId, x.CustomerId }).IsUnique();
            entity.HasIndex(x => x.CustomerId);
            entity.HasOne<Crm.Domain.Accounts.Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.TargetList>(entity => {
            ConfigureEntity(entity, "TargetLists", "marketing");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.TargetListMember>(entity => {
            ConfigureEntity(entity, "TargetListMembers", "marketing");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.TargetListId, x.CustomerId }).IsUnique();
            entity.HasIndex(x => x.CustomerId);
            entity.HasOne<Crm.Domain.Accounts.TargetList>().WithMany().HasForeignKey(x => x.TargetListId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.Accounts.Survey>(entity => {
            ConfigureEntity(entity, "Surveys", "marketing");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(8);
            entity.Ignore(x => x.ScoreRange);
        });
    }

    private static void ConfigureNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Crm.Domain.Notifications.NotificationPreference>(entity => {
            ConfigureEntity(entity, "NotificationPreferences", "notify");
            entity.Ignore(x => x.UserId);
            entity.Property(x => x.Mobile).HasMaxLength(20);
            entity.Property(x => x.MutedCategories).HasMaxLength(200);
            entity.HasOne<CrmUser>().WithOne().HasForeignKey<Crm.Domain.Notifications.NotificationPreference>(x => x.Id).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Crm.Domain.Notifications.NotificationMessage>(entity => {
            ConfigureEntity(entity, "NotificationMessages", "notify");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(8);
            entity.Property(x => x.Address).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.SourceReference).HasMaxLength(120);
            entity.Property(x => x.DedupKey).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.NextAttemptAtUtc).HasPrecision(3);
            entity.Property(x => x.LastError).HasMaxLength(500);
            entity.Property(x => x.ProviderMessageId).HasMaxLength(120);
            entity.Property(x => x.SentAtUtc).HasPrecision(3);
            entity.HasIndex(x => new { x.DedupKey, x.RecipientUserId, x.Channel }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            entity.HasIndex(x => new { x.RecipientUserId, x.CreatedAtUtc });
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureDealerIncentives(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DealerCommissionPlan>(entity => {
            ConfigureEntity(entity, "DealerCommissionPlans", "channel");
            entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.TierDefinition).HasMaxLength(400).IsRequired();
            entity.Ignore(x => x.Tiers);
            entity.HasIndex(x => x.CompanyId).IsUnique();
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<DealerCommissionStatement>(entity => {
            ConfigureEntity(entity, "DealerCommissionStatements", "channel"); Scope(entity);
            entity.Property(x => x.PeriodFromUtc).HasPrecision(3);
            entity.Property(x => x.PeriodToUtc).HasPrecision(3);
            entity.Property(x => x.NetSales).HasPrecision(18, 2);
            entity.Property(x => x.TargetAmount).HasPrecision(18, 2);
            entity.Property(x => x.OverdueAmount).HasPrecision(18, 2);
            entity.Property(x => x.AchievementPercent).HasPrecision(9, 2);
            entity.Property(x => x.RatePercent).HasPrecision(5, 2);
            entity.Property(x => x.CommissionAmount).HasPrecision(18, 2);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.CalculatedAtUtc).HasPrecision(3);
            entity.Property(x => x.DecidedAtUtc).HasPrecision(3);
            entity.Property(x => x.DecisionNote).HasMaxLength(1000);
            entity.Property(x => x.SplitDefinition).HasMaxLength(60);
            entity.Ignore(x => x.IsFinal);
            entity.Ignore(x => x.InternalSplit);
            entity.HasIndex(x => new { x.CompanyId, x.PeriodFromUtc, x.Status });
            entity.HasIndex(x => new { x.DealerId, x.PeriodFromUtc });
            entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<DealerCommissionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.CalculatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<DealerEvaluation>(entity => {
            ConfigureEntity(entity, "DealerEvaluations", "channel"); Scope(entity);
            entity.Property(x => x.PeriodFromUtc).HasPrecision(3);
            entity.Property(x => x.PeriodToUtc).HasPrecision(3);
            entity.Property(x => x.AchievementScore).HasPrecision(5, 1);
            entity.Property(x => x.CollectionScore).HasPrecision(5, 1);
            entity.Property(x => x.GrowthScore).HasPrecision(5, 1);
            entity.Property(x => x.ServiceScore).HasPrecision(5, 1);
            entity.Property(x => x.TotalScore).HasPrecision(5, 1);
            entity.Property(x => x.Tier).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.EvaluatedAtUtc).HasPrecision(3);
            entity.HasIndex(x => new { x.CompanyId, x.PeriodFromUtc, x.Rank });
            entity.HasIndex(x => new { x.DealerId, x.PeriodFromUtc });
            entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.EvaluatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<DealerGuarantee>(entity => {
            entity.HasIndex(x => new { x.CustomerId, x.Status });
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
            ConfigureEntity(entity, "DealerGuarantees", "channel"); Scope(entity);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Number).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Issuer).HasMaxLength(120);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.DecidedAtUtc).HasPrecision(3);
            entity.Property(x => x.DecisionReason).HasMaxLength(500);
            entity.HasIndex(x => new { x.DealerId, x.Status });
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.ExpiresOn });
            entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.RegisteredByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<DealerTraining>(entity => {
            ConfigureEntity(entity, "DealerTrainings", "channel"); Scope(entity);
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Topic).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Hours).HasPrecision(5, 1);
            entity.Property(x => x.Score).HasPrecision(5, 1);
            entity.HasIndex(x => new { x.DealerId, x.HeldOn });
            entity.HasOne<Dealer>().WithMany().HasForeignKey(x => x.DealerId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.RecordedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CommissionPayoutMessage>(entity => {
            ConfigureEntity(entity, "CommissionPayoutMessages", "channel"); Scope(entity);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Payload).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.NextAttemptAtUtc).HasPrecision(3);
            entity.Property(x => x.LastError).HasMaxLength(500);
            entity.Property(x => x.ExternalReference).HasMaxLength(80);
            entity.Property(x => x.CompletedAtUtc).HasPrecision(3);
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => x.StatementId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            entity.HasOne<DealerCommissionStatement>().WithMany().HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();
    public DbSet<RolePermissionGrant> RolePermissionGrants => Set<RolePermissionGrant>();

    private static void ConfigureRoleCatalog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RoleDefinition>(entity => {
            ConfigureEntity(entity, "RoleDefinitions", "iam");
            entity.Property(x => x.RoleKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Label).HasMaxLength(120).IsRequired();
            entity.Property(x => x.IsExternal);
            entity.Property(x => x.IsSystem);
            entity.Property(x => x.AllowedScopeTypes).HasMaxLength(64).IsRequired();
            entity.Ignore(x => x.ScopeTypes);
            entity.HasIndex(x => x.RoleKey).IsUnique();
        });
        modelBuilder.Entity<RolePermissionGrant>(entity => {
            ConfigureEntity(entity, "RolePermissionGrants", "iam");
            entity.Property(x => x.RoleKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Permission).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.RoleKey, x.Permission }).IsUnique();
            entity.HasOne<RoleDefinition>().WithMany().HasForeignKey(x => x.RoleKey).HasPrincipalKey(x => x.RoleKey).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public DbSet<ServiceCase> ServiceCases => Set<ServiceCase>();
    public DbSet<ServiceCaseHistory> ServiceCaseHistory => Set<ServiceCaseHistory>();

    private static void ConfigureServiceDesk(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ServiceCase>(entity => {
            ConfigureEntity(entity, "ServiceCases", "service"); Scope(entity);
            entity.Property(x => x.Code).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.Category).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Channel).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Priority).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Owner).HasMaxLength(200).IsRequired();
            entity.Property(x => x.OpenedAtUtc).HasPrecision(3);
            entity.Property(x => x.FirstResponseDueAtUtc).HasPrecision(3);
            entity.Property(x => x.ResolutionDueAtUtc).HasPrecision(3);
            entity.Property(x => x.FirstRespondedAtUtc).HasPrecision(3);
            entity.Property(x => x.PausedAtUtc).HasPrecision(3);
            entity.Property(x => x.ResolvedAtUtc).HasPrecision(3);
            entity.Property(x => x.ClosedAtUtc).HasPrecision(3);
            entity.Property(x => x.LastEscalatedAtUtc).HasPrecision(3);
            entity.Property(x => x.RootCause).HasMaxLength(1000);
            entity.Property(x => x.CorrectiveAction).HasMaxLength(1000);
            entity.Property(x => x.Resolution).HasMaxLength(2000);
            entity.Property(x => x.SatisfactionComment).HasMaxLength(1000);
            entity.Ignore(x => x.IsOpen);
            entity.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.CompanyId, x.Status, x.ResolutionDueAtUtc });
            entity.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.Status });
            entity.HasIndex(x => new { x.CompanyId, x.CustomerId });
            entity.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ServiceCaseHistory>(entity => {
            ConfigureEntity(entity, "ServiceCaseHistory", "service"); Scope(entity);
            entity.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.OccurredAtUtc).HasPrecision(3);
            entity.HasIndex(x => new { x.CaseId, x.OccurredAtUtc });
            entity.HasOne<ServiceCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<CrmUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void Scope<T>(EntityTypeBuilder<T> entity) where T : Entity, IOrganizationScoped
    {
        entity.Property(x => x.CompanyId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.BranchId).HasMaxLength(32).IsRequired();
        entity.Property(x => x.TerritoryId).HasMaxLength(32);
    }

    private static void ConfigureEntity<T>(EntityTypeBuilder<T> entity, string table, string schema) where T : Entity
    {
        entity.ToTable(table, schema);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.CreatedAtUtc).HasPrecision(3);
        entity.Property(x => x.UpdatedAtUtc).HasPrecision(3);
        entity.Property(x => x.Version).IsConcurrencyToken();
    }
}
