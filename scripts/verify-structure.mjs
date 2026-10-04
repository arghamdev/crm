import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, resolve } from "node:path";

const root = resolve(import.meta.dirname, "..");
const required = [
  "EnterpriseCrm.sln",
  "src/Crm.Domain/Crm.Domain.csproj",
  "src/Crm.Application/Crm.Application.csproj",
  "src/Crm.Infrastructure/Crm.Infrastructure.csproj",
  "src/Crm.Web/Crm.Web.csproj",
  "src/Crm.Web/Program.cs",
  "src/Crm.Web/Views/Shared/_Layout.cshtml",
  "src/Crm.Domain/Identity/ExternalIdentity.cs",
  "src/Crm.Domain/Identity/UserSession.cs",
  "src/Crm.Domain/Identity/UserRoleAssignment.cs",
  "src/Crm.Domain/Identity/SecurityAuditEvent.cs",
  "src/Crm.Domain/Organization/OrganizationModels.cs",
  "src/Crm.Application/Services/IdentityApplicationService.cs",
  "src/Crm.Application/Services/OrganizationContextService.cs",
  "src/Crm.Application/Services/OrganizationAdminService.cs",
  "src/Crm.Infrastructure/Data/CrmDbContext.cs",
  "src/Crm.Infrastructure/Data/EfCoreCrmDataStore.cs",
  "src/Crm.Infrastructure/Migrations/202609210001_InitialEnterpriseCrm.cs",
  "src/Crm.Infrastructure/Migrations/202609210002_Customer360.cs",
  "src/Crm.Infrastructure/Migrations/202609210003_CustomerRelationshipsAndMerge.cs",
  "src/Crm.Infrastructure/Migrations/202609220004_SalesPipelineGovernance.cs",
  "src/Crm.Infrastructure/Migrations/202609260005_QuotePricingGovernance.cs",
  "src/Crm.Infrastructure/Migrations/202609260006_OrderIntegrationVisibility.cs",
  "src/Crm.Infrastructure/Migrations/202609270007_DealerChannelGovernance.cs",
  "src/Crm.Infrastructure/Migrations/CrmDbContextModelSnapshot.cs",
  "src/Crm.Domain/Customers/Customer360Models.cs",
  "src/Crm.Application/Contracts/Customer360Contracts.cs",
  "src/Crm.Application/Services/Customer360Service.cs",
  "src/Crm.Web/Controllers/CustomersController.cs",
  "src/Crm.Web/Views/AccountFile/Index.cshtml",
  "src/Crm.Web/Views/Customers/Activity.cshtml",
  "src/Crm.Web/Views/Customers/_Activity.cshtml",
  "src/Crm.Web/Views/Customers/DataQuality.cshtml",
  "src/Crm.Web/Views/Customers/Duplicates.cshtml",
  "src/Crm.Web/Views/Customers/_MergeForm.cshtml",
  "src/Crm.Web/Views/Customers/Merges.cshtml",
  "src/Crm.Web/Views/Customers/_MergeTable.cshtml",
  "src/Crm.Web/Views/Quotes/_OpportunityOptions.cshtml",
  "src/Crm.Application/Abstractions/ICustomerQueryStore.cs",
  "src/Crm.Infrastructure/Data/EfCoreCustomerQueryStore.cs",
  "src/Crm.Web/Security/OrganizationContextMiddleware.cs",
  "src/Crm.Web/Controllers/ContextController.cs",
  "src/Crm.Web/Controllers/OrganizationController.cs",
  "src/Crm.Web/Views/Context/Select.cshtml",
  "src/Crm.Web/Views/Organization/Index.cshtml",
  "src/Crm.Web/Security/CrmOpenIdConnectEvents.cs",
  "src/Crm.Web/Views/Identity/Details.cshtml",
  "docs/priority-2-implementation-fa.md",
  "docs/priority-3-implementation-fa.md",
  "docs/priority-4-implementation-fa.md",
  "docs/priority-5-implementation-fa.md",
  "docs/priority-6-implementation-fa.md",
  "docs/priority-6-quality-gate-fa.md",
  "docs/priority-7-implementation-fa.md",
  "docs/priority-7-quality-gate-fa.md",
  "docs/priority-8-implementation-fa.md",
  "docs/priority-8-quality-gate-fa.md",
  "docs/priority-8-delivery-fa.md",
  "docs/priority-9-implementation-fa.md",
  "docs/priority-9-quality-gate-fa.md",
  "docs/priority-9-delivery-fa.md",
  "preview/crm-unified.html",
  "preview/unified.js",
  "scripts/build-unified-preview.mjs",
  "scripts/test-unified-preview.mjs",
  "docs/runbooks/reporting-bi-deployment-fa.md",
  "src/Crm.Application/Contracts/ReportingContracts.cs",
  "src/Crm.Application/Services/ReportingService.cs",
  "src/Crm.Infrastructure/Reporting/DemoReportingFinanceSource.cs",
  "src/Crm.Web/Controllers/ReportsController.cs",
  "src/Crm.Web/Views/Reports/Index.cshtml",
  "src/Crm.Web/Views/Reports/_Dashboard.cshtml",
  "src/Crm.Web/Views/Reports/_Drilldown.cshtml",
  "tests/Crm.ArchitectureTests/ReportingChecks.cs",
  "tests/Crm.WebTests/ReportingWebChecks.cs",
  "preview/crm-priority9-reports.html",
  "src/Crm.Domain/Commercial/Quote.cs",
  "src/Crm.Application/Contracts/QuoteContracts.cs",
  "src/Crm.Application/Services/QuoteApplicationService.cs",
  "src/Crm.Infrastructure/Commercial/DemoProductPriceCatalog.cs",
  "src/Crm.Web/Controllers/QuotesController.cs",
  "src/Crm.Web/Views/Quotes/Details.cshtml",
  "src/Crm.Web/Views/Quotes/_LineForm.cshtml",
  "src/Crm.Domain/Commercial/OrderRequest.cs",
  "src/Crm.Application/Contracts/OrderContracts.cs",
  "src/Crm.Application/Services/OrderApplicationService.cs",
  "src/Crm.Infrastructure/Commercial/DemoOrderIntegrationAdapters.cs",
  "src/Crm.Web/Controllers/OrdersController.cs",
  "src/Crm.Web/Views/Orders/Details.cshtml",
  "src/Crm.Web/Views/Orders/_CreditOverrideForm.cshtml",
  "src/Crm.Domain/Channel/Dealer.cs",
  "src/Crm.Application/Contracts/DealerContracts.cs",
  "src/Crm.Application/Services/DealerApplicationService.cs",
  "src/Crm.Infrastructure/Channel/DemoDealerProjectionAdapters.cs",
  "src/Crm.Web/Controllers/DealersController.cs",
  "src/Crm.Web/Views/Dealers/Index.cshtml",
  "src/Crm.Web/Views/Dealers/Details.cshtml",
  "src/Crm.Web/Views/Dealers/_Form.cshtml",
  "src/Crm.Domain/Sales/SalesPipelineModels.cs",
  "src/Crm.Application/Contracts/SalesPipelineContracts.cs",
  "src/Crm.Application/Services/SalesPipelineService.cs",
  "src/Crm.Web/Views/Leads/Details.cshtml",
  "src/Crm.Web/Views/Opportunities/Details.cshtml",
  "src/Crm.Web/wwwroot/lib/htmx/htmx.min.js",
  "src/Crm.Web/wwwroot/js/site.js",
  "tests/Crm.ArchitectureTests/Program.cs"
  ,"tests/Crm.WebTests/Crm.WebTests.csproj"
  ,"tests/Crm.WebTests/Program.cs"
  ,"tests/Crm.PersistenceTests/Program.cs"
  ,"docs/runbooks/idp-outage-fa.md"
  ,"docs/runbooks/credential-rotation-fa.md"
  ,"docs/runbooks/session-revocation-fa.md"
  ,"docs/runbooks/shared-key-ring-validation-fa.md"
  ,"docs/test-oidc-environment-fa.md"
  ,"scripts/scan-repository-secrets.mjs"
  ,"docs/runbooks/sqlserver-migration-and-rollback-fa.md"
  ,"docs/runbooks/distributed-cache-fa.md"
  ,"docs/runbooks/customer-duplicate-review-fa.md"
  ,"docs/runbooks/customer-merge-deployment-fa.md"
  ,"docs/runbooks/sales-pipeline-deployment-fa.md"
  ,"docs/runbooks/quote-pricing-deployment-fa.md"
  ,"docs/priority-5-ci-quality-gate-fa.md"
  ,"scripts/sql/priority4-relationships-merge-idempotent.sql"
  ,"scripts/sql/priority4-relationships-merge-rollback.sql"
  ,"scripts/sql/priority5-sales-pipeline-idempotent.sql"
  ,"scripts/sql/priority5-sales-pipeline-rollback.sql"
  ,"scripts/sql/priority6-quote-pricing-idempotent.sql"
  ,"scripts/sql/priority6-quote-pricing-rollback.sql"
  ,"scripts/sql/priority7-order-integration-idempotent.sql"
  ,"scripts/sql/priority7-order-integration-rollback.sql"
  ,"docs/runbooks/order-integration-deployment-fa.md"
  ,"scripts/sql/priority8-dealer-channel-idempotent.sql"
  ,"scripts/sql/priority8-dealer-channel-rollback.sql"
  ,"docs/runbooks/dealer-channel-deployment-fa.md"
  ,".github/workflows/priority10-quality-gate.yml"
  ,"src/Crm.Application/Services/SelfServiceService.cs"
  ,"src/Crm.Domain/SelfService/PortalRequest.cs"
  ,"src/Crm.Domain/SelfService/MobileVisit.cs"
  ,"src/Crm.Web/Views/Portal/Index.cshtml"
  ,"src/Crm.Web/Views/PartnerInbox/Review.cshtml"
  ,"src/Crm.Web/Views/Mobile/Index.cshtml"
  ,"src/Crm.Infrastructure/Data/CompatibleMigrationsIdGenerator.cs"
  ,"src/Crm.Infrastructure/Migrations/202609270008_PortalMobileSelfService.cs"
  ,"preview/self-service.js"
  ,"scripts/test-mobile-outbox.mjs"
  ,"docs/priority-10-implementation-fa.md"
  ,"docs/priority-10-quality-gate-fa.md"
  ,"docs/priority-10-delivery-fa.md"
  ,"docs/runbooks/portal-mobile-deployment-fa.md"
  ,"scripts/sql/priority10-portal-mobile-idempotent.sql"
  ,"scripts/sql/priority10-portal-mobile-rollback.sql"
  ,"src/Crm.Infrastructure/Migrations/202610030009_ServiceDeskSla.cs"
  ,"src/Crm.Infrastructure/Migrations/202610030010_RoleCatalog.cs"
  ,"src/Crm.Infrastructure/Migrations/202610030011_AuditLogIndexes.cs"
  ,"src/Crm.Infrastructure/Migrations/202610030012_RoleScopes.cs"
  ,"scripts/sql/role-scopes-idempotent.sql"
  ,"scripts/sql/role-scopes-rollback.sql"
  ,"src/Crm.Infrastructure/Migrations/202610030013_DealerIncentives.cs"
  ,"scripts/sql/phase7-dealer-incentives-idempotent.sql"
  ,"scripts/sql/phase7-dealer-incentives-rollback.sql"
  ,"src/Crm.Infrastructure/Migrations/202610030014_CustomerProfiles.cs"
  ,"scripts/sql/customer-profiles-idempotent.sql"
  ,"scripts/sql/customer-profiles-rollback.sql"
  ,"src/Crm.Infrastructure/Migrations/202610030015_DealerAssurance.cs"
  ,"scripts/sql/phase7-dealer-assurance-idempotent.sql"
  ,"scripts/sql/phase7-dealer-assurance-rollback.sql"
  ,"src/Crm.Infrastructure/Migrations/202610030016_Notifications.cs"
  ,"scripts/sql/notifications-idempotent.sql"
  ,"scripts/sql/notifications-rollback.sql"
  ,"src/Crm.Infrastructure/Migrations/202610030017_AccountFile.cs"
  ,"scripts/sql/account-file-idempotent.sql"
  ,"scripts/sql/account-file-rollback.sql"
  ,"src/Crm.Domain/Accounts/CrmActivity.cs"
  ,"src/Crm.Domain/Accounts/AccountRecords.cs"
  ,"src/Crm.Domain/Accounts/AccountNotesAndDocuments.cs"
  ,"src/Crm.Application/Services/AccountFileService.cs"
  ,"src/Crm.Application/Services/AccountActivityService.cs"
  ,"src/Crm.Application/Services/AccountNoteService.cs"
  ,"src/Crm.Application/Services/AccountRecordService.cs"
  ,"docs/account-file-fa.md"
  ,"src/Crm.Web/Controllers/AccountFileController.cs"
  ,"src/Crm.Web/Views/AccountFile/_Section.cshtml"
  ,"src/Crm.Web/Views/AccountFile/_ActivityPanel.cshtml"
  ,"src/Crm.Domain/Notifications/Notification.cs"
  ,"src/Crm.Application/Services/NotificationServices.cs"
  ,"src/Crm.Infrastructure/Notifications/NotificationSenders.cs"
  ,"src/Crm.Web/Background/NotificationWorker.cs"
  ,"src/Crm.Domain/Channel/DealerAssurance.cs"
  ,"src/Crm.Application/Services/DealerAssuranceService.cs"
  ,"src/Crm.Application/Services/CommissionPayoutDispatcher.cs"
  ,"src/Crm.Web/Views/Dealers/_Assurance.cshtml"
  ,"src/Crm.Domain/Customers/CustomerProfile.cs"
  ,"src/Crm.Domain/Common/IranianData.cs"
  ,"src/Crm.Application/Services/CustomerFormRules.cs"
  ,"src/Crm.Web/Views/Customers/_ProfileFields.cshtml"
  ,"src/Crm.Web/Views/Customers/_ContactRow.cshtml"
  ,"src/Crm.Domain/Channel/DealerIncentives.cs"
  ,"src/Crm.Application/Services/DealerIncentiveService.cs"
  ,"src/Crm.Web/Controllers/DealerIncentivesController.cs"
  ,"src/Crm.Web/Views/DealerIncentives/Commissions.cshtml"
  ,"src/Crm.Web/Views/DealerIncentives/Ranking.cshtml"
  ,"scripts/sql/audit-log-indexes-idempotent.sql"
  ,"scripts/sql/audit-log-indexes-rollback.sql"
  ,"scripts/ci/coverage-gate.mjs"
  ,"scripts/sql/role-catalog-idempotent.sql"
  ,"scripts/sql/role-catalog-rollback.sql"
  ,"scripts/sql/phase8-service-desk-idempotent.sql"
  ,"scripts/sql/phase8-service-desk-rollback.sql"
  ,"src/Crm.Domain/Service/ServiceCase.cs"
  ,"src/Crm.Application/Services/ServiceCaseService.cs"
  ,"src/Crm.Web/Controllers/ServiceCasesController.cs"
  ,"docs/phase-8-service-desk-fa.md"
  ,"docs/analysis-report-fa.md"
  ,"scripts/ci/wait-for-http.mjs"
  ,"tests/Crm.BrowserTests/Crm.BrowserTests.csproj"
  ,"tests/Crm.BrowserTests/Program.cs"
  ,"preview/crm-priority7-orders.html"
  ,"preview/crm-priority8-dealers.html"
];

