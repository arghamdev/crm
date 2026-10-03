using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crm.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace Crm.Infrastructure.Identity;

public sealed record LoginLockoutOptions(int MaxFailures, TimeSpan FailureWindow, TimeSpan LockoutDuration);

/// <summary>
/// Stores failure counters in the distributed cache (Redis in multi-node deployments) keyed by a hash of the
/// normalized username, so usernames are not written to the cache in clear text. The read-modify-write is not
/// atomic; under heavy parallel guessing a few extra attempts may slip through before the lock is set, which is
/// acceptable next to the per-IP rate limiter.
/// </summary>
public sealed class DistributedLoginAttemptGuard(IDistributedCache cache, LoginLockoutOptions options) : ILoginAttemptGuard
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DateTimeOffset? LockedUntil(string userName, DateTimeOffset nowUtc)
    {
        var state = Load(userName);
        return state?.LockedUntilUtc is { } until && until > nowUtc ? until : null;
    }

    public DateTimeOffset? RecordFailure(string userName, DateTimeOffset nowUtc)
    {
        var state = Load(userName);
        if (state is null || nowUtc - state.WindowStartUtc > options.FailureWindow || state.LockedUntilUtc <= nowUtc)
            state = new AttemptState(0, nowUtc, null);
        var failures = state.Failures + 1;
        DateTimeOffset? lockedUntil = failures >= options.MaxFailures ? nowUtc.Add(options.LockoutDuration) : null;
        // After a lockout the counter starts over, so the next lock again needs MaxFailures attempts.
        var next = lockedUntil is null ? new AttemptState(failures, state.WindowStartUtc, null) : new AttemptState(0, nowUtc, lockedUntil);
        var expires = (lockedUntil ?? state.WindowStartUtc.Add(options.FailureWindow)) - nowUtc;
        cache.SetString(Key(userName), JsonSerializer.Serialize(next, JsonOptions), new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expires > TimeSpan.Zero ? expires : TimeSpan.FromSeconds(1)
        });
        return lockedUntil;
    }

    public void Reset(string userName) => cache.Remove(Key(userName));

    private AttemptState? Load(string userName)
    {
        var json = cache.GetString(Key(userName));
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AttemptState>(json, JsonOptions);
    }

    private static string Key(string userName) =>
        "crm:login-attempts:v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.Trim().ToUpperInvariant())));

    private sealed record AttemptState(int Failures, DateTimeOffset WindowStartUtc, DateTimeOffset? LockedUntilUtc);
}
