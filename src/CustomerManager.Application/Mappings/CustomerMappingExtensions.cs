using System.Linq.Expressions;
using CustomerManager.Contracts.Customers;
using CustomerManager.Domain.Entities;

namespace CustomerManager.Application.Mappings;

/// <summary>Manual mapping — deliberately no AutoMapper. One entity, a handful of
/// DTOs: a mapping library would add indirection/"magic" without saving enough
/// boilerplate to justify the dependency for a project this size.
///
/// <see cref="ToListItemProjection"/> is an Expression (not a plain method) on
/// purpose: EF Core can translate a property-to-property expression tree straight
/// into a SQL SELECT list inside <c>.Select(...)</c>, whereas a call to an
/// ordinary C# extension method inside a LINQ query cannot be translated and
/// would throw at runtime. <see cref="ToDetailDto"/> is a plain method because
/// it is only ever called on an already-materialized entity (after the query has
/// executed), where a normal in-memory method call — including the
/// byte[] → base64 RowVersion conversion, which EF Core's SQL Server provider
/// cannot translate to SQL — is safe.</summary>
public static class CustomerMappingExtensions
{
    public static readonly Expression<Func<Customer, CustomerListItemDto>> ToListItemProjection = c => new CustomerListItemDto
    {
        Id = c.Id,
        CustomerCode = c.CustomerCode,
        FullName = c.FullName,
        Email = c.Email,
        PhoneNumber = c.PhoneNumber,
        DateOfBirth = c.DateOfBirth,
        IsActive = c.IsActive
    };

    public static CustomerDetailDto ToDetailDto(this Customer c) => new()
    {
        Id = c.Id,
        CustomerCode = c.CustomerCode,
        FullName = c.FullName,
        Email = c.Email,
        PhoneNumber = c.PhoneNumber,
        DateOfBirth = c.DateOfBirth,
        IsActive = c.IsActive,
        CreatedAt = c.CreatedAt,
        CreatedBy = c.CreatedBy,
        UpdatedAt = c.UpdatedAt,
        UpdatedBy = c.UpdatedBy,
        RowVersion = Convert.ToBase64String(c.RowVersion)
    };
}
