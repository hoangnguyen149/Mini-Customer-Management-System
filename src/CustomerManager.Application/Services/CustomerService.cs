using CustomerManager.Application.Common;
using CustomerManager.Application.Common.Exceptions;
using CustomerManager.Application.Interfaces;
using CustomerManager.Application.Mappings;
using CustomerManager.Application.Validators;
using CustomerManager.Contracts.Common;
using CustomerManager.Contracts.Customers;
using CustomerManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CustomerManager.Application.Services;

public class CustomerService : ICustomerService
{
    private const string ConcurrencyConflictMessage = "Dữ liệu khách hàng đã được người khác cập nhật. Vui lòng tải lại trang.";

    private readonly IApplicationDbContext _context;
    private readonly ICustomerCodeGenerator _customerCodeGenerator;

    public CustomerService(IApplicationDbContext context, ICustomerCodeGenerator customerCodeGenerator)
    {
        _context = context;
        _customerCodeGenerator = customerCodeGenerator;
    }

    public async Task<PagedResult<CustomerListItemDto>> GetPagedAsync(CustomerQueryParameters query, CancellationToken ct)
    {
        // AsNoTracking + Select-projection: read-only, and the DB only returns
        // the columns CustomerListItemDto actually needs.
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

        // Default (createdAt desc) is served by IX_Customers_CreatedAt_Active.
        q = (query.SortBy?.Trim().ToLowerInvariant(), query.SortDirection?.Trim().ToLowerInvariant()) switch
        {
            ("fullname", "asc") => q.OrderBy(c => c.FullName),
            ("fullname", _) => q.OrderByDescending(c => c.FullName),
            ("customercode", "asc") => q.OrderBy(c => c.CustomerCode),
            ("customercode", _) => q.OrderByDescending(c => c.CustomerCode),
            (_, "asc") => q.OrderBy(c => c.CreatedAt),
            _ => q.OrderByDescending(c => c.CreatedAt)
        };

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

    public async Task<CustomerStatsDto> GetStatsAsync(CancellationToken ct)
    {
        // One GROUP BY round trip instead of three paged COUNT queries.
        var counts = await _context.Customers
            .AsNoTracking()
            .GroupBy(c => c.IsActive)
            .Select(g => new { IsActive = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var active = counts.FirstOrDefault(c => c.IsActive)?.Count ?? 0;
        var inactive = counts.FirstOrDefault(c => !c.IsActive)?.Count ?? 0;

        return new CustomerStatsDto { Total = active + inactive, Active = active, Inactive = inactive };
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
        if (await _context.Customers.AnyAsync(c => c.Email == normalizedEmail, ct))
        {
            throw EmailTaken(request.Email);
        }

        // CustomerCode comes from a DB sequence, so concurrent creates never
        // share a code. The remaining race — two requests with the same new
        // Email — is closed by the filtered unique index; only a violation of
        // *that* index is reported as a duplicate email. Anything else
        // (truncation, deadlock, a code collision after a bad sequence
        // restart…) is a real server error and must surface as 500 + log.
        var code = await _customerCodeGenerator.NextAsync(ct);
        var customer = Customer.Create(code, request.FullName, request.Email, request.PhoneNumber, request.DateOfBirth, request.IsActive);
        _context.Customers.Add(customer);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == DatabaseConstraintNames.CustomerEmailUnique)
        {
            throw EmailTaken(request.Email);
        }

        return customer.ToDetailDto();
    }

    public async Task<CustomerDetailDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Customer), id);

        // Format is already enforced by UpdateCustomerRequestValidator (400);
        // this guard only protects callers that bypass the validator.
        if (!RowVersionFormat.TryDecode(request.RowVersion, out var clientRowVersion, requireSqlServerLength: false))
        {
            throw new FluentValidation.ValidationException("RowVersion không hợp lệ.");
        }

        // Fail fast if the row changed since the client read it; the
        // DbUpdateConcurrencyException catch below covers the remaining window
        // between this check and SaveChanges.
        if (!customer.RowVersion.AsSpan().SequenceEqual(clientRowVersion))
        {
            throw new ConflictException(ConcurrencyConflictMessage);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _context.Customers.AnyAsync(c => c.Id != id && c.Email == normalizedEmail, ct))
        {
            throw EmailTaken(request.Email);
        }

        customer.UpdateDetails(request.FullName, request.Email, request.PhoneNumber, request.DateOfBirth, request.IsActive);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ConcurrencyConflictMessage);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == DatabaseConstraintNames.CustomerEmailUnique)
        {
            throw EmailTaken(request.Email);
        }

        return customer.ToDetailDto();
    }

    public async Task DeleteAsync(Guid id, string? expectedRowVersion, CancellationToken ct)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(nameof(Customer), id);

        // Optional optimistic-concurrency check (If-Match header): callers that
        // know which version they saw won't delete a row someone else has just
        // edited. Callers without a version (e.g. bulk delete) keep the
        // previous behavior.
        if (expectedRowVersion is not null)
        {
            if (!RowVersionFormat.TryDecode(expectedRowVersion, out var expected, requireSqlServerLength: false))
            {
                throw new FluentValidation.ValidationException("RowVersion không hợp lệ.");
            }

            if (!customer.RowVersion.AsSpan().SequenceEqual(expected))
            {
                throw new ConflictException(ConcurrencyConflictMessage);
            }
        }

        // Remove is intentional: SoftDeleteAndAuditInterceptor rewrites the
        // Deleted state into an update of IsDeleted only.
        _context.Customers.Remove(customer);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ConcurrencyConflictMessage);
        }
    }

    public async Task<BulkDeleteResponse> DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var distinctIds = ids.Distinct().ToList();

        var customers = await _context.Customers
            .Where(c => distinctIds.Contains(c.Id))
            .ToListAsync(ct);

        // One SaveChanges: every row is soft-deleted and audited in a single
        // transaction instead of N separate HTTP calls from the UI.
        _context.Customers.RemoveRange(customers);
        await _context.SaveChangesAsync(ct);

        var found = customers.Select(c => c.Id).ToHashSet();
        return new BulkDeleteResponse
        {
            Deleted = customers.Count,
            NotFound = distinctIds.Where(id => !found.Contains(id)).ToList()
        };
    }

    public async Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(Guid id, CancellationToken ct)
    {
        var entityId = id.ToString();
        return await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityName == nameof(Customer) && a.EntityId == entityId)
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new AuditLogDto
            {
                Action = a.Action,
                OldValues = a.OldValues,
                NewValues = a.NewValues,
                UserName = a.UserName,
                Timestamp = a.Timestamp
            })
            .ToListAsync(ct);
    }

    private static ConflictException EmailTaken(string email) =>
        new($"Email '{email}' đã được sử dụng bởi khách hàng khác.");
}
