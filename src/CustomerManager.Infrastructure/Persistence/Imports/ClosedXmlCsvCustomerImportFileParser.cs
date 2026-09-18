using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using CustomerManager.Application.Common.Exceptions;
using CustomerManager.Application.Imports;
using CustomerManager.Application.Interfaces;

namespace CustomerManager.Infrastructure.Persistence.Imports;

/// <summary>
/// Reads the four required columns (FullName, Email, PhoneNumber,
/// DateOfBirth — see the Address-vs-DateOfBirth decision in README) from
/// either an .xlsx (ClosedXML) or .csv (CsvHelper) upload. Column order does
/// not matter — both formats are read by header name, case-insensitively.
///
/// Every exception this class can throw from the underlying library is
/// caught and rethrown as ImportFileException with a user-safe message —
/// callers (CustomerImportService, and ultimately GlobalExceptionHandler)
/// never see a ClosedXML/CsvHelper exception type.
/// </summary>
public class ClosedXmlCsvCustomerImportFileParser : ICustomerImportFileParser
{
    private static readonly string[] RequiredColumns = { "FullName", "Email", "PhoneNumber", "DateOfBirth" };

    public bool CanParse(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".xlsx" or ".csv";
    }

    public async Task<IReadOnlyList<ImportRawRow>> ParseAsync(Stream content, string fileName, int maxRows, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        // Buffer once: needed both for the magic-byte check on .xlsx and
        // because the upstream IFormFile stream isn't guaranteed seekable —
        // the file size limit already enforced by the caller/Kestrel bounds
        // how much this ever holds in memory.
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        return extension switch
        {
            ".xlsx" => ParseXlsx(buffer, maxRows),
            ".csv" => await ParseCsvAsync(buffer, maxRows, ct),
            _ => throw new ImportFileException($"Định dạng file '{extension}' không được hỗ trợ. Chỉ chấp nhận .xlsx hoặc .csv.")
        };
    }

