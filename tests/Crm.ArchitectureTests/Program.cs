using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Commercial;
using Crm.Domain.Channel;
using Crm.Domain.Customers;
using Crm.Domain.Identity;
using Crm.Domain.Organization;
using Crm.Domain.Sales;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Crm.Infrastructure.Commercial;
using Crm.Infrastructure.Channel;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

var failures = new List<string>();

CheckDependency(typeof(Customer).Assembly, "Crm.Domain", ["Crm.Application", "Crm.Infrastructure", "Crm.Web"]);
CheckDependency(typeof(CrmApplicationService).Assembly, "Crm.Application", ["Crm.Infrastructure", "Crm.Web"]);
CheckDependency(typeof(InMemoryCrmDataStore).Assembly, "Crm.Infrastructure", ["Crm.Web"]);

using var store = new InMemoryCrmDataStore();
var managerId = Guid.Parse("10000000-0000-4000-8000-000000000001");
var expertId = Guid.Parse("10000000-0000-4000-8000-000000000002");
var financeManagerId = Guid.Parse("10000000-0000-4000-8000-000000000006");
var channelManagerId = Guid.Parse("10000000-0000-4000-8000-000000000007");
var dealerUserId = Guid.Parse("10000000-0000-4000-8000-000000000008");
var pendingId = Guid.Parse("10000000-0000-4000-8000-000000000005");
using var cacheProvider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
var cache = cacheProvider.GetRequiredService<IDistributedCache>();
var access = new DemoAccessSnapshotService(store, cache);
var service = new CrmApplicationService(store, access, new InMemoryCustomerQueryStore(store));
var salesPipeline = new SalesPipelineService(store, access);
var quoteService = new QuoteApplicationService(store, access, new DemoProductPriceCatalog());
var orderService = new OrderApplicationService(store, access, new DemoAccountingCreditProvider(), new DemoErpOrderGateway());
var dealerService = new DealerApplicationService(store, access, new DemoDealerFinancialProjectionProvider(),
    new DemoDealerPerformanceProjectionProvider());
var customer360 = new Customer360Service(store, access);
var identity = new IdentityApplicationService(store, access, new IdentityRuntimeOptions(
    "Demo@1405", TimeSpan.FromMinutes(30), TimeSpan.FromHours(8), TimeSpan.FromMinutes(1), 3));
var organization = new OrganizationContextService(store, access);
var organizationAdmin = new OrganizationAdminService(store, access);
var companyOne = new OrganizationSelection("C01", null, null);
var expertBranch = new OrganizationSelection("C01", "B01", null);
var companyTwo = new OrganizationSelection("C02", null, null);

Check(service.GetCustomers(managerId, companyOne).Count == 4, "Company context C01 must expose only its four customers.");
var pagedCustomers = service.SearchCustomers(managerId, companyOne, null, 1, 10);
Check(pagedCustomers.TotalCount == 4 && pagedCustomers.Page == 1 && pagedCustomers.TotalPages == 1,
    "Customer search must return stable server-query pagination metadata.");
Check(service.GetCustomers(expertId, expertBranch).Count == 1, "Branch role must pre-filter customer queries despite base company membership.");
Check(service.GetLeads(managerId, companyOne).Count == 3, "C01 must contain three open leads without leaking the C02 lead.");
Check(service.GetDashboard(managerId, companyOne).OpenTaskCount == 3, "C01 dashboard must contain only its three work items.");

var seededCustomerId = Guid.Parse("20000000-0000-4000-8000-000000000001");
var seededCustomer360 = customer360.Get(managerId, companyOne, seededCustomerId);
Check(seededCustomer360 is not null && seededCustomer360.Contacts.Count >= 2 && seededCustomer360.Addresses.Count >= 2,
    "Customer 360 must aggregate contacts and addresses for a visible customer.");
Check(seededCustomer360 is not null && seededCustomer360.Timeline.Count >= 2 && seededCustomer360.Sources.Count == 2,
    "Customer 360 must expose timeline and source freshness projections.");
Check(seededCustomer360 is not null && seededCustomer360.Orders.Any(x => x.Code == "OR-1405-001"),
    "Customer 360 must expose scoped order visibility for the customer.");
Check(customer360.Get(expertId, expertBranch, Guid.Parse("20000000-0000-4000-8000-000000000002")) is null,
    "Customer 360 must not enumerate a customer outside the expert branch scope.");
var exactDuplicate = customer360.CheckDuplicates(managerId, companyOne, "نام متفاوت", "تهران", "۱۰۱۰۱۲۳۴۵۶۷", null, null);
Check(exactDuplicate.HasBlockingExactMatch, "Normalized Persian digits must still detect an exact national-id duplicate.");
Check(customer360.GetDataQuality(managerId, companyOne).Count == 4,
    "The data-quality dashboard must remain company scoped.");

var customer = service.CreateCustomer(managerId, companyOne, new CreateCustomerCommand("مشتری آزمون معماری", "تهران", "سارا احمدی", "B01", "استاندارد"));
Check(service.GetCustomer(managerId, companyOne, customer.Id) is not null, "Created customer must be queryable inside its company context.");
Check(service.GetCustomer(managerId, companyTwo, customer.Id) is null, "A C01 record identifier must not be enumerable from C02.");
customer360.AddContact(managerId, companyOne, customer.Id,
    new AddCustomerContactCommand("تماس آزمون", "خرید", "09120000000", null, true, ContactConsentStatus.Granted), DateTimeOffset.UtcNow);
customer360.AddAddress(managerId, companyOne, customer.Id,
    new AddCustomerAddressCommand(CustomerAddressType.Registered, "دفتر آزمون", "تهران", "تهران", "نشانی تست", "1111111111", true), DateTimeOffset.UtcNow);
var edit = customer360.GetEdit(managerId, companyOne, customer.Id)!;
customer360.Update(managerId, companyOne, customer.Id,
    new UpdateCustomerCommand(edit.Name, edit.City, "محمد رضایی", "B02", "T02", edit.Segment,
        edit.NationalId, edit.PrimaryPhone, edit.PrimaryEmail, "واگذاری به شعبه اصفهان", edit.ExpectedVersion), DateTimeOffset.UtcNow);
