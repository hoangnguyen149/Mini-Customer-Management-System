using CustomerManager.Application.Common.Exceptions;
using CustomerManager.Application.Interfaces;
using CustomerManager.Application.Mappings;
using CustomerManager.Contracts.Common;
using CustomerManager.Contracts.Customers;
using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerManager.Application.Services;

public class CustomerService : ICustomerService
{
    private readonly IApplicationDbContext _context;

    public CustomerService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<CustomerListItemDto>> GetPagedAsync(CustomerQueryParameters query, CancellationToken ct)
    {
        // AsNoTracking + Select-projection: this is a read-only query, never needs
        // change tracking, and the projection means the DB only returns the
        // columns CustomerListItemDto actually needs — not RowVersion/audit
        // fields the grid never renders.
        var q = _context.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.FullName))
        {
            q = q.Where(c => c.FullName.Contains(query.FullName));
        }

        if (!string.IsNullOrWhiteSpace(query.PhoneNumber))
        {
            q = q.Where(c => c.PhoneNumber.Contains(query.PhoneNumber));
        }

        if (query.IsActive.HasValue)
        {
            q = q.Where(c => c.IsActive == query.IsActive.Value);
        }

        q = (query.SortBy?.Trim().ToLowerInvariant(), query.SortDirection?.Trim().ToLowerInvariant()) switch
        {
            ("fullname", "asc") => q.OrderBy(c => c.FullName),
            ("fullname", _) => q.OrderByDescending(c => c.FullName),
            ("customercode", "asc") => q.OrderBy(c => c.CustomerCode),
            ("customercode", _) => q.OrderByDescending(c => c.CustomerCode),
            (_, "asc") => q.OrderBy(c => c.CreatedAt),
            _ => q.OrderByDescending(c => c.CreatedAt)
        };

        // Count + page are both executed at the database (IQueryable, deferred
        // execution) — never ToList()-then-filter-in-memory.
        var totalCount = await q.CountAsync(ct);

        var items = await q
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(CustomerMappingExtensions.ToListItemProjection)
            .ToListAsync(ct);

        return new PagedResult<CustomerListItemDto>
        {
            Items = items,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<CustomerDetailDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        return customer?.ToDetailDto();
    }

    public async Task<CustomerDetailDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailExists = await _context.Customers.AnyAsync(c => c.Email == normalizedEmail, ct);
        if (emailExists)
        {
            throw new ConflictException($"Email '{request.Email}' đã được sử dụng bởi khách hàng khác.");
        }

        // CustomerCode is generated server-side and is a unique business key.
        // Two concurrent creates could compute the same "next" code, so this
        // retries a couple of times on a unique-constraint violation rather than
        // trusting the pre-check alone (the DB unique index is the real guard).
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var code = await GenerateNextCustomerCodeAsync(ct);
            var customer = Customer.Create(code, request.FullName, request.Email, request.PhoneNumber, request.DateOfBirth, request.IsActive);
            _context.Customers.Add(customer);

            try
            {
                await _context.SaveChangesAsync(ct);
                return customer.ToDetailDto();
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                // Remove on an Added entity detaches it (EF Core behavior), so the
                // next loop iteration starts from a clean tracking state.
                _context.Customers.Remove(customer);
            }
        }

        throw new ConflictException("Không thể tạo mã khách hàng do xung đột dữ liệu, vui lòng thử lại.");
    }

    public async Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Customer), id);

        byte[] clientRowVersion;
        try
        {
            clientRowVersion = Convert.FromBase64String(request.RowVersion);
        }
        catch (FormatException)
        {
            throw new ConflictException("RowVersion không hợp lệ.");
        }

        // Explicit early check: the entity was just loaded fresh from the DB, so
        // if its RowVersion already differs from what the client last saw, someone
        // else has modified it in between — fail fast with a clear message instead
        // of waiting for SaveChanges to throw.
        if (!customer.RowVersion.AsSpan().SequenceEqual(clientRowVersion))
        {
            throw new ConflictException("Dữ liệu khách hàng đã được người khác cập nhật. Vui lòng tải lại trang.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailTakenByAnother = await _context.Customers
            .AnyAsync(c => c.Id != id && c.Email == normalizedEmail, ct);
        if (emailTakenByAnother)
        {
            throw new ConflictException($"Email '{request.Email}' đã được sử dụng bởi khách hàng khác.");
        }

        customer.UpdateDetails(request.FullName, request.Email, request.PhoneNumber, request.DateOfBirth, request.IsActive);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Safety net for the race between the check above and this SaveChanges
            // call — same conflict, same message, just caught at the DB level.
            throw new ConflictException("Dữ liệu khách hàng đã được người khác cập nhật. Vui lòng tải lại trang.");
        }

        return customer.ToDetailDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Customer), id);

        // Physically calling Remove here is intentional: the infrastructure-level
        // SaveChanges interceptor intercepts the Deleted entity state and rewrites
        // it into a Modified state that only sets IsDeleted = true. Application
        // code never sets IsDeleted directly.
        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync(ct);
    }

    private async Task<string> GenerateNextCustomerCodeAsync(CancellationToken ct)
    {
        const string prefix = "KH-";

        // IgnoreQueryFilters(): soft-deleted customers must still "reserve" their
        // code so a new customer never accidentally reuses a deleted one's code.
        var lastCode = await _context.Customers
            .IgnoreQueryFilters()
            .OrderByDescending(c => c.CustomerCode)
            .Select(c => c.CustomerCode)
            .FirstOrDefaultAsync(ct);

        var nextNumber = 1;
        if (lastCode is not null && lastCode.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(lastCode.AsSpan(prefix.Length), out var parsed))
        {
            nextNumber = parsed + 1;
        }

        return $"{prefix}{nextNumber:D4}";
    }
}
