using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerManager.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(20).IsRequired();
        builder.Property(x => x.UserName).HasMaxLength(50).IsRequired();

        // OldValues/NewValues are JSON blobs of arbitrary changed-property sets —
        // unbounded by design, so nvarchar(max) rather than a fixed length.
        builder.Property(x => x.OldValues).HasColumnType("nvarchar(max)");
        builder.Property(x => x.NewValues).HasColumnType("nvarchar(max)");

        // Every "history for this customer" query filters by EntityName + EntityId
        // and sorts by Timestamp — see CustomerService.GetAuditLogsAsync.
        builder.HasIndex(x => new { x.EntityName, x.EntityId, x.Timestamp });
    }
}
