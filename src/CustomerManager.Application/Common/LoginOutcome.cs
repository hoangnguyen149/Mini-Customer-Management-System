using CustomerManager.Contracts.Auth;

namespace CustomerManager.Application.Common;

public enum LoginResult
{
    Success,
    InvalidCredentials,
    LockedOut
}

/// <summary>Distinguishes "locked out" from "wrong credentials" so the UI can
/// show a specific, helpful message — safe to do here because this system has
/// exactly one, well-known admin account, so there's no username-enumeration
/// risk in telling a legitimate caller their own account is locked.</summary>
public record LoginOutcome(LoginResult Result, LoginResponse? Response, DateTime? LockedOutUntil)
{
    public static LoginOutcome Success(LoginResponse response) => new(LoginResult.Success, response, null);
    public static LoginOutcome InvalidCredentials() => new(LoginResult.InvalidCredentials, null, null);
    public static LoginOutcome LockedOut(DateTime lockedOutUntil) => new(LoginResult.LockedOut, null, lockedOutUntil);
}
