using System.Globalization;
using CustomerManager.Application.Common.Exceptions;
using CustomerManager.Application.Imports;
using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Customers;
using CustomerManager.Contracts.Customers.Import;
using CustomerManager.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CustomerManager.Application.Services;

/// <summary>
/// Preview/Confirm split so the same file is only ever read once (Preview),
/// never re-sent to Confirm — the parsed result is cached server-side
/// (IMemoryCache, keyed by a GUID session id) with a short TTL, never
/// persisted, never the raw file bytes. Reuses the exact same
/// IValidator&lt;CreateCustomerRequest&gt; and ICustomerCodeGenerator the
/// single-customer Create path uses (see CustomerService.CreateAsync), so
/// business rules never drift between "add one" and "import many". Confirm
/// batches every insert into one AddRange + one SaveChangesAsync — the
/// SoftDeleteAndAuditInterceptor / AuditLogInterceptor pair already stamps
/// CreatedAt/CreatedBy and writes AuditLog rows for every entity in that
/// SaveChanges call regardless of whether it arrived via Add or AddRange, so
/// audit trail correctness doesn't depend on going through CreateAsync itself.
/// </summary>
public class CustomerImportService : ICustomerImportService
{
    private const long DefaultMaxFileSizeBytes = 5 * 1024 * 1024;
    private const int DefaultMaxRows = 10_000;
    private const int DefaultSessionExpiryMinutes = 15;

    private static readonly string[] SupportedExtensions = { ".xlsx", ".csv" };
    private static readonly string[] DateFormats = { "dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy" };

    private readonly IApplicationDbContext _context;
    private readonly ICustomerCodeGenerator _customerCodeGenerator;
    private readonly ICustomerImportFileParser _parser;
    private readonly ICustomerImportWorkbookWriter _workbookWriter;
    private readonly IValidator<CreateCustomerRequest> _validator;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;

    public CustomerImportService(
        IApplicationDbContext context,
        ICustomerCodeGenerator customerCodeGenerator,
        ICustomerImportFileParser parser,
        ICustomerImportWorkbookWriter workbookWriter,
        IValidator<CreateCustomerRequest> validator,
        IMemoryCache cache,
        IConfiguration configuration)
    {
        _context = context;
        _customerCodeGenerator = customerCodeGenerator;
        _parser = parser;
        _workbookWriter = workbookWriter;
        _validator = validator;
        _cache = cache;
        _configuration = configuration;
    }

    public byte[] BuildTemplate() => _workbookWriter.BuildTemplate();

    public async Task<ImportPreviewResponse> PreviewAsync(Stream fileContent, string fileName, long fileSizeBytes, CancellationToken ct)
    {
        if (fileSizeBytes <= 0)
        {
            throw new ImportFileException("File rỗng, vui lòng chọn file khác.");
        }

        var maxFileSizeBytes = _configuration.GetValue<long?>("Import:MaxFileSizeBytes") ?? DefaultMaxFileSizeBytes;
        if (fileSizeBytes > maxFileSizeBytes)
        {
            throw new ImportFileException($"File vượt quá kích thước tối đa {maxFileSizeBytes / 1024 / 1024} MB.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            throw new ImportFileException($"Định dạng file '{extension}' không được hỗ trợ. Chỉ chấp nhận .xlsx hoặc .csv.");
        }

        var maxRows = _configuration.GetValue<int?>("Import:MaxRows") ?? DefaultMaxRows;
        var rawRows = await _parser.ParseAsync(fileContent, fileName, maxRows, ct);

        if (rawRows.Count == 0)
        {
            throw new ImportFileException("File không chứa dữ liệu khách hàng nào.");
        }

        var (rowResults, candidates) = await ValidateRowsAsync(rawRows, ct);
        await MarkDatabaseDuplicatesAsync(rowResults, candidates, ct);

        var session = new ImportSession
        {
            AllRows = rowResults,
            ValidCandidates = candidates.Where(c => IsStillValid(rowResults, c.RowNumber)).ToList()
        };

        var sessionId = Guid.NewGuid();
        var expiryMinutes = _configuration.GetValue<int?>("Import:SessionExpiryMinutes") ?? DefaultSessionExpiryMinutes;
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);
        _cache.Set(CacheKey(sessionId), session, expiresAtUtc);

        return new ImportPreviewResponse
        {
            ImportSessionId = sessionId,
            ExpiresAtUtc = expiresAtUtc,
            TotalRows = rowResults.Count,
            ValidRows = rowResults.Count(r => r.Status == ImportRowStatus.Valid),
            InvalidRows = rowResults.Count(r => r.Status == ImportRowStatus.Invalid),
            DuplicateRows = rowResults.Count(r => r.Status is ImportRowStatus.DuplicateInFile or ImportRowStatus.DuplicateInDatabase),
            Rows = rowResults
        };
    }

