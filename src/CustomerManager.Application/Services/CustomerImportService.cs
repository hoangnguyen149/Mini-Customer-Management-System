using System.Globalization;
using CustomerManager.Application.Common;
using CustomerManager.Application.Common.Exceptions;
using CustomerManager.Application.Common.Options;
using CustomerManager.Application.Imports;
using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Customers;
using CustomerManager.Contracts.Customers.Import;
using CustomerManager.Domain.Entities;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

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
    private static readonly string[] SupportedExtensions = { ".xlsx", ".csv" };
    private static readonly string[] DateFormats = { "dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy" };

    private readonly IApplicationDbContext _context;
    private readonly ICustomerCodeGenerator _customerCodeGenerator;
    private readonly ICustomerImportFileParser _parser;
    private readonly ICustomerImportWorkbookWriter _workbookWriter;
    private readonly IValidator<CreateCustomerRequest> _validator;
    private readonly IMemoryCache _cache;
    private readonly ImportOptions _options;
    private readonly TimeProvider _timeProvider;

    public CustomerImportService(
        IApplicationDbContext context,
        ICustomerCodeGenerator customerCodeGenerator,
        ICustomerImportFileParser parser,
        ICustomerImportWorkbookWriter workbookWriter,
        IValidator<CreateCustomerRequest> validator,
        IMemoryCache cache,
        IOptions<ImportOptions> options,
        TimeProvider timeProvider)
    {
        _context = context;
        _customerCodeGenerator = customerCodeGenerator;
        _parser = parser;
        _workbookWriter = workbookWriter;
        _validator = validator;
        _cache = cache;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public byte[] BuildTemplate() => _workbookWriter.BuildTemplate();

    public async Task<ImportPreviewResponse> PreviewAsync(Stream fileContent, string fileName, long fileSizeBytes, CancellationToken ct)
    {
        if (fileSizeBytes <= 0)
        {
            throw new ImportFileException("File rỗng, vui lòng chọn file khác.");
        }

        if (fileSizeBytes > _options.MaxFileSizeBytes)
        {
            throw new ImportFileException($"File vượt quá kích thước tối đa {_options.MaxFileSizeBytes / 1024 / 1024} MB.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            throw new ImportFileException($"Định dạng file '{extension}' không được hỗ trợ. Chỉ chấp nhận .xlsx hoặc .csv.");
        }

        var rawRows = await _parser.ParseAsync(fileContent, fileName, _options.MaxRows, ct);

        if (rawRows.Count == 0)
        {
            throw new ImportFileException("File không chứa dữ liệu khách hàng nào.");
        }

        var (rowResults, candidates) = await ValidateRowsAsync(rawRows, ct);
        await MarkDatabaseDuplicatesAsync(rowResults, candidates, ct);

        // O(n): the old per-candidate First() lookup was O(n²) — ~50M
        // comparisons for a 10,000-row file.
        var validRowNumbers = rowResults
            .Where(r => r.Status == ImportRowStatus.Valid)
            .Select(r => r.RowNumber)
            .ToHashSet();

        var session = new ImportSession
        {
            AllRows = rowResults,
            ValidCandidates = candidates.Where(c => validRowNumbers.Contains(c.RowNumber)).ToList()
        };

        var sessionId = Guid.NewGuid();
        var expiresAtUtc = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(_options.SessionExpiryMinutes);
        _cache.Set(CacheKey(sessionId), session, new DateTimeOffset(expiresAtUtc, TimeSpan.Zero));

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

        // Double click / client retry: a second concurrent Confirm of the same
        // session would otherwise pass the re-check below too and collide on
        // the Email unique index (a raw 500).
        if (!session.TryBeginConfirm())
        {
            throw new ConflictException("Phiên import này đang được xác nhận, vui lòng đợi.");
        }

        try
        {
            // State may have changed since Preview (another request took one of
            // these emails) — re-check before writing anything. All-or-nothing.
            var conflictedEmails = await FindExistingEmailsAsync(session.ValidCandidates.Select(c => c.Email).ToList(), ct);
            if (conflictedEmails.Count > 0)
            {
                throw RejectWithConflicts(importSessionId, session, conflictedEmails);
            }

            // One round trip for all codes instead of one per row.
            var codes = await _customerCodeGenerator.NextRangeAsync(session.ValidCandidates.Count, ct);
            var entities = session.ValidCandidates
                .Select((candidate, i) => Customer.Create(codes[i], candidate.FullName, candidate.Email, candidate.PhoneNumber, candidate.DateOfBirth, isActive: true))
                .ToList();

            _context.Customers.AddRange(entities);

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == DatabaseConstraintNames.CustomerEmailUnique)
            {
                // Lost the race after the re-check above: another request
                // inserted one of these emails in between. Nothing was written
                // (single SaveChanges = single transaction).
                foreach (var entity in entities)
                {
                    _context.Customers.Entry(entity).State = EntityState.Detached;
                }

                var nowTaken = await FindExistingEmailsAsync(session.ValidCandidates.Select(c => c.Email).ToList(), ct);
                throw RejectWithConflicts(importSessionId, session, nowTaken);
            }

            // Consumed: a later Confirm of the same id is a 404, not a re-import.
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
        finally
        {
            session.EndConfirm();
        }
    }

    private async Task<HashSet<string>> FindExistingEmailsAsync(List<string> emails, CancellationToken ct)
    {
        if (emails.Count == 0)
        {
            return new HashSet<string>();
        }

        var existing = await _context.Customers
            .Where(c => emails.Contains(c.Email))
            .Select(c => c.Email)
            .ToListAsync(ct);

        return existing.ToHashSet();
    }

    /// <summary>Builds new row objects (never mutates the cached ones), caches
    /// the updated session so the error report reflects the new conflicts, and
    /// returns the exception to throw.</summary>
    private ImportValidationFailedException RejectWithConflicts(Guid sessionId, ImportSession session, HashSet<string> conflictedEmails)
    {
        var conflictedRowNumbers = session.ValidCandidates
            .Where(c => conflictedEmails.Contains(c.Email))
            .Select(c => c.RowNumber)
            .ToHashSet();

        var updatedRows = session.AllRows
            .Select(r => conflictedRowNumbers.Contains(r.RowNumber)
                ? CopyWithStatus(r, ImportRowStatus.DuplicateInDatabase, "Email đã được sử dụng bởi một khách hàng khác kể từ lúc xem trước (preview).")
                : r)
            .ToList();

        var updatedSession = new ImportSession
        {
            AllRows = updatedRows,
            ValidCandidates = session.ValidCandidates.Where(c => !conflictedRowNumbers.Contains(c.RowNumber)).ToList()
        };
        _cache.Set(CacheKey(sessionId), updatedSession, TimeSpan.FromMinutes(_options.SessionExpiryMinutes));

        var errorRows = updatedRows.Where(r => r.Status != ImportRowStatus.Valid).ToList();
        return new ImportValidationFailedException(
            "Dữ liệu đã thay đổi kể từ lúc xem trước, import bị huỷ — chưa có khách hàng nào được ghi. Vui lòng tải lại file và thử lại.",
            totalRows: updatedRows.Count,
            validRows: updatedRows.Count - errorRows.Count,
            invalidRows: errorRows.Count,
            errors: errorRows);
    }

    private static ImportRowResult CopyWithStatus(ImportRowResult row, ImportRowStatus status, string extraError) => new()
    {
        RowNumber = row.RowNumber,
        FullName = row.FullName,
        Email = row.Email,
        PhoneNumber = row.PhoneNumber,
        DateOfBirthRaw = row.DateOfBirthRaw,
        Status = status,
        Errors = row.Errors.Append(extraError).ToList()
    };

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
                DateOfBirth = dateParsed ? dateOfBirth : DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime),
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