const failures = [];
for (const relative of required) {
  if (!existsSync(resolve(root, relative))) failures.push("Missing: " + relative);
}

const application = readFileSync(resolve(root, "src/Crm.Application/Crm.Application.csproj"), "utf8");
const domain = readFileSync(resolve(root, "src/Crm.Domain/Crm.Domain.csproj"), "utf8");
const infrastructure = readFileSync(resolve(root, "src/Crm.Infrastructure/Crm.Infrastructure.csproj"), "utf8");
if (/Crm\.(Infrastructure|Web)/.test(domain)) failures.push("Domain has an outward project reference.");
if (/Crm\.(Infrastructure|Web)/.test(application)) failures.push("Application has an outward project reference.");
if (/Crm\.Web/.test(infrastructure)) failures.push("Infrastructure references Web.");

for (const project of walk(root).filter(x => x.endsWith(".csproj"))) {
  const xml = readFileSync(project, "utf8");
  for (const match of xml.matchAll(/ProjectReference Include="([^"]+)"/g)) {
    const target = resolve(dirname(project), match[1].replaceAll("\\", "/"));
    if (!existsSync(target)) failures.push("Broken project reference: " + target);
  }
}

const siteJs = readFileSync(resolve(root, "src/Crm.Web/wwwroot/js/site.js"), "utf8");
for (const contract of ["htmx:configRequest", "htmx:beforeSwap", "RequestVerificationToken", "422"]) {
  if (!siteJs.includes(contract)) failures.push("Missing HTMX contract: " + contract);
}

