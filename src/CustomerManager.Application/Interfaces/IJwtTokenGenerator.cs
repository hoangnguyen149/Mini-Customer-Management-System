using CustomerManager.Domain.Entities;

namespace CustomerManager.Application.Interfaces;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(User user);
}