    public async Task<ImportConfirmResponse> ConfirmAsync(Guid importSessionId, CancellationToken ct)
    {
        var session = GetSessionOrThrow(importSessionId);

        // State may have changed since Preview (another request took one of
        // these emails) — re-check before writing anything. All-or-nothing:
        // any new conflict aborts the whole import.
        var candidateEmails = session.ValidCandidates.Select(c => c.Email).ToList();
        var existingEmails = candidateEmails.Count == 0
            ? new List<string>()
            : await _context.Customers
                .Where(c => candidateEmails.Contains(c.Email))
                .Select(c => c.Email)
                .ToListAsync(ct);

        if (existingEmails.Count > 0)
        {
            var existingSet = new HashSet<string>(existingEmails);
            var conflictedRowNumbers = session.ValidCandidates
                .Where(c => existingSet.Contains(c.Email))
                .Select(c => c.RowNumber)
                .ToHashSet();

            foreach (var row in session.AllRows.Where(r => conflictedRowNumbers.Contains(r.RowNumber)))
            {
                row.Status = ImportRowStatus.DuplicateInDatabase;
                row.Errors.Add("Email đã được sử dụng bởi một khách hàng khác kể từ lúc xem trước (preview).");
            }

            throw new ImportValidationFailedException(
                "Dữ liệu đã thay đổi kể từ lúc xem trước, import bị huỷ — chưa có khách hàng nào được ghi. Vui lòng tải lại file và thử lại.",
                totalRows: session.AllRows.Count,
                validRows: session.AllRows.Count(r => r.Status == ImportRowStatus.Valid),
                invalidRows: session.AllRows.Count(r => r.Status != ImportRowStatus.Valid),
                errors: session.AllRows.Where(r => r.Status != ImportRowStatus.Valid).ToList());
        }

        var entities = new List<Customer>(session.ValidCandidates.Count);
        foreach (var candidate in session.ValidCandidates)
        {
            var code = await _customerCodeGenerator.NextAsync(ct);
            entities.Add(Customer.Create(code, candidate.FullName, candidate.Email, candidate.PhoneNumber, candidate.DateOfBirth, isActive: true));
        }

        _context.Customers.AddRange(entities);
        await _context.SaveChangesAsync(ct);

        _cache.Remove(CacheKey(importSessionId));

        return new ImportConfirmResponse
        {
            Success = true,
            Message = $"Đã import thành công {entities.Count} khách hàng.",
            TotalRows = session.AllRows.Count,
            ImportedRows = entities.Count,
            FailedRows = 0
        };
    }

    public Task<byte[]> BuildErrorReportAsync(Guid importSessionId, CancellationToken ct)
    {
        var session = GetSessionOrThrow(importSessionId);
        var errorRows = session.AllRows.Where(r => r.Status != ImportRowStatus.Valid).ToList();
        return Task.FromResult(_workbookWriter.BuildErrorReport(errorRows));
    }

