using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerManager.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        // Id stays the primary key but is deliberately NOT the clustered index:
        // a randomly-generated GUID as a clustered key causes page splits/
        // fragmentation as rows are inserted out of key order. CustomerCode
        // (server-generated, near-sequential) is clustered instead — see the
        // Phase 1 design doc, section "Technical Decisions", item 7.
        builder.HasKey(x => x.Id).IsClustered(false);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasIndex(x => x.CustomerCode).IsUnique().IsClustered();
        builder.Property(x => x.CustomerCode).HasMaxLength(20).IsRequired();

        builder.Property(x => x.FullName).HasMaxLength(100).IsRequired();

        // Filtered unique index: only rows where IsDeleted = 0 participate in the
        // uniqueness constraint, so an email is freed up once its customer is
        // soft-deleted. This matches the duplicate-email checks in CustomerService,
        // which go through the Global Query Filter and therefore already ignore
        // deleted rows — before this filter, that mismatch let a deleted row's
        // email silently block every future create/update on that email (Issue H2).
        builder.HasIndex(x => x.Email).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Property(x => x.Email).HasMaxLength(150).IsRequired();

        // Not unique: PhoneNumber is indexed for search performance only (see
        // Phase 1 design doc, section 18 — confirmed business rule).
        builder.HasIndex(x => x.PhoneNumber);
        builder.Property(x => x.PhoneNumber).HasMaxLength(15).IsRequired();

        builder.Property(x => x.DateOfBirth).IsRequired();

        // No HasDefaultValue: EF Core treats a bool's CLR default (false) as
        // "not set" and silently drops the column from the INSERT statement
        // whenever a default value is configured, letting the DB default (true)
        // win even when the caller explicitly asked for IsActive = false
        // (Issue H1). Customer.Create always assigns IsActive explicitly, so
        // ValueGeneratedNever() forces EF to always send the real value instead.
        builder.Property(x => x.IsActive).IsRequired().ValueGeneratedNever();

        builder.Property(x => x.IsDeleted).HasDefaultValue(false);
        // Filtered index: most queries go through the Global Query Filter
        // (IsDeleted = 0), so this keeps that lookup cheap without indexing rows
        // that are never queried in the common case.
        builder.HasIndex(x => x.IsDeleted).HasFilter("[IsDeleted] = 0");

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(50).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(50);

        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}