var updated360 = customer360.Get(managerId, companyOne, customer.Id)!;
Check(updated360.Contacts.Count == 1 && updated360.Addresses.Count == 1 && updated360.OwnershipHistory.Count == 2,
    "Customer master mutations must append contact, address and ownership history records.");
var staleCustomerVersionRejected = false;
try
{
    customer360.Update(managerId, companyOne, customer.Id,
        new UpdateCustomerCommand(edit.Name, edit.City, edit.Owner, edit.BranchId, edit.TerritoryId, edit.Segment,
            edit.NationalId, edit.PrimaryPhone, edit.PrimaryEmail, "stale test", edit.ExpectedVersion), DateTimeOffset.UtcNow);
}
catch (InvalidOperationException)
{
    staleCustomerVersionRejected = true;
}
Check(staleCustomerVersionRejected, "A stale customer master version must be rejected.");

var duplicateQueue = customer360.GetDuplicateReviewQueue(managerId, companyOne);
Check(duplicateQueue.Any(x => x.Status == DuplicateReviewStatus.Pending),
    "The manager duplicate-review queue must include the seeded pending case.");
var expertDuplicateReviewRejected = false;
try
{
    customer360.GetDuplicateReviewQueue(expertId, expertBranch);
}
catch (UnauthorizedAccessException)
{
    expertDuplicateReviewRejected = true;
}
Check(expertDuplicateReviewRejected, "Duplicate review must require Customer.MergeReview permission.");
var pendingDuplicate = duplicateQueue.First(x => x.Status == DuplicateReviewStatus.Pending);
var pendingPreviewRejected = false;
try
{
    customer360.GetMergePreview(managerId, companyOne, pendingDuplicate.Id, pendingDuplicate.PossibleDuplicateCustomerId);
}
catch (InvalidOperationException)
{
    pendingPreviewRejected = true;
}
Check(pendingPreviewRejected, "Merge dry run must require a confirmed duplicate-review decision.");
customer360.ReviewDuplicate(managerId, companyOne, pendingDuplicate.Id,
    new ReviewDuplicateCommand(DuplicateReviewStatus.Confirmed, "تأیید برای آزمون ادغام", pendingDuplicate.Version), DateTimeOffset.UtcNow);
var confirmedDuplicate = customer360.GetDuplicateReviewQueue(managerId, companyOne).Single(x => x.Id == pendingDuplicate.Id);
Check(confirmedDuplicate.Status == DuplicateReviewStatus.Confirmed,
    "A duplicate-review decision must be persisted with optimistic concurrency.");
customer360.AddContact(managerId, companyOne, confirmedDuplicate.CustomerId,
    new AddCustomerContactCommand("تماس اصلی نخل", "خرید", "09121110001", null, true, ContactConsentStatus.Granted), DateTimeOffset.UtcNow);
customer360.AddContact(managerId, companyOne, confirmedDuplicate.PossibleDuplicateCustomerId,
    new AddCustomerContactCommand("تماس اصلی ماهان", "خرید", "09121110002", null, true, ContactConsentStatus.Granted), DateTimeOffset.UtcNow);
customer360.AddAddress(managerId, companyOne, confirmedDuplicate.CustomerId,
    new AddCustomerAddressCommand(CustomerAddressType.Registered, "نشانی اصلی نخل", "خوزستان", "اهواز", "نشانی آزمون نخل", null, true), DateTimeOffset.UtcNow);
customer360.AddAddress(managerId, companyOne, confirmedDuplicate.PossibleDuplicateCustomerId,
    new AddCustomerAddressCommand(CustomerAddressType.Registered, "نشانی اصلی ماهان", "فارس", "شیراز", "نشانی آزمون ماهان", null, true), DateTimeOffset.UtcNow);
var mergePreview = customer360.GetMergePreview(managerId, companyOne, confirmedDuplicate.Id, confirmedDuplicate.PossibleDuplicateCustomerId);
Check(mergePreview.Contacts == 1 && mergePreview.Addresses == 1 && mergePreview.Opportunities == 1 &&
      mergePreview.Quotes == 1 && mergePreview.DealerAssignments == 1,
    "Merge dry run must report the affected master and commercial relationships.");
Check(mergePreview.Warnings.Any(x => x.Contains("تماس اصلی", StringComparison.Ordinal)) &&
      mergePreview.Warnings.Any(x => x.Contains("آدرس اصلی", StringComparison.Ordinal)),
    "Merge dry run must warn when both records have active primary contact/address data.");
var staleMergeRejected = false;
try
{
    customer360.Merge(managerId, companyOne, confirmedDuplicate.Id,
        new MergeCustomerCommand(mergePreview.Survivor.Id, "نسخه قدیمی", mergePreview.CandidateVersion - 1,
            mergePreview.SurvivorVersion, mergePreview.MergedVersion), DateTimeOffset.UtcNow);
}
catch (InvalidOperationException)
{
    staleMergeRejected = true;
}
Check(staleMergeRejected && customer360.GetMergeHistory(managerId, companyOne).Count == 0,
    "A stale merge command must be rejected before any relationship mutation or audit creation.");
var merge = customer360.Merge(managerId, companyOne, confirmedDuplicate.Id,
    new MergeCustomerCommand(mergePreview.Survivor.Id, "آزمون انتقال روابط", mergePreview.CandidateVersion,
        mergePreview.SurvivorVersion, mergePreview.MergedVersion), DateTimeOffset.UtcNow);
Check(customer360.GetMergeHistory(managerId, companyOne).Any(x => x.Id == merge.Id && x.Status == CustomerMergeStatus.Merged),
    "Confirmed duplicates must support an audited merge operation.");
var afterMerge = customer360.Get(managerId, companyOne, merge.SurvivorCustomerId, includeRelatedActivity: true)!;
Check(afterMerge.Opportunities.Any(x => x.Id == Guid.Parse("40000000-0000-4000-8000-000000000003")) &&
      afterMerge.Quotes.Any(x => x.Id == Guid.Parse("50000000-0000-4000-8000-000000000002")),
    "Merge must transfer opportunity and quote relationships to the survivor CustomerId.");
