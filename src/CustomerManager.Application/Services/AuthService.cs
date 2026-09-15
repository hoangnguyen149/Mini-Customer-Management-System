using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Auth;
using Microsoft.EntityFrameworkCore;

namespace CustomerManager.Application.Services;

public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthService(
        IApplicationDbContext context,
        IPasswordHasherService passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username, ct);

        // Same "invalid credentials" outcome whether the username does not exist
        // or the password is wrong — never let a caller distinguish the two
        // (username enumeration is a real risk with a single, well-known admin
        // account).
        if (user is null || !_passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            return null;
        }

        var (token, expiresAtUtc) = _jwtTokenGenerator.GenerateToken(user);
        return new LoginResponse
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            Username = user.Username
        };
    }
}
