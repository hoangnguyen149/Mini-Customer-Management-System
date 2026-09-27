using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerManager.Contracts.Auth;
using CustomerManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;

namespace CustomerManager.IntegrationTests;

/// <summary>
/// Boots the real WebApi (full middleware pipeline, real DI, real
/// interceptors) against a throwaway SQL Server container with every EF
/// migration applied — the only setup in which constraint names, filtered
/// unique indexes, ROWVERSION and the code sequence behave exactly as in
/// production.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "Integration-Test-P@ssw0rd!";

    private readonly MsSqlContainer _database = new MsSqlBuilder().Build();
    private string? _accessToken;

    public string ConnectionString => _database.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting (not ConfigureAppConfiguration): Program.cs reads Jwt:* before
        // Build(), and only host settings are visible at that point.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Jwt:Secret", "integration-tests-secret-key-at-least-32-chars-long");
        builder.UseSetting("AdminSeed:Username", AdminUsername);
        builder.UseSetting("AdminSeed:Password", AdminPassword);
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        // Migrate before the host starts: AdminUserSeeder (a hosted service)
        // queries the Users table on startup.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    /// <summary>Client with a valid Bearer token. Logs in once per factory —
    /// /api/auth/login is rate limited to 5 requests/minute.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();

        if (_accessToken is null)
        {
            var response = await client.PostAsJsonAsync("api/auth/login", new LoginRequest
            {
                Username = AdminUsername,
                Password = AdminPassword
            });
            response.EnsureSuccessStatusCode();
            _accessToken = (await response.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);
}
