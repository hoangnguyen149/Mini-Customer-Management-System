using CustomerManager.Application.Common.Options;
using CustomerManager.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CustomerManager.Infrastructure.Authentication;

/// <summary>
/// In-memory ILoginAttemptTracker keyed by (username, client IP). Suitable for
/// a single API instance — the same constraint the import session cache already
/// documents; behind a load balancer this would move to IDistributedCache/Redis.
///
/// Each failure window lasts LockoutDuration from the first failure; once a
/// lockout has expired the counter starts again from zero (the previous
/// DB-based implementation never reset it, so every single failure after the
/// first lockout re-locked immediately).
/// </summary>
public sealed class MemoryLoginAttemptTracker : ILoginAttemptTracker
{
    private readonly IMemoryCache _cache;
    private readonly SecurityOptions _options;

    public MemoryLoginAttemptTracker(IMemoryCache cache, IOptions<SecurityOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public DateTime? GetLockedOutUntil(string username, string clientIp, DateTime nowUtc)
    {
        var state = _cache.Get<AttemptState>(Key(username, clientIp));
        if (state is null)
        {
            return null;
        }

        lock (state)
        {
            return state.LockedOutUntil > nowUtc ? state.LockedOutUntil : null;
        }
    }

    public DateTime? RecordFailure(string username, string clientIp, DateTime nowUtc)
    {
        var key = Key(username, clientIp);
        var state = _cache.GetOrCreate(key, entry =>
        {
            // Sliding: an attacker that keeps failing keeps their own entry alive.
            entry.SlidingExpiration = _options.LockoutDuration + TimeSpan.FromMinutes(1);
            return new AttemptState();
        })!;

        lock (state)
        {
            if (state.LockedOutUntil is not null && state.LockedOutUntil <= nowUtc)
            {
                // Previous lockout is over — start a fresh window.
                state.Failures = 0;
                state.LockedOutUntil = null;
            }

            state.Failures++;
            if (state.Failures >= _options.MaxFailedLoginAttempts)
            {
                state.LockedOutUntil = nowUtc.Add(_options.LockoutDuration);
                return state.LockedOutUntil;
            }

            return null;
        }
    }

    public void Reset(string username, string clientIp) => _cache.Remove(Key(username, clientIp));

    private static string Key(string username, string clientIp) =>
        $"login-attempts:{username.Trim().ToLowerInvariant()}|{clientIp}";

    private sealed class AttemptState
    {
        public int Failures { get; set; }
        public DateTime? LockedOutUntil { get; set; }
    }
}
