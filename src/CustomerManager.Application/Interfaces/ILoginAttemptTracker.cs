namespace CustomerManager.Application.Interfaces;

/// <summary>
/// Tracks failed login attempts per (username, client IP) — not per account.
/// With a single well-known admin account, an account-wide lock would let anyone
/// keep the only admin locked out indefinitely; keying on the client IP means an
/// attacker can only ever lock out their own address. Combined with the per-IP
/// token-bucket rate limiter this still bounds brute-force attempts.
/// </summary>
public interface ILoginAttemptTracker
{
    /// <summary>Returns the lockout end time if this (username, IP) pair is
    /// currently locked out, otherwise null.</summary>
    DateTime? GetLockedOutUntil(string username, string clientIp, DateTime nowUtc);

    /// <summary>Records one failed attempt and returns the lockout end time if
    /// this attempt crossed the threshold, otherwise null.</summary>
    DateTime? RecordFailure(string username, string clientIp, DateTime nowUtc);

    void Reset(string username, string clientIp);
}
