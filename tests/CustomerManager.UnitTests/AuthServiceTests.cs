using CustomerManager.Application.Services;
using CustomerManager.Contracts.Auth;
using CustomerManager.Domain.Entities;
using CustomerManager.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace CustomerManager.UnitTests;

public class AuthServiceTests
{
    private static async Task<AuthService> CreateSutWithSeededAdminAsync(
        AppDbContext context, string username, string password)
    {
        var hasher = new FakePasswordHasherService();
        context.Users.Add(User.Create(username, hasher.Hash(password)));
        await context.SaveChangesAsync(CancellationToken.None);

        return new AuthService(context, hasher, new FakeJwtTokenGenerator());
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnToken_WhenCredentialsAreValid()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        var result = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "P@ssw0rd!" }, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Username.Should().Be("admin");
        result.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnNull_WhenPasswordIsInvalid()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        var result = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong-password" }, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnNull_WhenUsernameDoesNotExist()
    {
        // Same outcome as a wrong password (see AuthService.LoginAsync) — this
        // test exists specifically to guard against a future change that makes
        // the two cases distinguishable (username enumeration).
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        var result = await sut.LoginAsync(new LoginRequest { Username = "no-such-user", Password = "P@ssw0rd!" }, CancellationToken.None);

        result.Should().BeNull();
    }
}