Check(store.Read(data => data.DealerCustomerAssignments.Any(x => x.CustomerId == merge.SurvivorCustomerId)),
    "Merge must transfer dealer-customer assignments to the survivor CustomerId.");
Check(afterMerge.Contacts.Single(x => x.FullName == "تماس اصلی نخل").IsPrimary is false &&
      afterMerge.Addresses.Single(x => x.Title == "نشانی اصلی نخل").IsPrimary is false,
    "Merge must demote transferred primary data when the survivor already has a primary record.");
Check(customer360.GetEdit(managerId, companyOne, merge.MergedCustomerId) is null,
    "An inactive merged customer must not expose an editable master-data model.");
var inactiveMutationRejected = false;
try
{
    customer360.AddContact(managerId, companyOne, merge.MergedCustomerId,
        new AddCustomerContactCommand("تماس نامعتبر", "خرید", "09121119999", null, false, ContactConsentStatus.Unknown), DateTimeOffset.UtcNow);
}
catch (InvalidOperationException)
{
    inactiveMutationRejected = true;
}
Check(inactiveMutationRejected, "An inactive merged customer must reject master-data mutations until Unmerge.");
Check(customer360.GetDuplicateReviewQueue(managerId, companyOne).Single(x => x.Id == confirmedDuplicate.Id).HasActiveMerge,
    "The duplicate queue must expose the active merged state instead of offering a second merge.");
var transferredQuoteId = Guid.Parse("50000000-0000-4000-8000-000000000002");
store.Write(data =>
{
    var alternate = data.Customers.Single(x => x.Id == seededCustomerId);
    data.Quotes.Single(x => x.Id == transferredQuoteId).ReassignCustomer(alternate.Id, alternate.Name);
    return true;
});
var changedRelationshipRejected = false;
try
{
    customer360.Unmerge(managerId, companyOne, merge.Id,
        new UnmergeCustomerCommand("نباید با رابطه تغییرکرده اجرا شود", merge.Version), DateTimeOffset.UtcNow);
}
catch (InvalidOperationException)
{
    changedRelationshipRejected = true;
}
Check(changedRelationshipRejected &&
      customer360.Get(managerId, companyOne, merge.SurvivorCustomerId, includeRelatedActivity: true)!.Contacts.Any(x => x.FullName == "تماس اصلی نخل"),
    "Unmerge must reject a changed manifest relationship before performing any partial restoration.");
store.Write(data =>
{
    var survivor = data.Customers.Single(x => x.Id == merge.SurvivorCustomerId);
    data.Quotes.Single(x => x.Id == transferredQuoteId).ReassignCustomer(survivor.Id, survivor.Name);
    return true;
});
customer360.Unmerge(managerId, companyOne, merge.Id, new UnmergeCustomerCommand("آزمون بازگردانی", merge.Version), DateTimeOffset.UtcNow);
Check(customer360.GetMergeHistory(managerId, companyOne).Single(x => x.Id == merge.Id).Status == CustomerMergeStatus.Reverted,
    "Unmerge must restore the customer and close the merge audit record.");
var restoredSource = customer360.Get(managerId, companyOne, merge.MergedCustomerId, includeRelatedActivity: true)!;
Check(restoredSource.Opportunities.Any(x => x.Id == Guid.Parse("40000000-0000-4000-8000-000000000003")) &&
      restoredSource.Quotes.Any(x => x.Id == Guid.Parse("50000000-0000-4000-8000-000000000002")),
    "Unmerge must restore every relationship recorded in the transfer manifest.");
Check(store.Read(data => data.DealerCustomerAssignments.Any(x => x.CustomerId == merge.MergedCustomerId)),
    "Unmerge must restore dealer-customer assignments recorded in the transfer manifest.");
Check(restoredSource.Contacts.Single(x => x.FullName == "تماس اصلی نخل").IsPrimary &&
      restoredSource.Addresses.Single(x => x.Title == "نشانی اصلی نخل").IsPrimary,
    "Unmerge must restore the original primary contact/address flags from the manifest.");
Check(!customer360.GetDuplicateReviewQueue(managerId, companyOne).Single(x => x.Id == confirmedDuplicate.Id).HasActiveMerge,
    "A reverted merge must no longer be exposed as active in the duplicate queue.");

var qualifiedLead = service.GetLeads(managerId, companyOne).Single(x => x.Status == Crm.Domain.Sales.LeadStatus.Qualified);
var opportunity = service.ConvertLead(managerId, companyOne, qualifiedLead.Id);
Check(opportunity.Customer == qualifiedLead.Name, "Lead conversion must create a matching opportunity.");
Check(opportunity.CustomerId != Guid.Empty, "Lead conversion must link the opportunity to CustomerId.");
Check(service.GetLeads(managerId, companyOne).All(x => x.Id != qualifiedLead.Id), "Converted lead must leave the open list.");

var quoteCustomer = service.GetCustomers(managerId, companyOne).Single(x => x.Id == opportunity.CustomerId);
var quote = service.CreateQuote(managerId, companyOne, new CreateQuoteCommand(quoteCustomer.Name, opportunity.Title,
    1_000_000m, 10, 22, opportunity.BranchId, quoteCustomer.Id, opportunity.Id));
Check(quote.Status == QuoteStatus.PendingApproval, "Quote above five percent discount must require approval.");
Check(quote.CustomerId == quoteCustomer.Id && quote.OpportunityId == opportunity.Id,
    "Quote must persist CustomerId and OpportunityId references.");
Check(service.DecideQuote(managerId, companyOne, quote.Id, true).Status == QuoteStatus.Approved, "Valid pending quote must be approvable.");
Check(QuoteApprovalPolicy.Resolve(5, 25) == QuoteApprovalLevel.None &&
      QuoteApprovalPolicy.Resolve(8, 22) == QuoteApprovalLevel.SalesSupervisor &&
      QuoteApprovalPolicy.Resolve(12, 18) == QuoteApprovalLevel.CommercialManager &&
      QuoteApprovalPolicy.Resolve(20, 15) == QuoteApprovalLevel.JointSalesAndFinance,
    "Quote approval boundaries must match the signed-off priority-6 matrix.");
