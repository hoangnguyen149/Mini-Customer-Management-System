using System.Data;
using CustomerManager.Application.Common;
using CustomerManager.Application.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CustomerManager.Infrastructure.Persistence;

/// <summary>
/// Formats "KH-{n:D6}" (see CustomerCodeFormat) around values of the
/// "dbo.CustomerCodeSequence" SQL Server sequence. NEXT VALUE FOR /
/// sp_sequence_get_range are atomic at the database level, so two concurrent
/// requests can never observe the same value.
/// </summary>
public class SqlSequenceCustomerCodeGenerator : ICustomerCodeGenerator
{
    private const string SequenceName = "dbo.CustomerCodeSequence";

    private readonly AppDbContext _context;

    public SqlSequenceCustomerCodeGenerator(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string> NextAsync(CancellationToken ct)
    {
        // ToListAsync (not SingleAsync): EF wraps SingleAsync/FirstAsync raw SQL
        // in a derived table, and SQL Server rejects NEXT VALUE FOR there.
        var values = await _context.Database
            .SqlQueryRaw<long>($"SELECT CAST(NEXT VALUE FOR {SequenceName} AS BIGINT) AS Value")
            .ToListAsync(ct);

        return CustomerCodeFormat.Format(values.Single());
    }

    public async Task<IReadOnlyList<string>> NextRangeAsync(int count, CancellationToken ct)
    {
        if (count <= 0)
        {
            return Array.Empty<string>();
        }

        // Reserves `count` consecutive values in one round trip (bulk import)
        // instead of one NEXT VALUE FOR per row.
        var firstValue = new SqlParameter("@first", SqlDbType.Variant) { Direction = ParameterDirection.Output };
        var rangeSize = new SqlParameter("@size", SqlDbType.BigInt) { Value = (long)count };

        await _context.Database.ExecuteSqlRawAsync(
            $"EXEC sys.sp_sequence_get_range @sequence_name = N'{SequenceName}', @range_size = @size, @range_first_value = @first OUTPUT",
            new object[] { rangeSize, firstValue },
            ct);

        var start = Convert.ToInt64(firstValue.Value);
        return Enumerable.Range(0, count)
            .Select(i => CustomerCodeFormat.Format(start + i))
            .ToList();
    }
}
