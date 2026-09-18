using ClosedXML.Excel;
using CustomerManager.Application.Interfaces;
using CustomerManager.Contracts.Customers.Import;

namespace CustomerManager.Infrastructure.Persistence.Imports;

public class ClosedXmlCustomerImportWorkbookWriter : ICustomerImportWorkbookWriter
{
    private static readonly string[] Headers = { "FullName", "Email", "PhoneNumber", "DateOfBirth" };

    public byte[] BuildTemplate()
    {
        using var workbook = new XLWorkbook();

        var sheet = workbook.Worksheets.Add("Customers");
        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.Bold = true;
        }

        // PhoneNumber column forced to Text format so a leading 0 survives
        // round-tripping through Excel (a General/Number-formatted cell
        // would silently drop it) — see ClosedXmlCsvCustomerImportFileParser's
        // GetFormattedString() read side, which only helps if the cell was
        // written as Text to begin with.
        sheet.Column(3).Style.NumberFormat.Format = "@";
        sheet.Column(4).Style.DateFormat.Format = "dd/MM/yyyy";

        var sampleRows = new[]
        {
            ("Nguyen Van A", "a@gmail.com", "0900000001", "15/01/1990"),
            ("Nguyen Van B", "b@gmail.com", "0900000002", "22/07/1985"),
            ("Nguyen Van C", "c@gmail.com", "0900000003", "03/11/1998")
        };

        for (var i = 0; i < sampleRows.Length; i++)
        {
            var (fullName, email, phone, dob) = sampleRows[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = fullName;
            sheet.Cell(row, 2).Value = email;
            sheet.Cell(row, 3).SetValue(phone); // SetValue (not Value=) keeps the Text data type instead of inferring Number
            sheet.Cell(row, 4).Value = dob;
        }

        sheet.Columns().AdjustToContents();

        var instructions = workbook.Worksheets.Add("Instructions");
        instructions.Cell(1, 1).Value = "Cột";
        instructions.Cell(1, 2).Value = "Quy tắc";
        instructions.Row(1).Style.Font.Bold = true;

        var rules = new[]
        {
            ("FullName", "Bắt buộc, tối đa 100 ký tự."),
            ("Email", "Bắt buộc, đúng định dạng email, tối đa 150 ký tự, không được trùng với khách hàng đã có."),
            ("PhoneNumber", "Bắt buộc, gồm đúng 10 chữ số và bắt đầu bằng 0 (ví dụ 0900000001)."),
            ("DateOfBirth", "Bắt buộc, định dạng dd/MM/yyyy, phải là một ngày trong quá khứ (không quá 120 năm).")
        };

        for (var i = 0; i < rules.Length; i++)
        {
            var (column, rule) = rules[i];
            instructions.Cell(i + 2, 1).Value = column;
            instructions.Cell(i + 2, 2).Value = rule;
        }

        instructions.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] BuildErrorReport(IReadOnlyList<ImportRowResult> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Errors");

        string[] headers = { "Row", "FullName", "Email", "PhoneNumber", "DateOfBirth", "Status", "Error" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
        }

        var line = 2;
        foreach (var row in rows)
        {
            sheet.Cell(line, 1).Value = row.RowNumber;
            sheet.Cell(line, 2).Value = row.FullName;
            sheet.Cell(line, 3).Value = row.Email;
            sheet.Cell(line, 4).SetValue(row.PhoneNumber);
            sheet.Cell(line, 5).Value = row.DateOfBirthRaw;
            sheet.Cell(line, 6).Value = row.Status.ToString();
            sheet.Cell(line, 7).Value = string.Join(" | ", row.Errors);
            line++;
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