const webProgram = readFileSync(resolve(root, "src/Crm.Web/Program.cs"), "utf8");
for (const contract of ["AddOpenIdConnect", "UsePkce = true", "SaveTokens = false", "MapInboundClaims = false", "AddRateLimiter", "PersistKeysToFileSystem", "__Host-Crm.Auth"]) {
  if (!webProgram.includes(contract)) failures.push("Missing priority-2 web contract: " + contract);
}

for (const contract of ["IOrganizationContextService", "OrganizationContextMiddleware"]) {
  if (!webProgram.includes(contract)) failures.push("Missing priority-3 web contract: " + contract);
}

for (const contract of ["UseSqlServer", "EfCoreCrmDataStore", "AddStackExchangeRedisCache", "CrmDatabaseInitializer"]) {
  if (!webProgram.includes(contract)) failures.push("Missing priority-3 production-readiness contract: " + contract);
}

for (const contract of ["ICustomer360Service", "Customer360Service", "ICustomerQueryStore", "EfCoreCustomerQueryStore"]) {
  if (!webProgram.includes(contract)) failures.push("Missing priority-4 Customer 360 registration: " + contract);
}

for (const contract of ["ISalesPipelineService", "SalesPipelineService"]) {
  if (!webProgram.includes(contract)) failures.push("Missing priority-5 sales-pipeline registration: " + contract);
}