var outsideQuotePolicyRejected = false;
try { QuoteApprovalPolicy.Resolve(20.01m, 30); }
catch (InvalidOperationException) { outsideQuotePolicyRejected = true; }
Check(outsideQuotePolicyRejected, "A discount above twenty percent must be rejected before approval routing.");

var pipelineNow = DateTimeOffset.UtcNow;
var managedLead = salesPipeline.CreateLead(managerId, companyOne,
    new CreateLeadCommand("آزمون پایپ‌لاین", "مدیر خرید", "وب‌سایت", "سارا احمدی", "B01",
        expertId, "09120001122", "PIPELINE@TEST.LOCAL", "T01"), pipelineNow);
Check(managedLead.Status == LeadStatus.Assigned && managedLead.OwnerUserId == expertId &&
      managedLead.SlaState == LeadSlaState.OnTrack,
    "Priority 5 lead creation must assign an eligible owner and start the first-contact SLA.");
var contactedLead = salesPipeline.TransitionLead(expertId, expertBranch, managedLead.Id,
    new TransitionLeadCommand(LeadStatus.Contacted, managedLead.Score, "تماس موفق", "جلسه کشف نیاز",
        pipelineNow.AddDays(1), managedLead.Version), pipelineNow.AddMinutes(5));
var qualifiedPipelineLead = salesPipeline.TransitionLead(expertId, expertBranch, managedLead.Id,
    new TransitionLeadCommand(LeadStatus.Qualified, 90, "نیاز و بودجه تأیید شد", null, null,
        contactedLead.Version), pipelineNow.AddMinutes(10));
var convertedPipelineOpportunity = salesPipeline.ConvertLead(expertId, expertBranch, managedLead.Id,
    new ConvertLeadCommand("فرصت آزمون پایپ‌لاین", 3_000_000_000m, pipelineNow.AddDays(35),
        qualifiedPipelineLead.Version), pipelineNow.AddMinutes(15));
var convertedLeadDetails = salesPipeline.GetLead(expertId, expertBranch, managedLead.Id, pipelineNow.AddMinutes(16))!;
Check(convertedLeadDetails.Lead.ConvertedOpportunityId == convertedPipelineOpportunity.Id &&
      convertedLeadDetails.History.Count >= 5,
    "Lead conversion must preserve source lineage and append auditable status history.");
Check(!convertedLeadDetails.CanAssign && !convertedLeadDetails.CanUpdate && !convertedLeadDetails.CanConvert,
    "A converted Lead must not expose assignment, update or conversion actions.");
var pipelineDetails = salesPipeline.GetOpportunity(expertId, expertBranch, convertedPipelineOpportunity.Id)!;
var movedOpportunity = salesPipeline.MoveOpportunity(expertId, expertBranch, convertedPipelineOpportunity.Id,
    new MoveOpportunityStageCommand(OpportunityStage.Discovery, "جلسه کشف برنامه‌ریزی شد",
        pipelineDetails.Opportunity.Version), pipelineNow.AddMinutes(20));
salesPipeline.AddActivity(expertId, expertBranch, movedOpportunity.Id,
    new AddOpportunityActivityCommand(OpportunityActivityType.Meeting, "جلسه کشف", "نیاز اولیه ثبت شد",
        pipelineNow.AddMinutes(25), "ارسال جمع‌بندی", pipelineNow.AddDays(2), movedOpportunity.Version));
var auditedOpportunity = salesPipeline.GetOpportunity(expertId, expertBranch, movedOpportunity.Id)!;
Check(auditedOpportunity.StageHistory.Count >= 2 && auditedOpportunity.Activities.Count >= 2,
    "Opportunity stage changes and activities must append immutable audit records.");
Check(!salesPipeline.GetPipeline(expertId, expertBranch).Items.Any(x => x.OwnerUserId != expertId),
    "Sales experts must only enumerate opportunities assigned to themselves.");
var stageCandidate = auditedOpportunity.Opportunity;
foreach (var target in new[]
{
    OpportunityStage.Qualified,
    OpportunityStage.SolutionOffer,
    OpportunityStage.Negotiation,
    OpportunityStage.Commit
})
{
    stageCandidate = salesPipeline.MoveOpportunity(expertId, expertBranch, stageCandidate.Id,
        new MoveOpportunityStageCommand(target, "آزمون تکمیل مرحله", stageCandidate.Version),
        pipelineNow.AddMinutes(30 + (int)target));
}
var automaticDraft = quoteService.CreateDraft(managerId, companyOne,
    new CreateQuoteDraftCommand(stageCandidate.CustomerId, stageCandidate.Id, stageCandidate.BranchId, "IRR",
        pipelineNow.AddDays(20), "تسویه ۳۰ روزه", "PRD-1002", 1, 4), pipelineNow.AddMinutes(45));
var staleQuoteVersionRejected = false;
try
{
    quoteService.AddLine(managerId, companyOne, automaticDraft.Quote.Id,
        new AddQuoteLineCommand("SRV-2001", 1, 4, automaticDraft.Quote.Version - 1), pipelineNow.AddMinutes(46));
}
catch (InvalidOperationException) { staleQuoteVersionRejected = true; }
Check(staleQuoteVersionRejected, "Quote line mutations must reject a stale aggregate version.");
var automaticallyApproved = quoteService.Submit(managerId, companyOne, automaticDraft.Quote.Id,
    new SubmitQuoteCommand("در محدوده اختیار کارشناس", automaticDraft.Quote.Version), pipelineNow.AddMinutes(47));
Check(automaticallyApproved.Quote.Status == QuoteStatus.Approved && automaticallyApproved.PendingRoles.Count == 0 &&
      automaticallyApproved.History.Any(x => x.ToStatus == QuoteStatus.Submitted),
    "A quote inside the expert threshold must auto-approve while preserving the Submitted audit event.");
var revisedQuote = quoteService.Revise(managerId, companyOne, automaticallyApproved.Quote.Id,
    new ReviseQuoteCommand(pipelineNow.AddDays(40), "بازنگری شرایط اعتبار", automaticallyApproved.Quote.Version),
    pipelineNow.AddMinutes(48));
