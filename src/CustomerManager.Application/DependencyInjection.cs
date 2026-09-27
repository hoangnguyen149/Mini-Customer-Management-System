using System.Reflection;
using CustomerManager.Application.Interfaces;
using CustomerManager.Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CustomerManager.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Every "now" in Application goes through TimeProvider, so tests can
        // move time forward (lockout expiry, token expiry) deterministically.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICustomerImportService, CustomerImportService>();

        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}
