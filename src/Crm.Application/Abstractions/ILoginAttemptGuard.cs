namespace Crm.Application.Abstractions;

/// <summary>
/// Per-username brute-force protection for local (password) sign-in. Complements the per-IP rate limiter,
/// which an attacker can evade by rotating addresses. Unknown usernames are tracked the same way so a
/// lockout response never reveals whether an account exists.
/// </summary>
public interface ILoginAttemptGuard
{
    /// <summary>Returns the lockout end time when the username is currently locked, otherwise null.</summary>
    DateTimeOffset? LockedUntil(string userName, DateTimeOffset nowUtc);

    /// <summary>Records a failed attempt and returns the lockout end time if this failure triggered a lockout.</summary>
    DateTimeOffset? RecordFailure(string userName, DateTimeOffset nowUtc);

    void Reset(string userName);
}
