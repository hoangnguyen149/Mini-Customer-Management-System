namespace CustomerManager.Application.Interfaces;

/// <summary>Abstraction over "who is making this request", read by the
/// SaveChanges interceptor to populate CreatedBy/UpdatedBy without Infrastructure
/// reaching into HttpContext directly from entity-tracking code.</summary>
public interface ICurrentUserService
{
    string? Username { get; }
}