for (const contract of ["IQuoteApplicationService", "QuoteApplicationService", "IProductPriceCatalog", "DemoProductPriceCatalog"]) {
  if (!webProgram.includes(contract)) failures.push("Missing priority-6 quote-governance registration: " + contract);
}

for (const contract of ["IOrderApplicationService", "OrderApplicationService", "IAccountingCreditProvider", "IErpOrderGateway"]) {
  if (!webProgram.includes(contract)) failures.push("Missing priority-7 order-integration registration: " + contract);
}

for (const contract of ["IDealerApplicationService", "DealerApplicationService", "IDealerFinancialProjectionProvider", "IDealerPerformanceProjectionProvider"]) {
  if (!webProgram.includes(contract)) failures.push("Missing priority-8 dealer-governance registration: " + contract);
}

const organizationService = readFileSync(resolve(root, "src/Crm.Application/Services/OrganizationContextService.cs"), "utf8");
for (const contract of ["UserContext.CompanyChanged", "SelectedCompanyId", "SelectedBranchId", "SelectedTerritoryId", "CanUseBranch"]) {
  if (!organizationService.includes(contract)) failures.push("Missing priority-3 organization contract: " + contract);
}

const organizationAdmin = readFileSync(resolve(root, "src/Crm.Application/Services/OrganizationAdminService.cs"), "utf8");
for (const contract of ["EnsureVersion", "EnsureNoCycle", "OrganizationChange", "HasActiveRecord", "ValidToUtc"]) {
  if (!organizationAdmin.includes(contract)) failures.push("Missing priority-3 organization-admin contract: " + contract);
}