Check(revisedQuote.Quote.Revision == 2 && revisedQuote.Quote.Status == QuoteStatus.Draft &&
      store.Read(data => data.Quotes.Single(x => x.Id == revisedQuote.Quote.Id).ParentQuoteId == automaticallyApproved.Quote.Id),
    "A governed revision must be a new Draft linked to its immutable parent quote.");
var governedDraft = quoteService.CreateDraft(managerId, companyOne,
    new CreateQuoteDraftCommand(stageCandidate.CustomerId, stageCandidate.Id, stageCandidate.BranchId, "IRR",
        pipelineNow.AddDays(30), "تسویه ۳۰ روزه", "PRD-1001", 1, 15), pipelineNow.AddMinutes(50));
var governedPending = quoteService.Submit(managerId, companyOne, governedDraft.Quote.Id,
    new SubmitQuoteCommand("کنترل تخفیف مشترک", governedDraft.Quote.Version), pipelineNow.AddMinutes(51));
Check(governedPending.Quote.ApprovalLevel == QuoteApprovalLevel.JointSalesAndFinance &&
      governedPending.PendingRoles.Count == 2, "High discount must require separate sales and finance approvals.");
var salesApproved = quoteService.Decide(managerId, companyOne, governedPending.Quote.Id,
    new DecideQuoteCommand(QuoteDecision.Approved, "تأیید مدیر فروش", governedPending.Quote.Version), pipelineNow.AddMinutes(52));
Check(salesApproved.Quote.Status == QuoteStatus.PendingApproval && salesApproved.PendingRoles.SequenceEqual([QuoteApprovalRole.FinanceManager]),
    "Joint approval must remain pending until the finance decision is recorded.");
var financeApproved = quoteService.Decide(financeManagerId, companyOne, salesApproved.Quote.Id,
    new DecideQuoteCommand(QuoteDecision.Approved, "تأیید مدیر مالی", salesApproved.Quote.Version), pipelineNow.AddMinutes(53));
var sentQuote = quoteService.MarkSent(managerId, companyOne, financeApproved.Quote.Id,
    financeApproved.Quote.Version, pipelineNow.AddMinutes(54));
var acceptedQuote = quoteService.RecordOutcome(managerId, companyOne, sentQuote.Quote.Id,
    new QuoteOutcomeCommand(true, "پذیرش مشتری", sentQuote.Quote.Version), pipelineNow.AddMinutes(55));
Check(acceptedQuote.Quote.Status == QuoteStatus.Accepted && acceptedQuote.Decisions.Count == 2,
    "An accepted quote must retain both immutable approval decisions.");
var createdOrder = orderService.Create(managerId, companyOne,
    new CreateOrderRequestCommand(acceptedQuote.Quote.Id, acceptedQuote.Quote.Version), pipelineNow.AddMinutes(56));
Check(createdOrder.Order.Status == OrderRequestStatus.Draft && createdOrder.History.Count == 1,
    "An accepted quote must create exactly one auditable order request.");
var duplicateOrderRejected = false;
try
{
    orderService.Create(managerId, companyOne,
        new CreateOrderRequestCommand(acceptedQuote.Quote.Id, acceptedQuote.Quote.Version), pipelineNow.AddMinutes(57));
}
catch (InvalidOperationException) { duplicateOrderRejected = true; }
Check(duplicateOrderRejected, "A quote revision must not create more than one order request.");
var creditApprovedOrder = orderService.CheckCredit(managerId, companyOne, createdOrder.Order.Id,
    new CheckOrderCreditCommand(createdOrder.Order.Version), pipelineNow.AddMinutes(58));
Check(creditApprovedOrder.Order.Status == OrderRequestStatus.CreditApproved &&
      creditApprovedOrder.CreditDecisions.Single().Decision == OrderCreditDecisionType.Approved,
    "Accounting snapshot must produce a persisted credit decision before ERP submission.");
var queuedOrder = orderService.QueueSubmission(managerId, companyOne, createdOrder.Order.Id,
    new QueueOrderSubmissionCommand(creditApprovedOrder.Order.Version), pipelineNow.AddMinutes(59));
var idempotencyKey = queuedOrder.Integration!.IdempotencyKey;
var failedOrder = orderService.ProcessIntegration(managerId, companyOne, createdOrder.Order.Id,
    new ProcessOrderIntegrationCommand(ErpSubmissionOutcome.TransientFailure, queuedOrder.Order.Version,
        queuedOrder.Integration.Version), pipelineNow.AddHours(1));
Check(failedOrder.Order.Status == OrderRequestStatus.IntegrationFailed &&
      failedOrder.Integration?.Status == OrderIntegrationStatus.RetryScheduled &&
      failedOrder.Integration?.IdempotencyKey == idempotencyKey,
    "A transient ERP failure must schedule retry without changing the idempotency key.");
var acceptedOrder = orderService.ProcessIntegration(managerId, companyOne, createdOrder.Order.Id,
    new ProcessOrderIntegrationCommand(ErpSubmissionOutcome.Accepted, failedOrder.Order.Version,
        failedOrder.Integration!.Version), pipelineNow.AddHours(1).AddSeconds(16));
Check(acceptedOrder.Order.Status == OrderRequestStatus.ErpAccepted && acceptedOrder.Attempts.Count == 2 &&
      acceptedOrder.Integration?.AttemptCount == 2 && acceptedOrder.Integration?.IdempotencyKey == idempotencyKey,
    "Retry must accept the order with the same message identity and an immutable second attempt.");
var projectedOrder = acceptedOrder;
foreach (var target in new[]
{
    OrderRequestStatus.Allocated,
    OrderRequestStatus.Delivered,
    OrderRequestStatus.Invoiced,
    OrderRequestStatus.Paid
})
{
    projectedOrder = orderService.AdvanceProjection(managerId, companyOne, createdOrder.Order.Id,
        new AdvanceOrderProjectionCommand(target, $"ERP-{target}-1405", projectedOrder.Order.Version),
        pipelineNow.AddHours(2 + (int)target));
}
Check(projectedOrder.Order.Status == OrderRequestStatus.Paid &&
      !string.IsNullOrWhiteSpace(projectedOrder.DeliveryReference) &&
      !string.IsNullOrWhiteSpace(projectedOrder.InvoiceNumber) &&
      !string.IsNullOrWhiteSpace(projectedOrder.PaymentReference),
    "ERP projections must advance in order through allocation, delivery, invoice and payment.");
