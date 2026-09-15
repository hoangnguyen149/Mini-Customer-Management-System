using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomerManager.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasIndex(x => x.Username).IsUnique();
        builder.Property(x => x.Username).HasMaxLength(50).IsRequired();

        // Never a plaintext column, never returned in any DTO — see
        // PasswordHasherService (PBKDF2 via Microsoft.AspNetCore.Identity).
        builder.Property(x => x.PasswordHash).IsRequired();

        builder.Property(x => x.CreatedAt).IsRequired();

        builder.Property(x => x.FailedLoginAttempts).HasDefaultValue(0);
    }
}