const dbContext = readFileSync(resolve(root, "src/Crm.Infrastructure/Data/CrmDbContext.cs"), "utf8");
for (const contract of ["IsConcurrencyToken", "OrganizationChanges", "CustomerContacts", "CustomerAddresses", "CustomerTimelineEvents", "CustomerDuplicateCandidates", "CustomerMergeOperations", "CustomerId", "OpportunityId", "HasIndex", "HasPrecision", "UX_CustomerMergeOperations_DuplicateCandidateId_Active", "UX_CustomerMergeOperations_MergedCustomerId_Active"]) {
  if (!dbContext.includes(contract)) failures.push("Missing EF Core persistence contract: " + contract);
}

for (const contract of ["LeadStatusHistory", "OpportunityStageHistory", "OpportunityActivities", "OwnerUserId", "ExpectedCloseAtUtc"]) {
  if (!dbContext.includes(contract)) failures.push("Missing priority-5 EF Core contract: " + contract);
}

for (const contract of ["QuoteLines", "QuoteStatusHistory", "QuoteApprovalDecisions", "ApprovalLevel", "ParentQuoteId"]) {
  if (!dbContext.includes(contract)) failures.push("Missing priority-6 EF Core contract: " + contract);
}

for (const contract of ["OrderRequests", "OrderCreditDecisions", "OrderStatusHistory", "OrderIntegrationMessages", "OrderIntegrationAttempts"]) {
  if (!dbContext.includes(contract)) failures.push("Missing priority-7 EF Core contract: " + contract);
}

