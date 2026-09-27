namespace CustomerManager.Application.Interfaces;

public enum PasswordCheckResult
{
    Failed,
    Success,

    /// <summary>Correct password, but the stored hash uses outdated parameters
    /// (e.g. a lower PBKDF2 iteration count) and should be re-hashed.</summary>
    SuccessRehashNeeded
}

public interface IPasswordHasherService
{
    string Hash(string password);

    /// <summary>Constant-time verify; never compare hashes/plaintext with ==.</summary>
    PasswordCheckResult Verify(string hashedPassword, string providedPassword);
}
