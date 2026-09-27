using CustomerManager.Application.Interfaces;
using CustomerManager.Infrastructure.Authentication;
using CustomerManager.Infrastructure.Persistence;
using CustomerManager.Infrastructure.Persistence.Imports;
using CustomerManager.Infrastructure.Persistence.Interceptors;
using CustomerManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Scoped (not Singleton): both interceptors depend on ICurrentUserService,
        // which reads the current HttpContext.User — they must be resolved from
        // the same DI scope as the request/DbContext, not once for the app lifetime.
        services.AddScoped<SoftDeleteAndAuditInterceptor>();
        services.AddScoped<AuditLogInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            options.UseSqlServer(connectionString);

            // Order matters: SoftDeleteAndAuditInterceptor must run first so it
            // rewrites Deleted -> Modified+IsDeleted=true before AuditLogInterceptor
            // inspects ChangeTracker state (see AuditLogInterceptor's XML doc).
            options.AddInterceptors(
                serviceProvider.GetRequiredService<SoftDeleteAndAuditInterceptor>(),
                serviceProvider.GetRequiredService<AuditLogInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ICustomerCodeGenerator, SqlSequenceCustomerCodeGenerator>();

        services.AddScoped<IPasswordHasherService, PasswordHasherService>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // Singleton: holds the per-(username, IP) failure counters for the
        // lifetime of the process (backed by IMemoryCache, registered below).
        services.AddSingleton<ILoginAttemptTracker, MemoryLoginAttemptTracker>();

        // Runs once at startup — see AdminUserSeeder for why this replaces both a
        // Users-management UI and a migration-based seed.
        services.AddHostedService<AdminUserSeeder>();

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        // Backs CustomerImportService's Preview->Confirm session cache — short
        // lived, never persisted, holds parsed rows only, never raw file bytes.
        services.AddMemoryCache();
        services.AddScoped<ICustomerImportFileParser, ClosedXmlCsvCustomerImportFileParser>();
        services.AddScoped<ICustomerImportWorkbookWriter, ClosedXmlCustomerImportWorkbookWriter>();

        return services;
    }
}