for (const contract of ["Dealers", "DealerContracts", "DealerTerritoryAssignments", "DealerCustomerAssignments", "DealerTargets", "DealerFinancialSnapshots", "DealerPerformanceSnapshots", "DealerStatusHistory"]) {
  if (!dbContext.includes(contract)) failures.push("Missing priority-8 EF Core contract: " + contract);
}

const salesPipeline = readFileSync(resolve(root, "src/Crm.Application/Services/SalesPipelineService.cs"), "utf8");
for (const contract of ["LeadSlaState", "CanManageRecord", "EnsureNoOpenDuplicate", "EnsureVersion", "LeadConverted", "OpportunityChanged"]) {
  if (!salesPipeline.includes(contract)) failures.push("Missing priority-5 sales-pipeline contract: " + contract);
}

const priorityNineWorkflow = readFileSync(resolve(root, ".github/workflows/priority10-quality-gate.yml"), "utf8");
for (const contract of ["dotnet-version: 10.0.x", "CRM_REQUIRE_SQLSERVER: true", "CRM_TEST_ROLLBACK: true", "install --with-deps chromium", "Crm.BrowserTests"]) {
  if (!priorityNineWorkflow.includes(contract)) failures.push("Missing cumulative CI quality-gate contract: " + contract);
}

const browserTests = readFileSync(resolve(root, "tests/Crm.BrowserTests/Program.cs"), "utf8");
for (const contract of ["/account/login", "/leads/create", "/opportunities/create", "/quotes/create", "/orders/", "/credit-check", "/dealers/", "/customers/assign", "/activities/create", "__RequestVerificationToken", "ExpectedVersion"]) {
  if (!browserTests.includes(contract)) failures.push("Missing cumulative browser-test contract: " + contract);
}

const persistenceTests = readFileSync(resolve(root, "tests/Crm.PersistenceTests/Program.cs"), "utf8");
for (const contract of ["CRM_REQUIRE_SQLSERVER", "CRM_TEST_ROLLBACK", "PriorityEightSchemaWasRemoved", "202609270007_DealerChannelGovernance"]) {
  if (!persistenceTests.includes(contract)) failures.push("Missing priority-8 persistence quality-gate contract: " + contract);
}

