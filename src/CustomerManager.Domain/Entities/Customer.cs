using CustomerManager.Domain.Common;

namespace CustomerManager.Domain.Entities;

/// <summary>
/// Core Customer entity. Uses private setters + factory/behavior methods so that
/// invariants (e.g. a customer is always created Active and not deleted) live in
/// the domain model instead of being scattered across service code.
/// </summary>
public class Customer : ISoftDeletable, IAuditable
{
    public Guid Id { get; private set; }

    /// <summary>Business key, e.g. "KH-0001". Server-generated, never user-entered.</summary>
    public string CustomerCode { get; private set; } = string.Empty;

    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public DateOnly DateOfBirth { get; private set; }
    public bool IsActive { get; private set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>EF Core concurrency token (SQL Server ROWVERSION). Setter is
    /// `internal` (not `private`) solely so CustomerManager.UnitTests can force a
    /// stale value to exercise the concurrency-conflict path — see
    /// AssemblyInfo.cs. EF Core itself writes this via reflection regardless of
    /// accessibility, and Application/WebApi code has no access to set it.</summary>
    public byte[] RowVersion { get; internal set; } = Array.Empty<byte>();

    // EF Core requires a parameterless constructor for materialization.
    private Customer()
    {
    }

    public static Customer Create(
        string customerCode,
        string fullName,
        string email,
        string phoneNumber,
        DateOnly dateOfBirth,
        bool isActive = true)
    {
        return new Customer
        {
            Id = Guid.NewGuid(),
            CustomerCode = customerCode,
            FullName = fullName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PhoneNumber = phoneNumber.Trim(),
            DateOfBirth = dateOfBirth,
            IsActive = isActive,
            IsDeleted = false
        };
    }

    public void UpdateDetails(
        string fullName,
        string email,
        string phoneNumber,
        DateOnly dateOfBirth,
        bool isActive)
    {
        FullName = fullName.Trim();
        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = phoneNumber.Trim();
        DateOfBirth = dateOfBirth;
        IsActive = isActive;
    }
}
