using System.Reflection;
using System.Text.RegularExpressions;
using CustomerManager.Application.Common.Exceptions;
using CustomerManager.Application.Interfaces;
using CustomerManager.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CustomerManager.Infrastructure.Persistence;

public partial class AppDbContext : DbContext, IApplicationDbContext
{
    // SQL Server: 2601 = duplicate key in a unique *index*,
    //             2627 = violation of a UNIQUE/PRIMARY KEY *constraint*.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Global Query Filter: every default query excludes soft-deleted rows.
        // Explicit reporting/restore scenarios can opt out with IgnoreQueryFilters().
        modelBuilder.Entity<Customer>().HasQueryFilter(c => !c.IsDeleted);

        // Backs SqlSequenceCustomerCodeGenerator — NEXT VALUE FOR is atomic, so
        // concurrent creates can never be handed the same code.
        modelBuilder.HasSequence<int>("CustomerCodeSequence", "dbo").StartsAt(1).IncrementsBy(1);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Translates SQL Server unique violations into the provider-agnostic
    /// UniqueConstraintViolationException (with the index/constraint name), so
    /// Application code can react to one specific constraint without
    /// referencing SqlClient. Every other DbUpdateException propagates
    /// unchanged and ends up as a logged 500.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException
            && ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sqlException)
        {
            throw new UniqueConstraintViolationException(ExtractConstraintName(sqlException.Message), ex);
        }
    }

    /// <summary>Both messages quote the name right after the keyword, e.g.
    /// "...with unique index 'IX_Customers_Email'..." (2601) or
    /// "Violation of UNIQUE KEY constraint 'UQ_x'..." (2627).</summary>
    internal static string? ExtractConstraintName(string message)
    {
        var match = ConstraintNameRegex().Match(message);
        return match.Success ? match.Groups["name"].Value : null;
    }

    [GeneratedRegex(@"(?:index|constraint) '(?<name>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex ConstraintNameRegex();
}
