using CustomerManager.Application.Common.Exceptions;
using CustomerManager.Application.Services;
using CustomerManager.Contracts.Customers;
using FluentAssertions;
using Xunit;

namespace CustomerManager.UnitTests;

public class CustomerServiceTests
{
    private static CreateCustomerRequest ValidCreateRequest(string email = "nguyen.van.a@example.com") => new()
    {
        FullName = "Nguyễn Văn A",
        Email = email,
        PhoneNumber = "0901234567",
        DateOfBirth = new DateOnly(1990, 1, 1),
        IsActive = true
    };

    [Fact]
    public async Task CreateAsync_ShouldReturnCustomerWithGeneratedCode_WhenRequestIsValid()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());

        var result = await sut.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        result.CustomerCode.Should().Be("KH-000001");
        result.FullName.Should().Be("Nguyễn Văn A");
        result.Email.Should().Be("nguyen.van.a@example.com");
        result.IsActive.Should().BeTrue();
        result.CreatedBy.Should().Be("test-admin"); // set by SoftDeleteAndAuditInterceptor, not by CustomerService
    }

    [Fact]
    public async Task CreateAsync_ShouldGenerateSequentialCustomerCode_ForSecondCustomer()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());

        await sut.CreateAsync(ValidCreateRequest("first@example.com"), CancellationToken.None);
        var second = await sut.CreateAsync(ValidCreateRequest("second@example.com"), CancellationToken.None);

        second.CustomerCode.Should().Be("KH-000002");
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowConflict_WhenEmailAlreadyExists()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        await sut.CreateAsync(ValidCreateRequest("duplicate@example.com"), CancellationToken.None);

        var act = () => sut.CreateAsync(ValidCreateRequest("duplicate@example.com"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnCustomer_WhenExists()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        var created = await sut.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        var result = await sut.GetByIdAsync(created.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNotExists()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());

        var result = await sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenSoftDeleted()
    {
        // Proves the Global Query Filter (HasQueryFilter in AppDbContext) is
        // actually wired up — not just that DeleteAsync ran without throwing.
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        var created = await sut.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        await sut.DeleteAsync(created.Id, null, CancellationToken.None);
        var result = await sut.GetByIdAsync(created.Id, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowNotFound_WhenCustomerDoesNotExist()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());

        var act = () => sut.DeleteAsync(Guid.NewGuid(), null, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateDetails_WhenRowVersionMatches()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        var created = await sut.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        var result = await sut.UpdateAsync(created.Id, new UpdateCustomerRequest
        {
            FullName = "Nguyễn Văn B",
            Email = created.Email,
            PhoneNumber = created.PhoneNumber,
            DateOfBirth = created.DateOfBirth,
            IsActive = false,
            RowVersion = created.RowVersion // exactly what CreateAsync returned — must match.
        }, CancellationToken.None);

        result.FullName.Should().Be("Nguyễn Văn B");
        result.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowConflict_WhenRowVersionIsStale()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        var created = await sut.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        // Deliberately wrong RowVersion — simulates another admin having saved a
        // change in between this client's read and this write, regardless of
        // what value the InMemory provider actually assigned RowVersion to.
        var staleRowVersion = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var act = () => sut.UpdateAsync(created.Id, new UpdateCustomerRequest
        {
            FullName = created.FullName,
            Email = created.Email,
            PhoneNumber = created.PhoneNumber,
            DateOfBirth = created.DateOfBirth,
            IsActive = created.IsActive,
            RowVersion = staleRowVersion
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task GetPagedAsync_ShouldReturnFilteredResult_WhenFullNameProvided()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        await sut.CreateAsync(ValidCreateRequest("a1@example.com"), CancellationToken.None); // Nguyễn Văn A
        await sut.CreateAsync(new CreateCustomerRequest
        {
            FullName = "Trần Thị B",
            Email = "b1@example.com",
            PhoneNumber = "0912345678",
            DateOfBirth = new DateOnly(1992, 2, 2),
            IsActive = true
        }, CancellationToken.None);

        var result = await sut.GetPagedAsync(new CustomerQueryParameters { FullName = "Trần" }, CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Items.Single().FullName.Should().Be("Trần Thị B");
    }

    [Fact]
    public async Task GetStatsAsync_ShouldCountActiveAndInactive_ExcludingDeleted()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        await sut.CreateAsync(ValidCreateRequest("s1@example.com"), CancellationToken.None);
        var inactive = ValidCreateRequest("s2@example.com");
        inactive.IsActive = false;
        await sut.CreateAsync(inactive, CancellationToken.None);
        var deleted = await sut.CreateAsync(ValidCreateRequest("s3@example.com"), CancellationToken.None);
        await sut.DeleteAsync(deleted.Id, null, CancellationToken.None);

        var stats = await sut.GetStatsAsync(CancellationToken.None);

        stats.Total.Should().Be(2);
        stats.Active.Should().Be(1);
        stats.Inactive.Should().Be(1);
    }

    [Fact]
    public async Task DeleteManyAsync_ShouldSoftDeleteFound_AndReportMissing()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        var a = await sut.CreateAsync(ValidCreateRequest("b1@example.com"), CancellationToken.None);
        var b = await sut.CreateAsync(ValidCreateRequest("b2@example.com"), CancellationToken.None);
        var missing = Guid.NewGuid();

        var result = await sut.DeleteManyAsync(new[] { a.Id, b.Id, missing }, CancellationToken.None);

        result.Deleted.Should().Be(2);
        result.NotFound.Should().Equal(missing);
        (await sut.GetByIdAsync(a.Id, CancellationToken.None)).Should().BeNull();
        context.AuditLogs.Count(l => l.Action == "Deleted").Should().Be(2);
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowConflict_WhenIfMatchRowVersionIsStale()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = new CustomerService(context, new FakeCustomerCodeGenerator());
        var created = await sut.CreateAsync(ValidCreateRequest(), CancellationToken.None);
        var stale = Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });

        var act = () => sut.DeleteAsync(created.Id, stale, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await sut.GetByIdAsync(created.Id, CancellationToken.None)).Should().NotBeNull();
    }
}
