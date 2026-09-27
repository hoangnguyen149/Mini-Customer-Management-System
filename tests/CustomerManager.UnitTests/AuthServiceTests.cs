using CustomerManager.Application.Common;
using CustomerManager.Application.Common.Options;
using CustomerManager.Application.Services;
using CustomerManager.Contracts.Auth;
using CustomerManager.Domain.Entities;
using CustomerManager.Infrastructure.Authentication;
using CustomerManager.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CustomerManager.UnitTests;

public class AuthServiceTests
{
    private const string AttackerIp = "203.0.113.10";
    private const string AdminIp = "198.51.100.20";

    /// <summary>MaxFailedLoginAttempts=3, LockoutDurationMinutes=10 — small,
    /// deterministic values so lockout tests don't need many iterations.</summary>
    private static readonly SecurityOptions TestSecurityOptions = new() { MaxFailedLoginAttempts = 3, LockoutDurationMinutes = 10 };

    private static readonly JwtOptions TestJwtOptions = new() { RefreshTokenExpiryDays = 7 };

    private static async Task<AuthService> CreateSutWithSeededAdminAsync(
        AppDbContext context, string username, string password, ManualTimeProvider? clock = null, string? storedHash = null)
    {
        clock ??= new ManualTimeProvider();
        var hasher = new FakePasswordHasherService();
        context.Users.Add(User.Create(username, storedHash ?? hasher.Hash(password), clock.GetUtcNow().UtcDateTime));
        await context.SaveChangesAsync(CancellationToken.None);

        var tracker = new MemoryLoginAttemptTracker(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(TestSecurityOptions));

        return new AuthService(
            context,
            hasher,
            new FakeJwtTokenGenerator(),
            tracker,
            clock,
            Options.Create(TestJwtOptions),
            NullLogger<AuthService>.Instance);
    }

    private static Task<LoginOutcome> Login(AuthService sut, string password, string ip = AdminIp, string username = "admin") =>
        sut.LoginAsync(new LoginRequest { Username = username, Password = password }, ip, CancellationToken.None);

    [Fact]
    public async Task LoginAsync_ShouldReturnToken_WhenCredentialsAreValid()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        var outcome = await Login(sut, "P@ssw0rd!");

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

        var outcome = await Login(sut, "wrong-password");

        outcome.Result.Should().Be(LoginResult.InvalidCredentials);
        outcome.Response.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnInvalidCredentials_WhenUsernameDoesNotExist()
    {
        // Same outcome as a wrong password — guards against username enumeration.
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        var outcome = await Login(sut, "P@ssw0rd!", username: "no-such-user");

        outcome.Result.Should().Be(LoginResult.InvalidCredentials);
    }

    [Fact]
    public async Task LoginAsync_ShouldLockOut_AfterMaxFailedAttempts()
    {
        await using var context = TestDbContextFactory.Create();
        var clock = new ManualTimeProvider();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!", clock);

        await Login(sut, "wrong-password");
        await Login(sut, "wrong-password");
        var thirdAttempt = await Login(sut, "wrong-password");

        thirdAttempt.Result.Should().Be(LoginResult.LockedOut);
        thirdAttempt.LockedOutUntil.Should().Be(clock.GetUtcNow().UtcDateTime.AddMinutes(10));
    }

    [Fact]
    public async Task LoginAsync_ShouldRejectCorrectPassword_WhileLockedOut()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        for (var i = 0; i < 3; i++)
        {
            await Login(sut, "wrong-password");
        }

        var outcome = await Login(sut, "P@ssw0rd!");