    private async Task<(List<ImportRowResult> RowResults, List<ImportCandidate> Candidates)> ValidateRowsAsync(
        IReadOnlyList<ImportRawRow> rawRows, CancellationToken ct)
    {
        var rowResults = new List<ImportRowResult>(rawRows.Count);
        var candidates = new List<ImportCandidate>(rawRows.Count);

        // normalizedEmail -> first row number seen with that email, so a
        // second occurrence can point back to the first one in its error message.
        var seenEmailsInFile = new Dictionary<string, int>();

        foreach (var raw in rawRows)
        {
            var errors = new List<string>();
            var fullName = raw.FullName.Trim();
            var email = raw.Email.Trim();
            var normalizedEmail = email.ToLowerInvariant();
            var phoneNumber = raw.PhoneNumber.Trim();

            var dateParsed = TryParseDate(raw.DateOfBirthRaw, out var dateOfBirth);
            if (!dateParsed)
            {
                errors.Add("Ngày sinh không hợp lệ (định dạng dd/MM/yyyy).");
            }

            var candidateRequest = new CreateCustomerRequest
            {
                FullName = fullName,
                Email = email,
                PhoneNumber = phoneNumber,
                // Placeholder when unparseable, purely so the other field
                // rules (name/email/phone) still run in the same pass — the
                // DateOfBirth-specific error above is what actually reports
                // the problem, so FluentValidation's own DateOfBirth errors
                // for this placeholder are filtered out below.
                DateOfBirth = dateParsed ? dateOfBirth : DateOnly.FromDateTime(DateTime.UtcNow),
                IsActive = true
            };

            var validationResult = await _validator.ValidateAsync(candidateRequest, ct);
            errors.AddRange(validationResult.Errors
                .Where(e => dateParsed || e.PropertyName != nameof(CreateCustomerRequest.DateOfBirth))
                .Select(e => e.ErrorMessage));

            var status = ImportRowStatus.Valid;
            if (errors.Count > 0)
            {
                status = ImportRowStatus.Invalid;
            }
            else if (seenEmailsInFile.TryGetValue(normalizedEmail, out var firstRowNumber))
            {
                status = ImportRowStatus.DuplicateInFile;
                errors.Add($"Email trùng lặp trong file (dòng {firstRowNumber}).");
            }
            else
            {
                seenEmailsInFile[normalizedEmail] = raw.RowNumber;
            }

            rowResults.Add(new ImportRowResult
            {
                RowNumber = raw.RowNumber,
                FullName = fullName,
                Email = email,
                PhoneNumber = phoneNumber,
                DateOfBirthRaw = raw.DateOfBirthRaw,
                Status = status,
                Errors = errors
            });

            if (status == ImportRowStatus.Valid)
            {
                candidates.Add(new ImportCandidate
                {
                    RowNumber = raw.RowNumber,
                    FullName = fullName,
                    Email = normalizedEmail,
                    PhoneNumber = phoneNumber,
                    DateOfBirth = dateOfBirth
                });
            }
        }

        return (rowResults, candidates);
    }

    /// <summary>Single round trip for every still-candidate email. Goes
    /// through AppDbContext's Global Query Filter, so a soft-deleted
    /// customer's email is never treated as taken — matches the filtered
    /// unique index ([IsDeleted] = 0) on Customers.Email.</summary>
    private async Task MarkDatabaseDuplicatesAsync(List<ImportRowResult> rowResults, List<ImportCandidate> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
        {
            return;
        }

        var candidateEmails = candidates.Select(c => c.Email).ToList();
        var existingEmails = await _context.Customers
            .Where(c => candidateEmails.Contains(c.Email))
            .Select(c => c.Email)
            .ToListAsync(ct);

        if (existingEmails.Count == 0)
        {
            return;
        }

        var existingSet = new HashSet<string>(existingEmails);
        var candidateByRow = candidates.ToDictionary(c => c.RowNumber);

        foreach (var row in rowResults)
        {
            if (candidateByRow.TryGetValue(row.RowNumber, out var candidate) && existingSet.Contains(candidate.Email))
            {
                row.Status = ImportRowStatus.DuplicateInDatabase;
                row.Errors.Add("Email đã tồn tại trong hệ thống.");
            }
        }
    }

    private static bool IsStillValid(List<ImportRowResult> rowResults, int rowNumber) =>
        rowResults.First(r => r.RowNumber == rowNumber).Status == ImportRowStatus.Valid;

    private ImportSession GetSessionOrThrow(Guid importSessionId)
    {
        if (!_cache.TryGetValue(CacheKey(importSessionId), out ImportSession? session) || session is null)
        {
            throw new NotFoundException("ImportSession", importSessionId);
        }

        return session;
    }

    private static string CacheKey(Guid sessionId) => $"customer-import:{sessionId}";

    private static bool TryParseDate(string raw, out DateOnly result) =>
        DateOnly.TryParseExact(raw.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
}
