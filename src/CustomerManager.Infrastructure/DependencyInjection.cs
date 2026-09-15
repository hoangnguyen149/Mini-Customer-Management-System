using CustomerManager.Application.Interfaces;
using CustomerManager.Infrastructure.Authentication;
using CustomerManager.Infrastructure.Persistence;
using CustomerManager.Infrastructure.Persistence.Interceptors;
using CustomerManager.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerManager.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Scoped (not Singleton): the interceptor depends on ICurrentUserService,
        // which reads the current HttpContext.User — it must be resolved from the
        // same DI scope as the request/DbContext, not once for the app lifetime.
        services.AddScoped<SoftDeleteAndAuditInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

            options.UseSqlServer(connectionString);
            options.AddInterceptors(serviceProvider.GetRequiredService<SoftDeleteAndAuditInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IPasswordHasherService, PasswordHasherService>();
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        // Runs once at startup — see AdminUserSeeder for why this replaces both a
        // Users-management UI and a migration-based seed.
        services.AddHostedService<AdminUserSeeder>();

        return services;
    }
}