const quoteService = readFileSync(resolve(root, "src/Crm.Application/Services/QuoteApplicationService.cs"), "utf8");
for (const contract of ["QuoteApprovalPolicy.Resolve", "QuoteApprovalDecision", "QuoteStatus.Submitted", "EnsureVersion", "Quote.Approve.Finance"]) {
  if (!quoteService.includes(contract)) failures.push("Missing priority-6 quote-governance contract: " + contract);
}

const orderService = readFileSync(resolve(root, "src/Crm.Application/Services/OrderApplicationService.cs"), "utf8");
for (const contract of ["accounting.Get", "SubmissionIdempotencyKey", "RetryScheduled", "DeadLetter", "CorrelationId", "AdvanceProjection", "Order.CreditOverride"]) {
  if (!orderService.includes(contract)) failures.push("Missing priority-7 order-integration contract: " + contract);
}

const dealerService = readFileSync(resolve(root, "src/Crm.Application/Services/DealerApplicationService.cs"), "utf8");
for (const contract of ["EnsureActivationPrerequisites", "Dealer.Financial.Read", "Dealer.Target.Manage", "DealerTerritoryStatus.Active", "EnsureVersion", "GetTargetForm", "EndContract", "EndTerritory", "EndCustomerAssignment", "DealerStatus.Terminated"]) {
  if (!dealerService.includes(contract)) failures.push("Missing priority-8 dealer-governance contract: " + contract);
}

const reportingService = readFileSync(resolve(root, "src/Crm.Application/Services/ReportingService.cs"), "utf8");
for (const contract of ["Reporting.Read", "Reporting.Export", "Reporting.BiExport", "Reporting.Financial.Read", "CsvCell", "ExportLimit", "InRegion", "commonPeriod", "Reporting.Export", "crm.reporting.v1"])
  if (!reportingService.includes(contract)) failures.push("Missing priority-9 reporting contract: " + contract);
for (const contract of ["IReportingService", "ReportingService", "IReportingFinanceSource"])
  if (!webProgram.includes(contract)) failures.push("Missing priority-9 service registration: " + contract);


const customer360Service = readFileSync(resolve(root, "src/Crm.Application/Services/Customer360Service.cs"), "utf8");
for (const contract of ["VisibleDuplicateCandidates", "AllowsRecord", "EnsureVersion", "GetDataQuality", "ReviewDuplicate", "GetMergePreview", "Unmerge", "MergeManifest", "ValidateMergeState", "EnsureManifestRelations", "EnsureRelationshipCompanyConsistency", "DealerAssignments"]) {
  if (!customer360Service.includes(contract)) failures.push("Missing priority-4 Customer 360 contract: " + contract);
}

const accessSnapshot = readFileSync(resolve(root, "src/Crm.Application/Abstractions/IAccessSnapshotService.cs"), "utf8");
for (const contract of ["CompanyPermissionSet", "PermissionScopeGrant", "AllowsRecord"]) {
  if (!accessSnapshot.includes(contract)) failures.push("Missing priority-3 access contract: " + contract);
}

const principalFactory = readFileSync(resolve(root, "src/Crm.Web/Security/CrmPrincipalFactory.cs"), "utf8");
for (const forbiddenClaim of ["role_label", "permission", "scope"]) {
  if (principalFactory.toLowerCase().includes(forbiddenClaim)) failures.push("Forbidden authorization data in cookie principal: " + forbiddenClaim);
}

const identityService = readFileSync(resolve(root, "src/Crm.Application/Services/IdentityApplicationService.cs"), "utf8");
for (const contract of ["PendingActivation", "EmailVerified", "Matches(identity.Issuer, identity.Subject)", "MaxConcurrentSessions", "SecurityVersion", "AccessDenied"]) {
  if (!identityService.includes(contract)) failures.push("Missing priority-2 identity contract: " + contract);
}

