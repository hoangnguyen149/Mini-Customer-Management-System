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
        var nextValue = await _context.Database
            .SqlQueryRaw<int>("SELECT NEXT VALUE FOR dbo.CustomerCodeSequence AS Value")
            .SingleAsync(ct);

        return $"{Prefix}{nextValue:D4}";
    }
}
