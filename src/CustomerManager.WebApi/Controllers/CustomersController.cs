using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Common;
using CustomerManager.Contracts.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerManager.WebApi.Controllers;

/// <summary>
/// Thin controller: call the Application service → shape the HTTP response.
/// Request validation runs in FluentValidationFilter before every action; no
/// business logic and no EF Core usage here.
/// </summary>
[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerListItemDto>>> GetPaged(
        [FromQuery] CustomerQueryParameters query, CancellationToken ct)
    {
        return Ok(await _customerService.GetPagedAsync(query, ct));
    }

    /// <summary>Dashboard KPIs (total / active / inactive) in one query.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<CustomerStatsDto>> GetStats(CancellationToken ct)
    {
        return Ok(await _customerService.GetStatsAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _customerService.GetByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/audit-logs")]
    public async Task<ActionResult<IReadOnlyList<AuditLogDto>>> GetAuditLogs(Guid id, CancellationToken ct)
    {
        return Ok(await _customerService.GetAuditLogsAsync(id, ct));
    }

    [HttpPost]
    public async Task<ActionResult<CustomerDetailDto>> Create([FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var result = await _customerService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerDetailDto>> Update(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        return Ok(await _customerService.UpdateAsync(id, request, ct));
    }

    /// <summary>Soft delete. An optional <c>If-Match: "&lt;base64 RowVersion&gt;"</c>
    /// header makes the delete fail with 409 if the customer changed after the
    /// caller read it.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        await _customerService.DeleteAsync(id, ParseIfMatch(ifMatch), ct);
        return NoContent();
    }

    /// <summary>Soft-deletes up to BulkDeleteRequest.MaxItems customers in one
    /// transaction (one audit entry per customer).</summary>
    [HttpPost("bulk-delete")]
    public async Task<ActionResult<BulkDeleteResponse>> BulkDelete([FromBody] BulkDeleteRequest request, CancellationToken ct)
    {
        return Ok(await _customerService.DeleteManyAsync(request.Ids, ct));
    }

    /// <summary>ETags are quoted and may carry a weak prefix: W/"abc=" → abc=.</summary>
    private static string? ParseIfMatch(string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch) || ifMatch.Trim() == "*")
        {
            return null;
        }

        var value = ifMatch.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return value.Trim('"');
    }
}