var heldSeedOrder = orderService.Get(financeManagerId, companyOne,
    Guid.Parse("55000000-0000-4000-8000-000000000001"), pipelineNow)!;
var overriddenSeedOrder = orderService.OverrideCredit(financeManagerId, companyOne, heldSeedOrder.Order.Id,
    new OverrideOrderCreditCommand("مجوز محدود مدیر مالی برای آزمون", pipelineNow.AddDays(2),
        heldSeedOrder.Order.Version), pipelineNow.AddMinutes(59));
Check(overriddenSeedOrder.Order.Status == OrderRequestStatus.CreditApproved &&
      overriddenSeedOrder.CreditDecisions.First().Decision == OrderCreditDecisionType.Overridden,
    "Finance must be able to issue an expiring, auditable override for a credit hold.");

var seededDealer = dealerService.GetWorkspace(channelManagerId, companyOne, pipelineNow);
Check(seededDealer.Items.Count == 1 && seededDealer.ActiveCount == 1 && seededDealer.Items[0].CustomerCount == 2,
    "Channel workspace must expose the active pilot dealer, its governed contract and customer portfolio.");
var exactDealerScope = dealerService.GetWorkspace(dealerUserId, companyOne, pipelineNow);
Check(exactDealerScope.Items.Count == 1 && exactDealerScope.Items[0].DealerId == "P-D01" &&
      dealerService.Get(dealerUserId, companyOne, exactDealerScope.Items[0].Id, pipelineNow)!.CanEdit is false,
    "Dealer scope must enumerate only the exact dealer and must remain read-only for the portal user.");
Check(dealerService.Get(managerId, companyOne, seededDealer.Items[0].Id, pipelineNow)!.Financial is null,
    "Financial projection must be masked when Dealer.Financial.Read is absent.");
var seededTargetForm = dealerService.GetTargetForm(channelManagerId, companyOne, seededDealer.Items[0].Id, pipelineNow);
Check(seededTargetForm.Command.ExpectedVersion > 0 && seededTargetForm.Command.Amount == 7_000_000_000m,
    "Target form must return the current version so an existing target can be updated safely.");

var draftDealer = dealerService.Save(channelManagerId, companyOne, null,
    new SaveDealerCommand("P-D99", "DLR-0099", "شرکت نماینده آزمون", "نماینده آزمون اولویت هشت",
        "B03", "T02", "اهواز", "14009999999", "06130000000", "dealer99@test.local",
        channelManagerId, 0), pipelineNow);
var activationWithoutPrerequisitesRejected = false;
try
{
    dealerService.ChangeStatus(channelManagerId, companyOne, draftDealer.Dealer.Id,
        new ChangeDealerStatusCommand(DealerStatus.PendingApproval, "ارسال زودهنگام", draftDealer.Dealer.Version), pipelineNow);
}
catch (InvalidOperationException) { activationWithoutPrerequisitesRejected = true; }
Check(activationWithoutPrerequisitesRejected,
    "Dealer submission must require an active contract and an active Territory assignment.");
var draftContractDealer = dealerService.SaveContract(managerId, companyOne, draftDealer.Dealer.Id, null,
    new SaveDealerContractCommand("CNT-D99-1405", pipelineNow.AddDays(-1), pipelineNow.AddYears(1),
        1_000_000_000m, "تسویه ۳۰ روزه", 0), pipelineNow);
var draftContract = draftContractDealer.Contracts.Single(x => x.ContractNumber == "CNT-D99-1405");
var pendingContractDealer = dealerService.SubmitContract(managerId, companyOne, draftDealer.Dealer.Id,
    draftContract.Id, new DecideDealerContractCommand("ارسال برای تأیید", draftContract.Version), pipelineNow);
var pendingContract = pendingContractDealer.Contracts.Single(x => x.Id == draftContract.Id);
var sameActorApprovalRejected = false;
try
{
    dealerService.ApproveContract(managerId, companyOne, draftDealer.Dealer.Id, pendingContract.Id,
        new DecideDealerContractCommand("تأیید توسط درخواست‌کننده", pendingContract.Version), pipelineNow);
}
catch (UnauthorizedAccessException) { sameActorApprovalRejected = true; }
Check(sameActorApprovalRejected, "Contract approval must require the dedicated channel approval permission.");
var contractedDealer = dealerService.ApproveContract(channelManagerId, companyOne, draftDealer.Dealer.Id,
    pendingContract.Id, new DecideDealerContractCommand("تأیید مستقل قرارداد", pendingContract.Version), pipelineNow);
var territoryRequested = dealerService.RequestTerritory(managerId, companyOne, draftDealer.Dealer.Id,
    new AssignDealerTerritoryCommand("T01", false, pipelineNow.AddDays(-1), pipelineNow.AddYears(1)), pipelineNow);
var proposedTerritory = territoryRequested.Territories.Single(x => x.TerritoryId == "T01");
var territoryApproved = dealerService.ApproveTerritory(channelManagerId, companyOne, draftDealer.Dealer.Id,
    proposedTerritory.Id, new DecideDealerTerritoryCommand("کنترل عدم تعارض", proposedTerritory.Version), pipelineNow);
var submittedDealer = dealerService.ChangeStatus(channelManagerId, companyOne, draftDealer.Dealer.Id,
    new ChangeDealerStatusCommand(DealerStatus.PendingApproval, "پیش‌نیازها تکمیل شد", territoryApproved.Dealer.Version), pipelineNow);
var activatedDealer = dealerService.ChangeStatus(channelManagerId, companyOne, draftDealer.Dealer.Id,
    new ChangeDealerStatusCommand(DealerStatus.Active, "تأیید نهایی", submittedDealer.Dealer.Version), pipelineNow);
