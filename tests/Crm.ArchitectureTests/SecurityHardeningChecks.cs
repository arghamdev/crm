using Crm.Application.Abstractions;
using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

internal static class SecurityHardeningChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CheckLoginLockout(check);
        CheckFieldMasking(check);
    }

    private static void CheckFieldMasking(Action<bool, string> check)
    {
        check(FieldMasking.MaskIdentifier("10101234567") == "********567" && FieldMasking.MaskPhone("09120005505") == "0912*****05" &&
              FieldMasking.MaskEmail("INFO@SEPEHR.TEST") == "I***@SEPEHR.TEST" && FieldMasking.MaskPhone(null) is null,
            "MASK: identifier, phone and email masks keep only a recognisable fragment.");

        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var access = new DemoAccessSnapshotService(store, provider.GetRequiredService<IDistributedCache>());
        var crm = new CrmApplicationService(store, access, new InMemoryCustomerQueryStore(store));
        var customer360 = new Customer360Service(store, access);
        var quotes = new QuoteApplicationService(store, access, new Crm.Infrastructure.Commercial.DemoProductPriceCatalog());
        Guid User(int n) => Guid.Parse($"10000000-0000-4000-8000-{n:000000000000}");
        var org = new OrganizationSelection("C01", null, null);
        var sepehr = Guid.Parse("20000000-0000-4000-8000-000000000001");

        var manager = crm.GetCustomer(User(1), org, sepehr)!;
        check(manager.NationalId == "10101234567" && manager.PrimaryPhone == "02188776655" && !manager.FinancialMasked && manager.Balance > 0,
            "MASK: the sales manager sees identifiers, contact data and balances unmasked.");
        var executive = crm.GetCustomer(User(9), org, sepehr)!;
        check(executive.NationalId == "********567" && executive.PrimaryPhone == "0218*****55" && executive.PrimaryEmail == "I***@SEPEHR.TEST" &&
              !executive.FinancialMasked,
            "MASK: the executive sees balances but masked identifiers and contact data.");
        var expert = crm.GetCustomer(User(2), org with { BranchId = "B01" }, sepehr)!;
        check(expert.PrimaryPhone == "02188776655" && expert.NationalId == "********567" && expert.FinancialMasked && expert.Balance == 0 && expert.CreditLimit == 0,
            "MASK: the sales expert can call the customer but sees neither national ID nor balances.");
        var executive360 = customer360.Get(User(9), org, sepehr)!;
        check(executive360.Customer.NationalId == "********567" && executive360.Contacts.All(x => x.Phone is null || x.Phone.Contains('*')),
            "MASK: Customer 360 masks the customer and its contacts for the executive.");
        check(crm.SearchCustomers(User(9), org).Items.All(x => x.NationalId is null || x.NationalId.StartsWith('*')),
            "MASK: the paged customer list masks every row.");

        var expertQuotes = quotes.GetWorkspace(User(2), org with { BranchId = "B01" }, DateTimeOffset.UtcNow).Items;
        var managerQuotes = quotes.GetWorkspace(User(1), org, DateTimeOffset.UtcNow).Items;
        check(expertQuotes.Count > 0 && expertQuotes.All(x => x.MarginMasked && x.MarginPercent == 0) &&
              managerQuotes.Any(x => !x.MarginMasked && x.MarginPercent != 0),
            "MASK: quote margin is hidden from the sales expert and visible to the manager.");
        var expertQuote = quotes.Get(User(2), org with { BranchId = "B01" }, expertQuotes[0].Id, DateTimeOffset.UtcNow)!;
        check(expertQuote.Quote.MarginMasked && expertQuote.Lines.All(x => x.MarginMasked && x.MarginPercent == 0),
            "MASK: quote details and every line hide the margin from the expert.");

        // A manager stripped of identifier access still holds Customer.Update, so the edit guard itself is exercised.
        var full = access.Get(User(1))!;
        var withoutIds = full with
        {
            CompanyPermissionSets = full.CompanyPermissionSets.Select(x => x with
            {
                Permissions = x.Permissions.Where(p => p != FieldMasking.NationalIdPermission).ToHashSet(StringComparer.OrdinalIgnoreCase)
            }).ToList()
        };
        var restricted = new Customer360Service(store, new FixedAccess(withoutIds));
        check(restricted.Get(User(1), org, sepehr)!.Customer.NationalId == "********567", "MASK: removing the permission masks the identifier.");
        try { restricted.GetEdit(User(1), org, sepehr); check(false, "MASK: an editor without identifier access must not get the raw edit form."); }
        catch (UnauthorizedAccessException) { }
        try
        {
            restricted.Update(User(1), org, sepehr, new UpdateCustomerCommand("x", "x", "x", "B01", null, "x",
                "********567", null, null, "x", manager.Version), DateTimeOffset.UtcNow);
            check(false, "MASK: an editor without identifier access must not be able to overwrite identifiers.");
        }
        catch (UnauthorizedAccessException) { }
        check(store.Read(d => d.Customers.Single(x => x.Id == sepehr).NationalId) == "10101234567", "MASK: the stored identifier is unchanged.");
    }

    private sealed class FixedAccess(AccessSnapshot snapshot) : IAccessSnapshotService
    {
        public AccessSnapshot? Get(Guid userId) => snapshot.UserId == userId ? snapshot : null;
        public bool HasPermission(Guid userId, string companyId, string permission) => Get(userId)?.PermissionsFor(companyId).Contains(permission) == true;
        public void Invalidate(Guid userId) { }
    }

    private static void CheckLoginLockout(Action<bool, string> check)
    {
        using var store = new InMemoryCrmDataStore();
        using var provider = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var guard = new DistributedLoginAttemptGuard(cache, new LoginLockoutOptions(5, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15)));
        var identity = new IdentityApplicationService(store, new DemoAccessSnapshotService(store, cache),
            new IdentityRuntimeOptions("Demo@1405", TimeSpan.FromMinutes(30), TimeSpan.FromHours(8), TimeSpan.FromMinutes(1), 3), guard);
        var now = DateTimeOffset.UtcNow;
        IdentityRequestContext At(DateTimeOffset time) => new(time, "ip-hash", "tests", Guid.NewGuid().ToString("N"));

        SignInResult last = null!;
        for (var attempt = 1; attempt <= 4; attempt++) last = identity.AuthenticateDemo("sales.expert", "wrong", At(now));
        check(last.FailureReason == SignInFailureReason.InvalidCredentials, "LOCKOUT: failures below the threshold report invalid credentials.");
        last = identity.AuthenticateDemo("SALES.EXPERT ", "wrong", At(now));
        check(last.FailureReason == SignInFailureReason.LockedOut, "LOCKOUT: the fifth failure locks the account (case/space-insensitive).");
        check(identity.AuthenticateDemo("sales.expert", "Demo@1405", At(now.AddMinutes(5))).FailureReason == SignInFailureReason.LockedOut,
            "LOCKOUT: the correct password is refused while locked.");
        check(identity.AuthenticateDemo("sales.manager", "Demo@1405", At(now.AddMinutes(5))).Succeeded,
            "LOCKOUT: other accounts are unaffected.");
        check(identity.AuthenticateDemo("sales.expert", "Demo@1405", At(now.AddMinutes(16))).Succeeded,
            "LOCKOUT: the lock expires after the lockout duration.");

        for (var attempt = 1; attempt <= 4; attempt++) identity.AuthenticateDemo("sales.expert", "wrong", At(now.AddMinutes(17)));
        identity.AuthenticateDemo("sales.expert", "Demo@1405", At(now.AddMinutes(17)));
        check(identity.AuthenticateDemo("sales.expert", "wrong", At(now.AddMinutes(17))).FailureReason == SignInFailureReason.InvalidCredentials,
            "LOCKOUT: a successful sign-in resets the failure counter.");

        for (var attempt = 1; attempt <= 5; attempt++) last = identity.AuthenticateDemo("no.such.user", "wrong", At(now));
        check(last.FailureReason == SignInFailureReason.LockedOut, "LOCKOUT: unknown usernames lock identically, so lockout does not reveal account existence.");
        check(store.Read(d => d.SecurityAuditEvents.Any(x => x.Reason.StartsWith("LockedOut", StringComparison.Ordinal))),
            "LOCKOUT: lockouts are written to the security audit log.");
    }
}
