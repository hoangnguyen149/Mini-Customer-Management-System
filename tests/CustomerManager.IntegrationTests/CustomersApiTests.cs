using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerManager.Contracts.Customers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CustomerManager.IntegrationTests;

public class CustomersApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CustomersApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static CreateCustomerRequest NewCustomer(string? email = null) => new()
    {
        FullName = "Nguyễn Văn Tích Hợp",
        Email = email ?? $"it-{Guid.NewGuid():N}@example.com",
        PhoneNumber = "0901234567",
        DateOfBirth = new DateOnly(1990, 5, 20),
        IsActive = true
    };

    private static async Task<CustomerDetailDto> CreateAsync(HttpClient client, CreateCustomerRequest request)
    {
        var response = await client.PostAsJsonAsync("api/customers", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CustomerDetailDto>())!;
    }

    [Fact]
    public async Task Create_ShouldAssignSixDigitCodes_FromSqlSequence()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var first = await CreateAsync(client, NewCustomer());
        var second = await CreateAsync(client, NewCustomer());

        first.CustomerCode.Should().MatchRegex(@"^KH-\d{6}$");
        second.CustomerCode.Should().MatchRegex(@"^KH-\d{6}$");
        second.CustomerCode.Should().NotBe(first.CustomerCode);
    }

    [Fact]
    public async Task Create_ShouldSkipExistingCodes_AfterSequenceIsBehindTheData()
    {
        // Reproduces the pre-ReviewFixes situation: rows exist whose codes are
        // ahead of the sequence. Re-running the migration's restart logic must
        // move the sequence past them so the next create succeeds.
        await using (var db = _factory.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO dbo.Customers (Id, CustomerCode, FullName, Email, PhoneNumber, DateOfBirth, IsActive, IsDeleted, CreatedAt, CreatedBy)
                VALUES (NEWID(), N'KH-900000', N'Legacy', N'legacy-900000@example.com', N'0900000000', '1990-01-01', 1, 0, SYSUTCDATETIME(), N'seed');
                ALTER SEQUENCE dbo.CustomerCodeSequence RESTART WITH 900000;
                """);

            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @next BIGINT = ISNULL((
                    SELECT MAX(TRY_CAST(SUBSTRING(CustomerCode, 4, 20) AS BIGINT))
                    FROM dbo.Customers WHERE CustomerCode LIKE N'KH-%'), 0) + 1;
                DECLARE @sql NVARCHAR(200) =
                    N'ALTER SEQUENCE dbo.CustomerCodeSequence RESTART WITH ' + CAST(@next AS NVARCHAR(20));
                EXEC sp_executesql @sql;
                """);
        }

        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client, NewCustomer());

        created.CustomerCode.Should().Be("KH-900001");
    }

    [Fact]
    public async Task Create_ShouldReturn409_WhenEmailAlreadyUsed()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await CreateAsync(client, NewCustomer(email));

        var response = await client.PostAsJsonAsync("api/customers", NewCustomer(email));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(email);
    }

    [Fact]
    public async Task Create_ShouldAllowEmail_OfSoftDeletedCustomer()
    {
        // Filtered unique index ([IsDeleted] = 0) — only verifiable on SQL Server.
        var client = await _factory.CreateAuthenticatedClientAsync();
        var email = $"reuse-{Guid.NewGuid():N}@example.com";
        var original = await CreateAsync(client, NewCustomer(email));
        (await client.DeleteAsync($"api/customers/{original.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var again = await client.PostAsJsonAsync("api/customers", NewCustomer(email));

        again.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Update_ShouldReturn409_WhenRowVersionIsStale()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client, NewCustomer());

        UpdateCustomerRequest Update(string fullName, string rowVersion) => new()
        {
            FullName = fullName,
            Email = created.Email,
            PhoneNumber = created.PhoneNumber,
            DateOfBirth = created.DateOfBirth,
            IsActive = true,
            RowVersion = rowVersion
        };

        (await client.PutAsJsonAsync($"api/customers/{created.Id}", Update("Người sửa trước", created.RowVersion)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await client.PutAsJsonAsync($"api/customers/{created.Id}", Update("Người sửa sau", created.RowVersion));

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ShouldReturn400_WhenRowVersionIsMalformed()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client, NewCustomer());

        var response = await client.PutAsJsonAsync($"api/customers/{created.Id}", new UpdateCustomerRequest
        {
            FullName = created.FullName,
            Email = created.Email,
            PhoneNumber = created.PhoneNumber,
            DateOfBirth = created.DateOfBirth,
            IsActive = true,
            RowVersion = "not-base64!!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_ShouldAuditOnlyTheChangedColumns()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var created = await CreateAsync(client, NewCustomer());

        (await client.DeleteAsync($"api/customers/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var logs = await client.GetFromJsonAsync<List<AuditLogDto>>($"api/customers/{created.Id}/audit-logs");

        var deleted = logs!.Single(l => l.Action == "Deleted");
        var changed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(deleted.NewValues!)!;
        changed.Keys.Should().BeEquivalentTo("IsDeleted", "UpdatedAt", "UpdatedBy");
    }

    [Fact]
    public async Task Stats_ShouldMatchGroupedCounts()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        await CreateAsync(client, NewCustomer());

        var stats = await client.GetFromJsonAsync<CustomerStatsDto>("api/customers/stats");

        stats!.Total.Should().Be(stats.Active + stats.Inactive);
        stats.Total.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Customers_ShouldRequireAuthentication()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("api/customers");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