Check(activatedDealer.Dealer.Status == DealerStatus.Active && activatedDealer.Contracts.Any(x => x.Status == DealerContractStatus.Active) &&
      activatedDealer.Territories.Any(x => x.Status == DealerTerritoryStatus.Active),
    "A dealer with approved contract and Territory must complete the governed activation workflow.");
var lastContractEndRejected = false;
var activeDraftContract = activatedDealer.Contracts.Single(x => x.ContractNumber == "CNT-D99-1405");
try
{
    dealerService.EndContract(channelManagerId, companyOne, draftDealer.Dealer.Id, activeDraftContract.Id,
        new EndDealerRelationshipCommand("خاتمه نامعتبر آخرین قرارداد", activeDraftContract.Version), pipelineNow.AddHours(3));
}
catch (InvalidOperationException) { lastContractEndRejected = true; }
Check(lastContractEndRejected, "An active dealer must not lose its last effective contract without suspension or replacement.");
var dealerWithCustomer = dealerService.AssignCustomer(managerId, companyOne, draftDealer.Dealer.Id,
    new AssignDealerCustomerCommand(Guid.Parse("20000000-0000-4000-8000-000000000001"), "سبد آزمون خاتمه"),
    pipelineNow.AddHours(4));
var terminatedDealer = dealerService.ChangeStatus(channelManagerId, companyOne, draftDealer.Dealer.Id,
    new ChangeDealerStatusCommand(DealerStatus.Terminated, "خاتمه همکاری آزمایشی", dealerWithCustomer.Dealer.Version),
    pipelineNow.AddHours(5));
Check(terminatedDealer.Dealer.Status == DealerStatus.Terminated &&
      terminatedDealer.Contracts.All(x => x.Status != DealerContractStatus.Active) &&
      terminatedDealer.Territories.All(x => x.Status != DealerTerritoryStatus.Active) &&
      terminatedDealer.Customers.All(x => !x.IsActive && x.EndedByUserId == channelManagerId),
    "Dealer termination must close active contracts, Territories and customer portfolio assignments atomically.");
var wonOpportunity = salesPipeline.MoveOpportunity(managerId, companyOne, stageCandidate.Id,
    new MoveOpportunityStageCommand(OpportunityStage.Won, "توافق نهایی آزمون", stageCandidate.Version),
    pipelineNow.AddHours(1));
var closedOpportunity = salesPipeline.GetOpportunity(managerId, companyOne, wonOpportunity.Id)!;
Check(closedOpportunity.Opportunity.Stage == OpportunityStage.Won && !closedOpportunity.CanAssign &&
      !closedOpportunity.CanUpdate && !closedOpportunity.CanClose,
    "A closed Opportunity must not expose assignment, update or close actions.");

Check(access.HasPermission(managerId, "C01", "Administration.Manage"), "Demo manager must have C01 administration permission.");
Check(access.HasPermission(managerId, "C02", "Administration.Manage"), "Demo manager must have C02 administration permission.");
Check(!access.HasPermission(expertId, "C01", "Administration.Manage"), "Sales expert must not have administration permission.");

var now = DateTimeOffset.UtcNow;
IdentityRequestContext Context(string suffix) => new(now, "IP-HASH", "ArchitectureTests", "test-" + suffix);
var initialOrganization = organizationAdmin.Get("C01", managerId, now);
Check(initialOrganization.Units.Count == 5 && initialOrganization.Territories.Count == 2,
    "C01 organization administration must be isolated to its own units and territories.");
organizationAdmin.SaveUnit("C01", null,
    new SaveOrganizationUnitCommand("ST-TEST", "P-ST-TEST", "تیم فروش آزمون", OrganizationUnitType.SalesTeam, "B01", 0),
    managerId, Context("organization-create"));
var createdTeam = organizationAdmin.Get("C01", managerId, now).Units.Single(x => x.UnitId == "ST-TEST");
organizationAdmin.SetUnitStatus("C01", createdTeam.Id, OrganizationStatus.Inactive, createdTeam.Version,
    managerId, Context("organization-status"));
Check(organizationAdmin.Get("C01", managerId, now).Changes.Count >= 2,
    "Organization create and status mutations must append immutable change history.");
var staleOrganizationVersionRejected = false;
try
{
    organizationAdmin.SaveUnit("C01", createdTeam.Id,
        new SaveOrganizationUnitCommand(createdTeam.UnitId, createdTeam.Code, "نام قدیمی", createdTeam.Type, createdTeam.ParentUnitId, createdTeam.Version),
        managerId, Context("organization-stale"));
}
catch (InvalidOperationException)
{
    staleOrganizationVersionRejected = true;
}
Check(staleOrganizationVersionRejected, "A stale organization version must be rejected.");
var crossCompanyAdminRejected = false;
try
{
    organizationAdmin.Get("C02", expertId, now);
}
catch (UnauthorizedAccessException)
{
    crossCompanyAdminRejected = true;
}
Check(crossCompanyAdminRejected, "Organization administration must require permission in the target company.");
var expiredUser = new CrmUser(Guid.NewGuid(), "Expired User", "expired.user", "EXPIRED@DEMO.CRM", null,
    UserStatus.Active, now.AddHours(-2), now.AddHours(-1));
Check(!expiredUser.IsActiveAt(now), "An active status outside its access window must not be sign-in eligible.");
var exactBinding = identity.AuthenticateExternal(new ExternalIdentityDescriptor(
    "CorporateOidc", "https://login.example.test/tenant/v2.0/", "demo-manager-sub", null, false), Context("exact"));
Check(exactBinding.Succeeded, "Existing identities must match the normalized Issuer + exact Subject pair.");
Check(exactBinding.RequiresOrganizationSelection && exactBinding.SessionId is not null,
    "A multi-company user must select an organization context after sign-in.");
Check(organization.GetAvailableCompanies(managerId, exactBinding.SessionId!.Value).Count == 2,
    "The manager context selector must expose exactly two permitted companies.");
var selectedC01 = organization.Select(managerId, exactBinding.SessionId.Value,
    new SelectOrganizationContextCommand("C01", null, null, "/"), Context("context-c01"));
