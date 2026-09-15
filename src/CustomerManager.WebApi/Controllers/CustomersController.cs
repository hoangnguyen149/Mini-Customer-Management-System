using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Common;
using CustomerManager.Contracts.Customers;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace CustomerManager.WebApi.Controllers;

/// <summary>
/// Thin controller: every action is Validate → call Application service → shape
/// the HTTP response. No business logic, no direct DbContext/EF Core usage here.
/// </summary>
[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;
    private readonly IValidator<CreateCustomerRequest> _createValidator;
    private readonly IValidator<UpdateCustomerRequest> _updateValidator;
    private readonly IOutputCacheStore _outputCacheStore;

    public CustomersController(
        ICustomerService customerService,
        IValidator<CreateCustomerRequest> createValidator,
        IValidator<UpdateCustomerRequest> updateValidator,
        IOutputCacheStore outputCacheStore)
    {
        _customerService = customerService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _outputCacheStore = outputCacheStore;
    }

    [HttpGet]
    [OutputCache(PolicyName = "CustomersListPolicy")]
    public async Task<ActionResult<PagedResult<CustomerListItemDto>>> GetPaged(
        [FromQuery] CustomerQueryParameters query, CancellationToken ct)
    {
        var result = await _customerService.GetPagedAsync(query, ct);
        return Ok(result);
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
        var result = await _customerService.GetAuditLogsAsync(id, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerDetailDto>> Create([FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);

        var result = await _customerService.CreateAsync(request, ct);

        // Must happen before returning 201 — otherwise the very next GET /customers
        // from the admin who just created this row could still serve the stale
        // cached page (see Program.cs ConfigureOutputCache).
        await _outputCacheStore.EvictByTagAsync("customers", ct);

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerDetailDto>> Update(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);

        var result = await _customerService.UpdateAsync(id, request, ct);
        await _outputCacheStore.EvictByTagAsync("customers", ct);

        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _customerService.DeleteAsync(id, ct);
        await _outputCacheStore.EvictByTagAsync("customers", ct);

        return NoContent();
    }
}
