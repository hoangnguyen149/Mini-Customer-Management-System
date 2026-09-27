using CustomerManager.Contracts.Auth;

namespace CustomerManager.Application.Common;

public enum LoginResult
{
    Success,
    InvalidCredentials,
    LockedOut
}

/// <summary>Distinguishes "locked out" from "wrong credentials" so the UI can
/// show a specific, helpful message. The lockout applies to the caller's own
/// (username, client IP) pair — see ILoginAttemptTracker — so reporting it only
/// ever tells a caller about their own IP, never about the account.</summary>
public record LoginOutcome(LoginResult Result, LoginResponse? Response, DateTime? LockedOutUntil)
{
    public static LoginOutcome Success(LoginResponse response) => new(LoginResult.Success, response, null);
    public static LoginOutcome InvalidCredentials() => new(LoginResult.InvalidCredentials, null, null);
    public static LoginOutcome LockedOut(DateTime lockedOutUntil) => new(LoginResult.LockedOut, null, lockedOutUntil);
}
