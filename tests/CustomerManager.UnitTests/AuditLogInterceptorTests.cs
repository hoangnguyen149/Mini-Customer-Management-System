using CustomerManager.Application.Services;
using CustomerManager.Contracts.Customers;
using FluentAssertions;
using Xunit;

namespace CustomerManager.UnitTests;

public class AuditLogInterceptorTests
{
    private static CreateCustomerRequest ValidCreateRequest(string email = "audit@example.com") => new()
    {
        FullName = "Nguyễn Văn Audit",
        Email = email,
        PhoneNumber = "0901234567",
        DateOfBirth = new DateOnly(1990, 1, 1),
        IsActive = true
    };

    [Fact]
    public async Task CreateAsync_ShouldWriteAddedAuditLog()
    {
        await using var context = TestDbContextFactory.Create("creator");
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());

        var created = await sut.CreateAsync(ValidCreateRequest(), CancellationToken.None);
        var logs = await sut.GetAuditLogsAsync(created.Id, CancellationToken.None);

        logs.Should().ContainSingle();
        logs[0].Action.Should().Be("Added");
        logs[0].UserName.Should().Be("creator");
        logs[0].OldValues.Should().BeNull();
        logs[0].NewValues.Should().Contain("Nguyễn Văn Audit");
    }

    [Fact]
    public async Task UpdateAsync_ShouldWriteModifiedAuditLog_WithOnlyChangedFields()
    {
        await using var context = TestDbContextFactory.Create("editor");
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        var created = await sut.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        await sut.UpdateAsync(created.Id, new UpdateCustomerRequest
        {
            FullName = "Nguyễn Văn Updated",
            Email = created.Email,
            PhoneNumber = created.PhoneNumber,
            DateOfBirth = created.DateOfBirth,
            IsActive = created.IsActive,
            RowVersion = created.RowVersion
        }, CancellationToken.None);

        var logs = await sut.GetAuditLogsAsync(created.Id, CancellationToken.None);

        logs.Should().HaveCount(2); // Added, then Modified
        var modified = logs.Single(l => l.Action == "Modified");
        modified.UserName.Should().Be("editor");
        modified.OldValues.Should().Contain("Nguyễn Văn Audit");
        modified.NewValues.Should().Contain("Nguyễn Văn Updated");
        // FullName didn't change on this field, so the diff must not mention it.
        modified.OldValues.Should().NotContain("PhoneNumber");
    }

    [Fact]
    public async Task DeleteAsync_ShouldWriteDeletedAuditLog_NotModified()
    {
        // The interceptor derives "Deleted" from the soft-delete flag rewrite
        // (this system never issues a physical DELETE) — this is the behavior
        // that would silently regress into a confusing "Modified" entry if
        // interceptor registration order (SoftDelete before AuditLog) broke.
        await using var context = TestDbContextFactory.Create("deleter");
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        var created = await sut.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        await sut.DeleteAsync(created.Id, CancellationToken.None);
        var logs = await sut.GetAuditLogsAsync(created.Id, CancellationToken.None);

        logs.Should().Contain(l => l.Action == "Deleted" && l.UserName == "deleter");
    }

    [Fact]
    public async Task GetAuditLogsAsync_ShouldReturnNewestFirst()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        var created = await sut.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        await sut.UpdateAsync(created.Id, new UpdateCustomerRequest
        {
            FullName = "Lần sửa 1",
            Email = created.Email,
            PhoneNumber = created.PhoneNumber,
            DateOfBirth = created.DateOfBirth,
            IsActive = created.IsActive,
            RowVersion = created.RowVersion
        }, CancellationToken.None);

        var logs = await sut.GetAuditLogsAsync(created.Id, CancellationToken.None);

        logs.Should().HaveCount(2);
        logs[0].Action.Should().Be("Modified"); // most recent change first
        logs[1].Action.Should().Be("Added");
    }
}
