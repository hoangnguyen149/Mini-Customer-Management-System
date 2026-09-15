using CustomerManager.Application.Common;
using CustomerManager.Application.Services;
using CustomerManager.Contracts.Auth;
using CustomerManager.Domain.Entities;
using CustomerManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CustomerManager.UnitTests;

public class AuthServiceTests
{
    /// <summary>MaxFailedLoginAttempts=3, LockoutDurationMinutes=10 — small,
    /// deterministic values so lockout tests don't need many iterations.</summary>
    private static IConfiguration TestConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Security:MaxFailedLoginAttempts"] = "3",
            ["Security:LockoutDurationMinutes"] = "10",
            ["Jwt:RefreshTokenExpiryDays"] = "7"
        })
        .Build();

    private static async Task<AuthService> CreateSutWithSeededAdminAsync(
        AppDbContext context, string username, string password)
    {
        var hasher = new FakePasswordHasherService();
        context.Users.Add(User.Create(username, hasher.Hash(password)));
        await context.SaveChangesAsync(CancellationToken.None);

        return new AuthService(
            context,
            hasher,
            new FakeJwtTokenGenerator(),
            TestConfiguration(),
            NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnToken_WhenCredentialsAreValid()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        var outcome = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "P@ssw0rd!" }, CancellationToken.None);

        outcome.Result.Should().Be(LoginResult.Success);
        outcome.Response.Should().NotBeNull();
        outcome.Response!.Username.Should().Be("admin");
        outcome.Response.Token.Should().NotBeNullOrWhiteSpace();
        outcome.Response.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnInvalidCredentials_WhenPasswordIsInvalid()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        var outcome = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong-password" }, CancellationToken.None);

        outcome.Result.Should().Be(LoginResult.InvalidCredentials);
        outcome.Response.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnInvalidCredentials_WhenUsernameDoesNotExist()
    {
        // Same outcome as a wrong password (see AuthService.LoginAsync) — this
        // test exists specifically to guard against a future change that makes
        // the two cases distinguishable (username enumeration).
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        var outcome = await sut.LoginAsync(new LoginRequest { Username = "no-such-user", Password = "P@ssw0rd!" }, CancellationToken.None);

        outcome.Result.Should().Be(LoginResult.InvalidCredentials);
    }

    [Fact]
    public async Task LoginAsync_ShouldLockAccount_AfterMaxFailedAttempts()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");
        var badRequest = new LoginRequest { Username = "admin", Password = "wrong-password" };

        // TestConfiguration sets MaxFailedLoginAttempts=3.
        await sut.LoginAsync(badRequest, CancellationToken.None);
        await sut.LoginAsync(badRequest, CancellationToken.None);
        var thirdAttempt = await sut.LoginAsync(badRequest, CancellationToken.None);

        thirdAttempt.Result.Should().Be(LoginResult.LockedOut);
        thirdAttempt.LockedOutUntil.Should().NotBeNull();
        thirdAttempt.LockedOutUntil!.Value.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task LoginAsync_ShouldRejectCorrectPassword_WhileAccountIsLockedOut()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");
        var badRequest = new LoginRequest { Username = "admin", Password = "wrong-password" };

        for (var i = 0; i < 3; i++)
        {
            await sut.LoginAsync(badRequest, CancellationToken.None);
        }

        // Correct password, but the account is now locked — must still fail.
        var outcome = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "P@ssw0rd!" }, CancellationToken.None);

        outcome.Result.Should().Be(LoginResult.LockedOut);
    }

    [Fact]
    public async Task LoginAsync_ShouldResetFailedAttempts_OnSuccessfulLogin()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong-password" }, CancellationToken.None);
        await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "P@ssw0rd!" }, CancellationToken.None);

        // One more wrong attempt after a successful login should be "attempt
        // 1 of 3" again, not "attempt 2" — proves the counter actually reset.
        var afterReset = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong-password" }, CancellationToken.None);
        var secondAfterReset = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong-password" }, CancellationToken.None);

        afterReset.Result.Should().Be(LoginResult.InvalidCredentials);
        secondAfterReset.Result.Should().Be(LoginResult.InvalidCredentials); // not locked out yet (2 of 3)
    }

    [Fact]
    public async Task RefreshAsync_ShouldIssueNewTokens_ForActiveRefreshToken()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");
        var login = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "P@ssw0rd!" }, CancellationToken.None);

        var refreshed = await sut.RefreshAsync(login.Response!.RefreshToken, CancellationToken.None);

        refreshed.Should().NotBeNull();
        refreshed!.Username.Should().Be("admin");
        refreshed.RefreshToken.Should().NotBe(login.Response.RefreshToken); // rotated
    }

    [Fact]
    public async Task RefreshAsync_ShouldFail_WhenTokenAlreadyUsedOnce()
    {
        // Rotation means the old refresh token is revoked the moment it's
        // exchanged — replaying it (e.g. after theft) must not work.
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");
        var login = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "P@ssw0rd!" }, CancellationToken.None);

        await sut.RefreshAsync(login.Response!.RefreshToken, CancellationToken.None);
        var secondAttempt = await sut.RefreshAsync(login.Response.RefreshToken, CancellationToken.None);

        secondAttempt.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ShouldFail_ForUnknownToken()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        var result = await sut.RefreshAsync("not-a-real-token", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LogoutAsync_ShouldRevokeToken_SoItCanNoLongerBeRefreshed()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");
        var login = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "P@ssw0rd!" }, CancellationToken.None);

        await sut.LogoutAsync(login.Response!.RefreshToken, CancellationToken.None);
        var afterLogout = await sut.RefreshAsync(login.Response.RefreshToken, CancellationToken.None);

        afterLogout.Should().BeNull();
    }

    [Fact]
    public async Task LogoutAsync_ShouldNotThrow_WhenTokenIsUnknownOrEmpty()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        var act = () => sut.LogoutAsync("not-a-real-token", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
