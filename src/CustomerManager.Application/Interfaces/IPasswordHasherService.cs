namespace CustomerManager.Application.Interfaces;

public interface IPasswordHasherService
{
    string Hash(string password);

    /// <summary>Constant-time verify; never compare hashes/plaintext with ==.</summary>
    bool Verify(string hashedPassword, string providedPassword);
}