        outcome.Result.Should().Be(LoginResult.LockedOut);
    }

    [Fact]
    public async Task LoginAsync_ShouldStartFreshCount_AfterLockoutExpires()
    {
        // Regression: the old implementation never reset the counter when a
        // lockout expired, so the very next wrong password re-locked for
        // another full period.
        await using var context = TestDbContextFactory.Create();
        var clock = new ManualTimeProvider();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!", clock);

        for (var i = 0; i < 3; i++)
        {
            await Login(sut, "wrong-password");
        }

        clock.Advance(TimeSpan.FromMinutes(11));

        var firstAfterExpiry = await Login(sut, "wrong-password");
        var secondAfterExpiry = await Login(sut, "wrong-password");

        firstAfterExpiry.Result.Should().Be(LoginResult.InvalidCredentials);
        secondAfterExpiry.Result.Should().Be(LoginResult.InvalidCredentials); // 2 of 3, still not locked
    }

    [Fact]
    public async Task LoginAsync_ShouldNotLockOutOtherIps_WhenOneIpIsLockedOut()
    {
        // The DoS fix: an attacker hammering the single admin account only
        // locks out their own IP — the real admin can still sign in.
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        for (var i = 0; i < 3; i++)
        {
            await Login(sut, "wrong-password", AttackerIp);
        }

        var attacker = await Login(sut, "P@ssw0rd!", AttackerIp);
        var admin = await Login(sut, "P@ssw0rd!", AdminIp);

        attacker.Result.Should().Be(LoginResult.LockedOut);
        admin.Result.Should().Be(LoginResult.Success);
    }

    [Fact]
    public async Task LoginAsync_ShouldResetFailedAttempts_OnSuccessfulLogin()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");

        await Login(sut, "wrong-password");
        await Login(sut, "P@ssw0rd!");

        var afterReset = await Login(sut, "wrong-password");
        var secondAfterReset = await Login(sut, "wrong-password");

        afterReset.Result.Should().Be(LoginResult.InvalidCredentials);
        secondAfterReset.Result.Should().Be(LoginResult.InvalidCredentials); // 2 of 3
    }

    [Fact]
    public async Task LoginAsync_ShouldUpgradeHash_WhenHasherReportsRehashNeeded()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!", storedHash: "legacy:P@ssw0rd!");

        var outcome = await Login(sut, "P@ssw0rd!");

        outcome.Result.Should().Be(LoginResult.Success);
        var user = await context.Users.SingleAsync();
        user.PasswordHash.Should().Be(new FakePasswordHasherService().Hash("P@ssw0rd!"));
    }

    [Fact]
    public async Task RefreshAsync_ShouldIssueNewTokens_ForActiveRefreshToken()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");
        var login = await Login(sut, "P@ssw0rd!");

        var refreshed = await sut.RefreshAsync(login.Response!.RefreshToken, CancellationToken.None);

        refreshed.Should().NotBeNull();
        refreshed!.Username.Should().Be("admin");
        refreshed.RefreshToken.Should().NotBe(login.Response.RefreshToken); // rotated
    }

    [Fact]
    public async Task RefreshAsync_ShouldFail_WhenTokenAlreadyUsedOnce()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");
        var login = await Login(sut, "P@ssw0rd!");

        await sut.RefreshAsync(login.Response!.RefreshToken, CancellationToken.None);
        var secondAttempt = await sut.RefreshAsync(login.Response.RefreshToken, CancellationToken.None);

        secondAttempt.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_ShouldRevokeWholeSession_WhenRotatedTokenIsReused()
    {
        // Reuse of an already-rotated token means a copy leaked. The newest
        // token in the chain (possibly held by the attacker) must stop working.
        await using var context = TestDbContextFactory.Create();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!");
        var login = await Login(sut, "P@ssw0rd!");
        var first = login.Response!.RefreshToken;

        var rotated = await sut.RefreshAsync(first, CancellationToken.None);
        await sut.RefreshAsync(first, CancellationToken.None); // replay → theft detected
        var withNewest = await sut.RefreshAsync(rotated!.RefreshToken, CancellationToken.None);

        withNewest.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_ShouldPurgeLongExpiredRefreshTokens()
    {
        await using var context = TestDbContextFactory.Create();
        var clock = new ManualTimeProvider();
        var sut = await CreateSutWithSeededAdminAsync(context, "admin", "P@ssw0rd!", clock);

        await Login(sut, "P@ssw0rd!");
        clock.Advance(TimeSpan.FromDays(9)); // 7-day expiry + 1-day retention + margin
        await Login(sut, "P@ssw0rd!");

        (await context.RefreshTokens.CountAsync()).Should().Be(1);
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
        var login = await Login(sut, "P@ssw0rd!");

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