Check(selectedC01.Succeeded && selectedC01.Context?.SelectedCompanyId == "C01",
    "A permitted company must be stored in the server-side session context.");
var rejectedCrossCompanyBranch = organization.Select(managerId, exactBinding.SessionId.Value,
    new SelectOrganizationContextCommand("C02", "B01", null, "/"), Context("context-tamper"));
Check(!rejectedCrossCompanyBranch.Succeeded, "A branch from another company must be rejected as context tampering.");
var selectedC02 = organization.Select(managerId, exactBinding.SessionId.Value,
    new SelectOrganizationContextCommand("C02", null, "T21", "/"), Context("context-c02"));
Check(selectedC02.Succeeded && service.GetCustomers(managerId, companyTwo).Count == 2,
    "Switching to C02 must reveal only the two C02 customers.");
Check(identity.GetUser(managerId)!.AuditEvents.Any(x => x.EventType == "UserContext.CompanyChanged"),
    "Organization context changes must be security-audited.");
for (var index = 0; index < 4; index++) identity.AuthenticateDemo("sales.manager", "Demo@1405", Context("concurrent-" + index));
Check(identity.GetUser(managerId)!.User.ActiveSessionCount == 3, "Concurrent session limit must keep at most three active sessions.");

var unverified = identity.AuthenticateExternal(new ExternalIdentityDescriptor(
    "CorporateOidc", "https://issuer.example", "pending-sub", "pending@demo.crm", false), Context("unverified"));
Check(unverified.FailureReason == SignInFailureReason.EmailNotVerified, "First link must reject an unverified email.");

var unknownUserCount = identity.GetUsers().Count;
var unknown = identity.AuthenticateExternal(new ExternalIdentityDescriptor(
    "CorporateOidc", "https://issuer.example", "unknown-sub", "unknown@demo.crm", true), Context("unknown"));
Check(unknown.FailureReason == SignInFailureReason.UnknownIdentity && identity.GetUsers().Count == unknownUserCount,
    "An unknown external identity must never create an active CRM user.");

store.Write(data =>
{
    data.Users.Add(new CrmUser(Guid.NewGuid(), "Ambiguous One", "ambiguous.one", "AMBIGUOUS@DEMO.CRM"));
    data.Users.Add(new CrmUser(Guid.NewGuid(), "Ambiguous Two", "ambiguous.two", "AMBIGUOUS@DEMO.CRM"));
    return true;
});
var ambiguous = identity.AuthenticateExternal(new ExternalIdentityDescriptor(
    "CorporateOidc", "https://issuer.example", "ambiguous-sub", "ambiguous@demo.crm", true), Context("ambiguous"));
Check(ambiguous.FailureReason == SignInFailureReason.AmbiguousBinding, "Ambiguous pending-user binding must be rejected.");

var linked = identity.AuthenticateExternal(new ExternalIdentityDescriptor(
    "CorporateOidc", "https://issuer.example", "pending-sub", "pending@demo.crm", true), Context("link"));
Check(linked.Succeeded && linked.User?.Id == pendingId, "A unique pending user may be linked by verified email.");
Check(identity.GetUser(pendingId)?.ExternalIdentities.Count == 1, "The new external identity must be persisted.");

var demo = identity.AuthenticateDemo("sales.expert", "Demo@1405", Context("demo"));
Check(demo.Succeeded && demo.SessionId is not null, "Demo login must issue a server-side session.");
Check(!demo.RequiresOrganizationSelection && demo.SelectedCompanyId == "C01",
    "A single-company expert must receive an automatic company context.");
Check(organization.GetCurrent(expertId, demo.SessionId!.Value)?.SelectedBranchId == "B01",
    "A single-branch expert must receive an automatic branch context.");
var validation = identity.ValidateSession(demo.SessionId!.Value, expertId, demo.User!.SecurityVersion, Context("validate"));
Check(validation.IsValid, "A fresh session must validate.");
identity.RevokeSession(expertId, demo.SessionId.Value, managerId, Context("revoke-session"));
Check(!identity.ValidateSession(demo.SessionId.Value, expertId, demo.User.SecurityVersion, Context("validate-revoked")).IsValid,
    "A revoked session must fail validation immediately.");

var versionBeforeRoleChange = identity.GetUser(expertId)!.User.SecurityVersion;
identity.AssignRole(expertId, new AssignRoleCommand("SalesExpert", "Branch", "B02", "Architecture test", null, versionBeforeRoleChange), managerId, Context("role"));
Check(identity.GetUser(expertId)!.User.SecurityVersion == versionBeforeRoleChange + 1,
    "Role assignment must increment SecurityVersion.");
Check(access.Get(expertId)!.Covers("C01", "Branch", "B02"), "The invalidated access snapshot must include the new branch scope.");
Check(identity.GetUser(expertId)!.AuditEvents.Count > 0, "Security-sensitive actions must create audit events.");
var staleConcurrencyRejected = false;
try
{
    identity.AssignRole(expertId, new AssignRoleCommand("SalesExpert", "Branch", "B03", "Stale architecture test", null, versionBeforeRoleChange), managerId, Context("stale"));
}
catch (InvalidOperationException)
{
    staleConcurrencyRejected = true;
}
Check(staleConcurrencyRejected, "A stale SecurityVersion must be rejected as an optimistic concurrency conflict.");

ReportingChecks.Run(Check);
SelfServiceChecks.Run(Check);
ServiceDeskChecks.Run(Check);
SecurityHardeningChecks.Run(Check);
DataIntegrityChecks.Run(Check);
RoleAdministrationChecks.Run(Check);

if (failures.Count > 0)
{
    Console.Error.WriteLine("Priority-9 architecture/sample checks failed:");
    foreach (var failure in failures) Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine("All priority-10 portal/mobile, reporting, scope, export, architecture and cumulative behavior checks passed.");
return 0;

void CheckDependency(System.Reflection.Assembly assembly, string name, string[] forbidden)
{
    var references = assembly.GetReferencedAssemblies().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var dependency in forbidden)
        Check(!references.Contains(dependency), $"{name} must not reference {dependency}.");
}

void Check(bool condition, string message)
{
    if (!condition) failures.Add(message);
}