    private static List<ImportRawRow> ParseXlsx(MemoryStream buffer, int maxRows)
    {
        // .xlsx is a ZIP archive — a renamed .exe/.pdf wearing an .xlsx
        // extension will not start with the ZIP local-file-header signature.
        // Reject before ever handing it to ClosedXML.
        var bytes = buffer.GetBuffer();
        if (buffer.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
        {
            throw new ImportFileException("File không đúng định dạng Excel (.xlsx). Vui lòng kiểm tra lại file.");
        }

        IXLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(buffer);
        }
        catch (Exception ex) when (ex is not ImportFileException)
        {
            throw new ImportFileException("File Excel bị lỗi hoặc không đọc được. Vui lòng kiểm tra lại file.");
        }

        using (workbook)
        {
            var worksheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new ImportFileException("File Excel không có sheet dữ liệu nào.");

            var headerRow = worksheet.Row(1);
            var columnIndexByHeader = BuildHeaderIndex(
                headerRow.CellsUsed().Select(c => (c.Address.ColumnNumber, Text: c.GetString())));
            EnsureRequiredColumnsPresent(columnIndexByHeader);

            var rows = new List<ImportRawRow>();
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                var row = worksheet.Row(rowNumber);

                var fullName = row.Cell(columnIndexByHeader["fullname"]).GetString();
                var email = row.Cell(columnIndexByHeader["email"]).GetString();

                var phoneCell = row.Cell(columnIndexByHeader["phonenumber"]);
                var phoneNumber = phoneCell.DataType == XLDataType.Text
                    ? phoneCell.GetString()
                    : phoneCell.GetFormattedString();

                var dobCell = row.Cell(columnIndexByHeader["dateofbirth"]);
                var dateOfBirthRaw = dobCell.DataType == XLDataType.DateTime
                    ? dobCell.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                    : dobCell.GetString();

                if (IsBlankRow(fullName, email, phoneNumber, dateOfBirthRaw))
                {
                    continue;
                }

                if (rows.Count >= maxRows)
                {
                    throw new ImportFileException($"File có hơn {maxRows} dòng dữ liệu, vượt quá giới hạn cho phép.");
                }

                rows.Add(new ImportRawRow
                {
                    RowNumber = rowNumber,
                    FullName = fullName,
                    Email = email,
                    PhoneNumber = phoneNumber,
                    DateOfBirthRaw = dateOfBirthRaw
                });
            }

            return rows;
        }
    }

    private static async Task<List<ImportRawRow>> ParseCsvAsync(MemoryStream buffer, int maxRows, CancellationToken ct)
    {
        string text;
        using (var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        {
            text = await reader.ReadToEndAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<ImportRawRow>();
        }

        // Excel on Vietnamese-locale Windows exports CSV with ';' as the
        // delimiter (',' is the decimal separator there), not ','. Sniff
        // from the header line instead of hard-coding either.
        var firstLine = text.Split('\n', 2)[0];
        var delimiter = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ";" : ",";

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim
        };

        try
        {
            using var stringReader = new StringReader(text);
            using var csv = new CsvReader(stringReader, config);

            if (!csv.Read() || !csv.ReadHeader())
            {
                return new List<ImportRawRow>();
            }

            var columnIndexByHeader = BuildHeaderIndex(
                (csv.HeaderRecord ?? Array.Empty<string>())
                    .Select((h, i) => (Column: i, Text: h)));
            EnsureRequiredColumnsPresent(columnIndexByHeader);

            var rows = new List<ImportRawRow>();

            while (csv.Read())
            {
                var fullName = GetField(csv, columnIndexByHeader["fullname"]);
                var email = GetField(csv, columnIndexByHeader["email"]);
                var phoneNumber = GetField(csv, columnIndexByHeader["phonenumber"]);
                var dateOfBirthRaw = GetField(csv, columnIndexByHeader["dateofbirth"]);

                if (IsBlankRow(fullName, email, phoneNumber, dateOfBirthRaw))
                {
                    continue;
                }

                if (rows.Count >= maxRows)
                {
                    throw new ImportFileException($"File có hơn {maxRows} dòng dữ liệu, vượt quá giới hạn cho phép.");
                }

                rows.Add(new ImportRawRow
                {
                    RowNumber = csv.Parser.Row,
                    FullName = fullName,
                    Email = email,
                    PhoneNumber = phoneNumber,
                    DateOfBirthRaw = dateOfBirthRaw
                });
            }

            return rows;
        }
        catch (Exception ex) when (ex is not ImportFileException)
        {
            throw new ImportFileException("File CSV bị lỗi hoặc không đọc được. Vui lòng kiểm tra lại file.");
        }
    }

    private static string GetField(CsvReader csv, int columnIndex) => csv.GetField(columnIndex)?.Trim() ?? string.Empty;

    private static bool IsBlankRow(params string[] values) => values.All(string.IsNullOrWhiteSpace);

    /// <summary>Maps a normalized header name ("fullname", "email", ...) to
    /// its 1-based (xlsx) or 0-based (csv) column index, whichever the
    /// caller's indexing convention needs.</summary>
    private static Dictionary<string, int> BuildHeaderIndex(IEnumerable<(int Column, string Text)> headerCells)
    {
        var map = new Dictionary<string, int>();
        foreach (var (column, text) in headerCells)
        {
            var normalized = NormalizeHeader(text);
            if (!string.IsNullOrEmpty(normalized) && !map.ContainsKey(normalized))
            {
                map[normalized] = column;
            }
        }

        return map;
    }

    private static void EnsureRequiredColumnsPresent(Dictionary<string, int> columnIndexByHeader)
    {
        var missing = RequiredColumns
            .Where(required => !columnIndexByHeader.ContainsKey(NormalizeHeader(required)))
            .ToList();

        if (missing.Count > 0)
        {
            throw new ImportFileException($"File thiếu cột bắt buộc: {string.Join(", ", missing)}.");
        }
    }

    private static string NormalizeHeader(string header) =>
        header.Replace(" ", string.Empty).Trim().ToLowerInvariant();
}
