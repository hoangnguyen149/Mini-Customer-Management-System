using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerManager.Application.Interfaces;

/// <summary>
/// Thin abstraction over the EF Core DbContext. This intentionally replaces a
/// generic Repository/UnitOfWork pair: DbContext already *is* a Unit of Work and
/// DbSet&lt;T&gt; already *is* a Repository, so wrapping it again only produces a
/// leaky abstraction that hides useful EF Core features (IQueryable composition,
/// ExecuteUpdateAsync/ExecuteDeleteAsync). This interface exists purely so that
/// Application does not take a compile-time dependency on the concrete
/// AppDbContext type in Infrastructure (keeps Dependency Inversion intact) and so
/// that CustomerService/AuthService stay unit-testable against an EF Core
/// InMemory/SQLite-backed implementation instead of a hand-rolled mock.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<User> Users { get; }
    DbSet<AuditLog> AuditLogs { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
