using System.Reflection;
using CustomerManager.Application.Interfaces;
using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerManager.Infrastructure.Persistence;

public class AppDbContext : DbContext, IApplicationDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Applies CustomerConfiguration / UserConfiguration (Fluent API) from this
        // assembly instead of scattering configuration across the entities.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Global Query Filter: every default query excludes soft-deleted rows, so
        // application code can never accidentally forget "WHERE IsDeleted = 0".
        // Explicit reporting/restore scenarios can opt out with IgnoreQueryFilters().
        modelBuilder.Entity<Customer>().HasQueryFilter(c => !c.IsDeleted);

        // Backs SqlSequenceCustomerCodeGenerator. A DB sequence (not "read the max
        // existing CustomerCode and add one") is what actually guarantees
        // uniqueness under concurrent creates, and never runs out: formatting a
        // number past 9999 just widens to more digits instead of colliding with an
        // earlier code the way string-ordering comparison used to (Issue M2).
        modelBuilder.HasSequence<int>("CustomerCodeSequence", "dbo").StartsAt(1).IncrementsBy(1);

        base.OnModelCreating(modelBuilder);
    }
}
