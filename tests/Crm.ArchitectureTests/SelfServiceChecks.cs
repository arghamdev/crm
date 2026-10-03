using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Domain.Channel;
using Crm.Domain.Identity;
using Crm.Domain.SelfService;
using Crm.Infrastructure.Commercial;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

internal static class SelfServiceChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var store=new InMemoryCrmDataStore();
        using var provider=new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access=new DemoAccessSnapshotService(store,provider.GetRequiredService<IDistributedCache>());
        var source=new DemoPortalReadSource(new DemoProductPriceCatalog());
        var service=new SelfServiceService(store,access,source);
        Guid User(int n)=>Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var manager=User(1);var expert=User(2);var channel=User(7);var dealerUser=User(8);
        var org=new OrganizationSelection("C01",null,null);var now=DateTimeOffset.UtcNow.AddSeconds(1);
        void Reject<T>(Action action,string name) where T:Exception { try {action();check(false,"P10: "+name+" must reject.");}catch(T){} }
        var portal=service.Portal(dealerUser,org,now);
        check(portal.CanSubmit && portal.Products.Count>0 && portal.Customers.Count==2 && portal.Balance.HasValue,"P10: explicit dealer projection includes authorized customers and finance.");
        check(!typeof(PortalProductDto).GetProperties().Any(x=>x.Name.Contains("Cost")||x.Name.Contains("Margin")),"P10: catalog contract must omit internal cost/margin.");
        check(source.Products("C02","P-D01",now).Count==0&&source.Invoices("C01","other",now).Count==0,"P10: source fallback cannot leak dealer financial data.");
        Reject<UnauthorizedAccessException>(()=>service.Portal(manager,org,now),"company role is not dealer");
        Reject<UnauthorizedAccessException>(()=>service.Portal(dealerUser,org with {CompanyId="C02"},now),"foreign company");
        Reject<UnauthorizedAccessException>(()=>service.Portal(dealerUser,org with {BranchId="B01"},now),"context intersection");
        var forged=access.Get(dealerUser)!;
        var broad=forged with {PermissionScopeGrants=forged.PermissionScopeGrants.Select(x=>x with {ScopeType="Company",ScopeId="C01"}).ToList()};
        Reject<UnauthorizedAccessException>(()=>new SelfServiceService(store,new Fixed(broad),source).Portal(dealerUser,org,now),"explicit dealer grant required");
        var noFinance=forged with {PermissionScopeGrants=forged.PermissionScopeGrants.Where(x=>x.Permission!="Dealer.Financial.Read").ToList()};
        var masked=new SelfServiceService(store,new Fixed(noFinance),source).Portal(dealerUser,org,now);
        check(masked.Balance is null&&masked.Invoices.Count==0,"P10: finance requires its own exact dealer permission.");
        var command=new SubmitPortalRequestCommand(Guid.NewGuid(),PortalRequestKind.Order,"سفارش آزمون","شرح سفارش",portal.Customers[0].Id,portal.Products[0].Code,2);
        var ordersBefore=store.Read(d=>d.OrderRequests.Count);
        Reject<ArgumentException>(()=>service.Submit(dealerUser,org,command with {OperationId=Guid.NewGuid(),Quantity=0.0001m},now),"quantity cannot round to zero in SQL");
        var request=service.Submit(dealerUser,org,command,now);
        check(request.Quantity==2&&request.UnitPrice==portal.Products[0].UnitPrice&&store.Read(d=>d.OrderRequests.Count)==ordersBefore,"P10: submission derives price and cannot create final order.");
        check(service.Submit(dealerUser,org,command,now).Id==request.Id,"P10: identical operation replays without duplicate.");
        Reject<SelfServiceConflictException>(()=>service.Submit(dealerUser,org,command with {Quantity=3},now),"changed payload same operation");
        Reject<UnauthorizedAccessException>(()=>service.Submit(dealerUser,org,command with {OperationId=Guid.NewGuid(),CustomerId=Guid.Parse("20000000-0000-4000-8000-000000000001")},now),"foreign dealer customer");
        Reject<ArgumentException>(()=>service.Review(manager,org,request.Id,new(request.Version,PortalRequestStatus.Accepted,"تأیید"),now),"order requires existing internal order");
        Reject<UnauthorizedAccessException>(()=>service.Review(manager,org,request.Id,new(request.Version,PortalRequestStatus.Accepted,"تأیید",store.Read(d=>d.OrderRequests[0].Id)),now),"link foreign customer order");
        var reviewing=service.Review(manager,org,request.Id,new(request.Version,PortalRequestStatus.InReview,"در حال بررسی"),now);
        Reject<SelfServiceConflictException>(()=>service.Cancel(dealerUser,org,request.Id,request.Version,now),"stale cancellation");
        service.Cancel(dealerUser,org,request.Id,reviewing.Version,now);
        check(service.Request(dealerUser,org,request.Id,now).Status==PortalRequestStatus.Cancelled,"P10: requester can cancel open request.");
        var leadCommand=new SubmitPortalRequestCommand(Guid.NewGuid(),PortalRequestKind.Lead,"سرنخ حفاظت‌شده","اطلاعات تماس و شرح");
        var leadRequest=service.Submit(dealerUser,org,leadCommand,now);
        Reject<UnauthorizedAccessException>(()=>service.Review(channel,org,leadRequest.Id,new(1,PortalRequestStatus.Accepted,"ثبت سرنخ"),now),"review permission cannot elevate lead create");
        var approved=service.Review(manager,org,leadRequest.Id,new(1,PortalRequestStatus.Accepted,"ثبت و حفاظت"),now);
        check(approved.LinkedRecordId.HasValue&&approved.ProtectedUntilUtc==now.AddDays(30)&&store.Read(d=>d.Leads.Any(x=>x.Id==approved.LinkedRecordId)),"P10: approved lead has one CRM record and bounded protection.");
        var duplicate=service.Submit(dealerUser,org,leadCommand with {OperationId=Guid.NewGuid()},now);
        var normalizedDuplicate=service.Submit(dealerUser,org,leadCommand with {OperationId=Guid.NewGuid(),Subject="سرنخ  حفاظت‌شده"},now);
        Reject<SelfServiceConflictException>(()=>service.Review(manager,org,normalizedDuplicate.Id,new(1,PortalRequestStatus.Accepted,"تکرار"),now),"normalized protection collision");
        Reject<SelfServiceConflictException>(()=>service.Review(manager,org,duplicate.Id,new(1,PortalRequestStatus.Accepted,"تکرار"),now),"protected lead collision");
        Reject<SelfServiceConflictException>(()=>service.Review(manager,org,approved.Id,new(approved.Version,PortalRequestStatus.Accepted,"تکرار"),now),"terminal review replay");
        var invitation=service.Submit(dealerUser,org,new(Guid.NewGuid(),PortalRequestKind.AccessInvite,"همکار نماینده","درخواست حساب",Email:"new.portal@example.test"),now);
        Reject<UnauthorizedAccessException>(()=>service.Review(channel,org,invitation.Id,new(1,PortalRequestStatus.Accepted,"دعوت"),now),"review cannot administer accounts");
        var invited=service.Review(manager,org,invitation.Id,new(1,PortalRequestStatus.Accepted,"منتظر فعال‌سازی"),now);
        check(store.Read(d=>d.Users.Single(x=>x.Id==invited.LinkedRecordId).Status)==UserStatus.PendingActivation,"P10: accepted invitation does not activate or email account.");
        var revoke=service.Submit(dealerUser,org,new(Guid.NewGuid(),PortalRequestKind.AccessRevoke,"لغو حساب","اتمام همکاری",TargetUserId:invited.LinkedRecordId),now.AddSeconds(1));
        service.Review(manager,org,revoke.Id,new(1,PortalRequestStatus.Accepted,"دسترسی لغو شد"),now.AddSeconds(1));
        check(store.Read(d=>d.UserRoleAssignments.Where(x=>x.CrmUserId==invited.LinkedRecordId).All(x=>x.Status==RoleAssignmentStatus.Revoked)),"P10: accepted revocation invalidates role.");
        check(service.Invoice(dealerUser,org,portal.Invoices[0].Id,now).Content.Length>0,"P10: authorized invoice export.");
        Reject<KeyNotFoundException>(()=>service.Invoice(dealerUser,org,"foreign",now),"invoice enumeration");
        Reject<UnauthorizedAccessException>(()=>service.Mobile(dealerUser,org,null,now),"dealer cannot access internal visits");
        var customers=service.Mobile(expert,org,null,now).Customers;
        check(customers.All(x=>x.BranchId=="B01"),"P10: mobile customer scope intersects CRM read scope.");
        var plan=new CreateVisitCommand(Guid.NewGuid(),customers[0].Id,now.AddHours(1).UtcDateTime,"بازدید آزمون");
        var visit=service.PlanVisit(expert,org,plan,now);
        check(service.PlanVisit(expert,org,plan,now).Replayed,"P10: plan operation replay.");
        Reject<SelfServiceConflictException>(()=>service.PlanVisit(expert,org,plan with {Purpose="changed"},now),"plan payload collision");
        Reject<KeyNotFoundException>(()=>service.VisitAction(manager,org,visit.VisitId,new(Guid.NewGuid(),1,VisitStatus.CheckedIn,""),now),"another owner visit");
        Reject<ArgumentException>(()=>service.VisitAction(expert,org,visit.VisitId,new(Guid.NewGuid(),1,VisitStatus.CheckedIn,"",now,false,10,20),now),"GPS without consent");
        Reject<ArgumentException>(()=>service.VisitAction(expert,org,visit.VisitId,new(Guid.NewGuid(),1,VisitStatus.CheckedIn,"",now,true,91,20),now),"invalid GPS coordinates");
        var checkin=new VisitActionCommand(Guid.NewGuid(),1,VisitStatus.CheckedIn,"شروع",now.AddMinutes(-2),true,35.1m,51.1m);
        var started=service.VisitAction(expert,org,visit.VisitId,checkin,now);
        check(started.AppliedVersion==2&&started.Visit.HasLocation&&service.VisitAction(expert,org,visit.VisitId,checkin,now).Replayed,"P10: offline checkin accepted once with consent.");
        Reject<SelfServiceConflictException>(()=>service.VisitAction(expert,org,visit.VisitId,checkin with {Outcome="changed"},now),"receipt fingerprint mismatch");
        Reject<SelfServiceConflictException>(()=>service.VisitAction(expert,org,visit.VisitId,new(Guid.NewGuid(),1,VisitStatus.Completed,"نتیجه",now),now),"stale visit version");
        Reject<SelfServiceConflictException>(()=>service.VisitAction(expert,org,visit.VisitId,new(Guid.NewGuid(),2,VisitStatus.Completed,"نتیجه",now.AddHours(-9)),now),"expired offline event");
        var completed=service.VisitAction(expert,org,visit.VisitId,new(Guid.NewGuid(),2,VisitStatus.Completed,"پیگیری انجام شد",now),now);
        check(completed.Visit.Status==VisitStatus.Completed&&completed.Visit.Version==3,"P10: sequential offline transitions preserve server version.");
        check(store.Read(d=>d.CustomerTimelineEvents.Count(x=>x.Source=="MobileCRM"&&x.SourceReference==visit.VisitId.ToString("N")))==1,"P10: completed visit joins Customer 360 timeline once.");
        Reject<InvalidOperationException>(()=>service.VisitAction(expert,org,visit.VisitId,new(Guid.NewGuid(),3,VisitStatus.Cancelled,"لغو",now),now),"terminal visit transition");
        check(!store.Read(d=>d.SecurityAuditEvents.Any(x=>x.Reason.Contains("35.1")||x.Reason.Contains("new.portal@"))),"P10: audit omits GPS and private payloads.");
        var merger=new Customer360Service(store,access);
        var candidate=merger.GetDuplicateReviewQueue(manager,org).First(x=>x.CustomerId==command.CustomerId);
        merger.ReviewDuplicate(manager,org,candidate.Id,new(Crm.Domain.Customers.DuplicateReviewStatus.Confirmed,"بررسی ادغام",candidate.Version),now);
        var mergeVisit=service.PlanVisit(manager,org,new(Guid.NewGuid(),command.CustomerId!.Value,now.UtcDateTime,"ادغام بازدید"),now);
        var preview=merger.GetMergePreview(manager,org,candidate.Id,candidate.PossibleDuplicateCustomerId);
        var merged=merger.Merge(manager,org,candidate.Id,new(preview.Survivor.Id,"انتقال روابط P10",preview.CandidateVersion,preview.SurvivorVersion,preview.MergedVersion),now);
        check(store.Read(d=>d.MobileVisits.Single(x=>x.Id==mergeVisit.VisitId).CustomerId)==preview.Survivor.Id && store.Read(d=>d.PortalRequests.Single(x=>x.Id==request.Id).CustomerId)==preview.Survivor.Id,"P10: customer merge transfers both new relationships.");
        Reject<SelfServiceConflictException>(()=>service.VisitAction(manager,org,mergeVisit.VisitId,new(Guid.NewGuid(),1,VisitStatus.CheckedIn,"",now),now),"merge invalidates old mobile version");
        merger.Unmerge(manager,org,merged.Id,new("بازگردانی P10",merged.Version),now);
        check(store.Read(d=>d.MobileVisits.Single(x=>x.Id==mergeVisit.VisitId).CustomerId)==command.CustomerId && store.Read(d=>d.PortalRequests.Single(x=>x.Id==request.Id).CustomerId)==command.CustomerId,"P10: unmerge restores portal/mobile references from manifest.");
        var internalOrder=store.Read(d=>d.OrderRequests.First(x=>x.CompanyId==org.CompanyId));
        store.Write(d=>{var c=d.Customers.Single(x=>x.Id==internalOrder.CustomerId);d.DealerCustomerAssignments.Add(new(Guid.NewGuid(),org.CompanyId,c.BranchId,c.TerritoryId,portal.DealerId,c.Id,now.AddMinutes(-1),manager,"P10 order link test"));return true;});
        var orderToLink=service.Submit(dealerUser,org,command with {OperationId=Guid.NewGuid(),CustomerId=internalOrder.CustomerId},now);
        var linkedOrder=service.Review(manager,org,orderToLink.Id,new(1,PortalRequestStatus.Accepted,"متصل به سفارش داخلی",internalOrder.Id),now);
        check(linkedOrder.OrderStatus==internalOrder.Status.ToString(),"P10: only the explicitly linked order status reaches the portal.");
        store.Write(d=>{d.Dealers.Single(x=>x.Id==portal.DealerId).Suspend("test");return true;});
        check(!service.Portal(dealerUser,org,now).CanSubmit,"P10: suspended dealer remains readable but cannot submit.");
        Reject<UnauthorizedAccessException>(()=>service.Submit(dealerUser,org,leadCommand with {OperationId=Guid.NewGuid()},now),"suspended dealer submission");
    }
    private sealed class Fixed(AccessSnapshot snapshot):IAccessSnapshotService {
        public AccessSnapshot? Get(Guid id)=>snapshot.UserId==id?snapshot:null;
        public bool HasPermission(Guid id,string company,string permission)=>Get(id)?.PermissionsFor(company).Contains(permission)==true;
        public void Invalidate(Guid id){}
    }
}
