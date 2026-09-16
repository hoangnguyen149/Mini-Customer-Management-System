using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CustomerManager.Infrastructure.Persistence;

/// <summary>Reports healthy only if the configured connection string can
/// actually open a connection — see Issue M7 (no health check existed, so a
/// misconfigured/unreachable DB was only ever discovered via a 500 on the
/// first real request).</summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _context;

    public DatabaseHealthCheck(AppDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await _context.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the configured database.");
    }
}
