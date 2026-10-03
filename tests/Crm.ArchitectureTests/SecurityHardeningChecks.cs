using Crm.Application.Contracts;
using Crm.Application.Services;
using Crm.Infrastructure.Data;
using Crm.Infrastructure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

internal static class SecurityHardeningChecks
{
    internal static void Run(Action<bool, string> check) => CheckLoginLockout(check);

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
