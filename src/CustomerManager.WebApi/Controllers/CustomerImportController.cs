using CustomerManager.Application.Common.Exceptions;
using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Customers.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerManager.WebApi.Controllers;

/// <summary>
/// Bulk customer import (Excel/CSV) — kept separate from CustomersController
/// (single-customer CRUD) since the concerns barely overlap: multipart
/// uploads, file-format parsing, and a stateful Preview→Confirm handshake vs.
/// plain JSON CRUD. Same [Authorize] requirement as CustomersController —
/// importing customers is exactly as privileged as creating one by hand.
/// The uploaded file is never written to disk: Preview reads directly from
/// IFormFile.OpenReadStream() and only the parsed, validated result is
/// cached (see CustomerImportService) — Confirm never receives the file again.
/// </summary>
[ApiController]
[Route("api/customers/import")]
[Authorize]
public class CustomerImportController : ControllerBase
{
    private readonly ICustomerImportService _importService;

    public CustomerImportController(ICustomerImportService importService)
    {
        _importService = importService;
    }

    [HttpGet("template")]
    public IActionResult Template()
    {
        var bytes = _importService.BuildTemplate();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "customer-import-template.xlsx");
    }

    [HttpPost("preview")]
    public async Task<ActionResult<ImportPreviewResponse>> Preview(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            throw new ImportFileException("Vui lòng chọn một file để import.");
        }

        await using var stream = file.OpenReadStream();
        var result = await _importService.PreviewAsync(stream, file.FileName, file.Length, ct);
        return Ok(result);
    }

    [HttpPost("confirm")]
    public async Task<ActionResult<ImportConfirmResponse>> Confirm([FromBody] ImportConfirmRequest request, CancellationToken ct)
    {
        var result = await _importService.ConfirmAsync(request.ImportSessionId, ct);
        return Ok(result);
    }

    [HttpGet("{sessionId:guid}/error-report")]
    public async Task<IActionResult> ErrorReport(Guid sessionId, CancellationToken ct)
    {
        var bytes = await _importService.BuildErrorReportAsync(sessionId, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "import-errors.xlsx");
    }
}