const webTests = readFileSync(resolve(root, "tests/Crm.WebTests/Program.cs"), "utf8");
for (const contract of ["WebApplicationFactory", "CheckOidcChallengeUsesCodeAndPkce", "CheckSharedKeyRing", "CheckAntiforgery", "CheckLoginRateLimit", "CheckOrganizationSwitchAndIsolation", "CheckCustomer360", "CheckQuoteGovernance", "CheckOrderVisibility", "CheckDealerGovernance", "CheckDealerScope"]) {
  if (!webTests.includes(contract)) failures.push("Missing priority-2 web test: " + contract);
}

const packageJson = JSON.parse(readFileSync(resolve(root, "src/Crm.Web/package.json"), "utf8"));
if (!packageJson.devDependencies?.tailwindcss || !packageJson.devDependencies?.["@tailwindcss/cli"]) {
  failures.push("Tailwind v4 CLI dependencies are incomplete.");
}

for (const file of [...walk(resolve(root, "src")), ...walk(resolve(root, "tests"))].filter(x => x.endsWith(".cs"))) {
  const source = readFileSync(file, "utf8");
  const stripped = stripCommentsAndStrings(source);
  if (/\}\s*using\s+[\w.]+\s*;/.test(stripped)) failures.push("Using directive after a type declaration: " + file);
  for (const [open, close] of [["{", "}"], ["(", ")"], ["[", "]"]]) {
    const opens = [...stripped].filter(x => x === open).length;
    const closes = [...stripped].filter(x => x === close).length;
    if (opens !== closes) failures.push("Unbalanced " + open + close + " in " + file);
  }
}

for (const file of walk(resolve(root, "src/Crm.Web/Views")).filter(x => x.endsWith(".cshtml"))) {
  const view = readFileSync(file, "utf8");
  for (const form of view.matchAll(/<form\b[^>]*method="post"[^>]*>[\s\S]*?<\/form>/gi)) {
    if (!/asp-action=|AntiForgeryToken/.test(form[0])) {
      failures.push("POST form without anti-forgery fallback: " + file);
    }
  }
}

const routeKeys = new Set();
for (const file of walk(resolve(root, "src/Crm.Web/Controllers")).filter(x => x.endsWith("Controller.cs"))) {
  const controller = readFileSync(file, "utf8");
  for (const match of controller.matchAll(/\[Http(Get|Post)\("([^"]+)"\)\]/g)) {
    const key = match[1] + " " + match[2];
    if (routeKeys.has(key)) failures.push("Duplicate route: " + key);
    routeKeys.add(key);
  }
}

if (failures.length) {
  console.error(failures.join("\n"));
  process.exit(1);
}

console.log("Structure, dependency direction, JSON and HTMX contracts are valid.");

function walk(directory) {
  const items = [];
  for (const name of readdirSync(directory)) {
    const path = resolve(directory, name);
    if (statSync(path).isDirectory()) items.push(...walk(path));
    else items.push(path);
  }
  return items;
}

function stripCommentsAndStrings(source) {
  let result = "";
  let state = "code";
  for (let i = 0; i < source.length; i++) {
    const current = source[i];
    const next = source[i + 1];
    if (state === "code" && current === "/" && next === "/") { state = "line"; i++; continue; }
    if (state === "code" && current === "/" && next === "*") { state = "block"; i++; continue; }
    if (state === "code" && current === "@" && next === '"') { state = "verbatim"; i++; continue; }
    if (state === "code" && current === '"') { state = "string"; continue; }
    if (state === "code" && current === "'") { state = "char"; continue; }
    if (state === "line" && current === "\n") { state = "code"; result += "\n"; continue; }
    if (state === "block" && current === "*" && next === "/") { state = "code"; i++; continue; }
    if ((state === "string" || state === "char") && current === "\\") { i++; continue; }
    if (state === "string" && current === '"') { state = "code"; continue; }
    if (state === "char" && current === "'") { state = "code"; continue; }
    if (state === "verbatim" && current === '"' && next === '"') { i++; continue; }
    if (state === "verbatim" && current === '"') { state = "code"; continue; }
    if (state === "code") result += current;
  }
  return result;
}
