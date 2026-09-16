using CustomerManager.Application.Interfaces;
using CustomerManager.Domain.Entities;
using CustomerManager.Infrastructure.Persistence;
using CustomerManager.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace CustomerManager.UnitTests;

internal class FakeCurrentUserService : ICurrentUserService
{
    public FakeCurrentUserService(string? username = "test-admin")
    {
        Username = username;
    }

    public string? Username { get; }
}

/// <summary>Deterministic stand-in for PasswordHasherService — AuthService tests
/// only need "same input round-trips", not real PBKDF2 behavior.</summary>
internal class FakePasswordHasherService : IPasswordHasherService
{
    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string hashedPassword, string providedPassword) => hashedPassword == Hash(providedPassword);
}

internal class FakeJwtTokenGenerator : IJwtTokenGenerator
{
    public (string Token, DateTime ExpiresAtUtc) GenerateToken(User user) =>
        ($"fake-jwt-for-{user.Username}", DateTime.UtcNow.AddHours(1));
}

/// <summary>In-memory stand-in for SqlSequenceCustomerCodeGenerator — hands out
/// "KH-0001", "KH-0002", ... in call order, without needing a real SQL Server
/// sequence (SqlQueryRaw isn't supported by the InMemory provider).</summary>
internal class FakeCustomerCodeGenerator : ICustomerCodeGenerator
{
    private int _next = 1;

    public Task<string> NextAsync(CancellationToken ct) => Task.FromResult($"KH-{_next++:D4}");
}

/// <summary>
/// Builds a real AppDbContext against the EF Core InMemory provider, with the
/// same SoftDeleteAndAuditInterceptor production uses wired in — this is what
/// replaces mocking a Repository (see Phase 1 design doc, Technical Decisions #1
/// and the .csproj comment above).
///
/// Known limitation: the InMemory provider does not reproduce SQL Server's
/// ROWVERSION auto-generation/unique-index-violation behavior exactly — that
/// scenario belongs in a SQL Server/SQLite-backed integration test
/// (CustomerManager.IntegrationTests, not built out in this pass). The
/// RowVersion-mismatch test below sets RowVersion directly via its internal
/// setter (see Customer.cs) instead of relying on provider-generated values,
/// which IS reliable across providers. CustomerCode generation is likewise not
/// provider-backed here — see FakeCustomerCodeGenerator, which replaces
/// SqlSequenceCustomerCodeGenerator (SqlQueryRaw isn't supported by InMemory).
/// </summary>
internal static class TestDbContextFactory
{
    public static AppDbContext Create(string? currentUsername = "test-admin")
    {
        var currentUserService = new FakeCurrentUserService(currentUsername);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(
                new SoftDeleteAndAuditInterceptor(currentUserService),
                new AuditLogInterceptor(currentUserService))
            .Options;

        return new AppDbContext(options);
    }
}
