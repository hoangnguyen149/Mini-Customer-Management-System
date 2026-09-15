using CustomerManager.Application.Interfaces;
using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CustomerManager.Infrastructure.Authentication;

/// <summary>
/// Seeds exactly one admin user on application startup, if (and only if) the
/// Users table is still empty. Deliberately NOT done via EF Core migration
/// HasData(): a migration-seeded hash would sit in the Git history forever and
/// could never be rotated without a new migration. This reads the bootstrap
/// username/password from configuration (User Secrets in dev, an environment
/// variable/secret store in prod) at runtime instead — see README.md "Setup".
/// There is intentionally no registration endpoint; this is the only way a User
/// row is ever created, which is what "hardcoded admin account" means here.
/// </summary>
public class AdminUserSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminUserSeeder> _logger;

    public AdminUserSeeder(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AdminUserSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();

        if (await context.Users.AnyAsync(cancellationToken))
        {
            return; // Already seeded — never overwrite an existing admin's password here.
        }

        var username = _configuration["AdminSeed:Username"];
        var password = _configuration["AdminSeed:Password"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "AdminSeed:Username / AdminSeed:Password are not configured — no admin user was seeded. " +
                "Set them via 'dotnet user-secrets' (see README.md) and restart the API to enable login.");
            return;
        }

        var passwordHash = passwordHasher.Hash(password);
        var admin = User.Create(username, passwordHash);
        context.Users.Add(admin);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded initial admin user '{Username}'.", username);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
