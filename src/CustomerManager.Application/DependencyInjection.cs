using System.Reflection;
using CustomerManager.Application.Interfaces;
using CustomerManager.Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerManager.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICustomerImportService, CustomerImportService>();

        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}
