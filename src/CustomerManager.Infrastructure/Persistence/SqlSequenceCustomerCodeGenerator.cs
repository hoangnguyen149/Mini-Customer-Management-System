using CustomerManager.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CustomerManager.Infrastructure.Persistence;

/// <summary>
/// Formats "KH-{n:D4}" around the next value of the "dbo.CustomerCodeSequence"
/// SQL Server sequence (see AppDbContext.OnModelCreating). NEXT VALUE FOR is
/// atomic at the database level — unlike reading MAX(CustomerCode) in
/// application code, two concurrent requests can never observe the same value.
/// </summary>
public class SqlSequenceCustomerCodeGenerator : ICustomerCodeGenerator
{
    private const string Prefix = "KH-";

    private readonly AppDbContext _context;

    public SqlSequenceCustomerCodeGenerator(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string> NextAsync(CancellationToken ct)
    {
        // SingleAsync()/FirstAsync() on a SqlQueryRaw result make EF Core wrap
        // the raw SQL in a derived-table subquery (to apply its own TOP(n)
        // cardinality check) — and SQL Server explicitly rejects "NEXT VALUE
        // FOR" inside a subquery/derived table ("not allowed in ... derived
        // tables"), so this only ever worked against unit tests (which fake
        // this generator entirely) and silently 500'd against a real SQL
        // Server. ToListAsync() executes the raw SQL as-is with no wrapper;
        // the single row is then read in-memory.
        var values = await _context.Database
            .SqlQueryRaw<int>("SELECT NEXT VALUE FOR dbo.CustomerCodeSequence AS Value")
            .ToListAsync(ct);

        return $"{Prefix}{values.Single():D4}";
    }
}
