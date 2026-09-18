using System.Text;
using ClosedXML.Excel;
using CustomerManager.Application.Common.Exceptions;
using CustomerManager.Application.Services;
using CustomerManager.Application.Validators;
using CustomerManager.Contracts.Customers.Import;
using CustomerManager.Infrastructure.Persistence.Imports;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CustomerManager.UnitTests;

public class CustomerImportServiceTests
{
    private static IConfiguration TestConfiguration(int maxRows = 10_000, long maxFileSizeBytes = 5 * 1024 * 1024) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:MaxRows"] = maxRows.ToString(),
                ["Import:MaxFileSizeBytes"] = maxFileSizeBytes.ToString(),
                ["Import:SessionExpiryMinutes"] = "15"
            })
            .Build();

    private static CustomerImportService CreateSut(CustomerManager.Infrastructure.Persistence.AppDbContext context, IConfiguration? configuration = null) => new(
        context,
        new FakeCustomerCodeGenerator(),
        new ClosedXmlCsvCustomerImportFileParser(),
        new ClosedXmlCustomerImportWorkbookWriter(),
        new CreateCustomerRequestValidator(),
        new MemoryCache(new MemoryCacheOptions()),
        configuration ?? TestConfiguration());

    private static byte[] BuildXlsx(
        IEnumerable<(string FullName, string Email, string Phone, string Dob)> rows,
        bool includeAllHeaders = true)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");

        sheet.Cell(1, 1).Value = "FullName";
        sheet.Cell(1, 2).Value = "Email";
        if (includeAllHeaders)
        {
            sheet.Cell(1, 3).Value = "PhoneNumber";
            sheet.Cell(1, 4).Value = "DateOfBirth";
        }

        var r = 2;
        foreach (var (fullName, email, phone, dob) in rows)
        {
            sheet.Cell(r, 1).Value = fullName;
            sheet.Cell(r, 2).Value = email;
            if (includeAllHeaders)
            {
                sheet.Cell(r, 3).SetValue(phone); // SetValue keeps Text data type — leading 0 survives.
                sheet.Cell(r, 4).Value = dob;
            }

            r++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildCsv(string delimiter, bool withBom, IEnumerable<(string FullName, string Email, string Phone, string Dob)> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(delimiter, "FullName", "Email", "PhoneNumber", "DateOfBirth")).Append('\n');
        foreach (var (fullName, email, phone, dob) in rows)
        {
            sb.Append(string.Join(delimiter, fullName, email, phone, dob)).Append('\n');
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return withBom ? Encoding.UTF8.GetPreamble().Concat(bytes).ToArray() : bytes;
    }

    private static (string FullName, string Email, string Phone, string Dob) Row(int n) =>
        ($"Nguyen Van {n}", $"customer{n}@example.com", $"0{n:D9}", "15/01/1990");

    [Fact]
    public async Task PreviewAsync_ShouldMarkAllRowsValid_WhenXlsxIsWellFormed()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);
        var bytes = BuildXlsx(new[] { Row(1), Row(2), Row(3) });

        var result = await sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        result.TotalRows.Should().Be(3);
        result.ValidRows.Should().Be(3);
        result.InvalidRows.Should().Be(0);
        result.DuplicateRows.Should().Be(0);
        result.Rows[0].PhoneNumber.Should().Be("0000000001"); // leading zero must survive the Text-typed cell round trip
    }

    [Fact]
    public async Task ConfirmAsync_ShouldImportAllRows_WithSequentialCodes_AndAuditLog_WhenPreviewIsAllValid()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);
        var bytes = BuildXlsx(new[] { Row(1), Row(2) });
        var preview = await sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        var result = await sut.ConfirmAsync(preview.ImportSessionId, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ImportedRows.Should().Be(2);
        context.Customers.Select(c => c.CustomerCode).OrderBy(c => c).Should().Equal("KH-0001", "KH-0002");
        context.AuditLogs.Count(a => a.Action == "Added").Should().Be(2);
    }

    [Fact]
    public async Task PreviewAsync_ShouldParseCsv_WithSemicolonDelimiterAndUtf8Bom()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);
        var bytes = BuildCsv(";", withBom: true, new[] { Row(1), Row(2) });

        var result = await sut.PreviewAsync(new MemoryStream(bytes), "customers.csv", bytes.Length, CancellationToken.None);

        result.TotalRows.Should().Be(2);
        result.ValidRows.Should().Be(2);
    }

    [Fact]
    public async Task PreviewAsync_ShouldThrow_WhenFileIsEmpty()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);

        var act = () => sut.PreviewAsync(new MemoryStream(), "customers.xlsx", 0, CancellationToken.None);

        await act.Should().ThrowAsync<ImportFileException>();
    }

    [Fact]
    public async Task PreviewAsync_ShouldThrow_WhenExtensionIsUnsupported()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);
        var bytes = Encoding.UTF8.GetBytes("not a real file");

        var act = () => sut.PreviewAsync(new MemoryStream(bytes), "customers.pdf", bytes.Length, CancellationToken.None);

        await act.Should().ThrowAsync<ImportFileException>();
    }

    [Fact]
    public async Task PreviewAsync_ShouldThrow_WhenXlsxIsCorrupted()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }; // not a ZIP/xlsx signature

        var act = () => sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        await act.Should().ThrowAsync<ImportFileException>();
    }

    [Fact]
    public async Task PreviewAsync_ShouldThrow_WhenRequiredColumnIsMissing()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);
        // Only FullName/Email headers — PhoneNumber/DateOfBirth missing.
        var bytes = BuildXlsx(new[] { Row(1) }, includeAllHeaders: false);

        var act = () => sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        (await act.Should().ThrowAsync<ImportFileException>()).Which.Message.Should().Contain("PhoneNumber");
    }

    [Fact]
    public async Task PreviewAsync_ShouldMarkRowInvalid_WhenEmailFormatIsWrong()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);
        var bytes = BuildXlsx(new[] { ("Nguyen Van A", "not-an-email", "0900000001", "15/01/1990") });

        var result = await sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        result.Rows.Single().Status.Should().Be(ImportRowStatus.Invalid);
        result.Rows.Single().Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PreviewAsync_ShouldMarkSecondOccurrence_AsDuplicateInFile()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);
        var bytes = BuildXlsx(new[]
        {
            ("Nguyen Van A", "same@example.com", "0900000001", "15/01/1990"),
            ("Nguyen Van B", "SAME@example.com", "0900000002", "15/01/1990") // different case, same normalized email
        });

        var result = await sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        result.Rows[0].Status.Should().Be(ImportRowStatus.Valid);
        result.Rows[1].Status.Should().Be(ImportRowStatus.DuplicateInFile);
        result.ValidRows.Should().Be(1);
    }

    [Fact]
    public async Task PreviewAsync_ShouldMarkDuplicateInDatabase_ButNotForSoftDeletedCustomer()
    {
        await using var context = TestDbContextFactory.Create();
        var customerService = new CustomerService(context, new FakeCustomerCodeGenerator());
        var active = await customerService.CreateAsync(new()
        {
            FullName = "Existing Active",
            Email = "active@example.com",
            PhoneNumber = "0900000099",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true
        }, CancellationToken.None);

        var deleted = await customerService.CreateAsync(new()
        {
            FullName = "Existing Deleted",
            Email = "deleted@example.com",
            PhoneNumber = "0900000098",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true
        }, CancellationToken.None);
        await customerService.DeleteAsync(deleted.Id, CancellationToken.None);

        var sut = CreateSut(context);
        var bytes = BuildXlsx(new[]
        {
            ("New For Active Email", "active@example.com", "0900000001", "15/01/1990"),
            ("New For Deleted Email", "deleted@example.com", "0900000002", "15/01/1990")
        });

        var result = await sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        result.Rows[0].Status.Should().Be(ImportRowStatus.DuplicateInDatabase);
        result.Rows[1].Status.Should().Be(ImportRowStatus.Valid); // email freed up by soft delete + filtered unique index
    }

    [Fact]
    public async Task PreviewAsync_ShouldThrow_WhenRowCountExceedsMaxRows()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context, TestConfiguration(maxRows: 2));
        var bytes = BuildXlsx(new[] { Row(1), Row(2), Row(3) });

        var act = () => sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        await act.Should().ThrowAsync<ImportFileException>();
    }

    [Fact]
    public async Task PreviewAsync_ShouldThrow_WhenFileExceedsMaxSize()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context, TestConfiguration(maxFileSizeBytes: 10));
        var bytes = BuildXlsx(new[] { Row(1) });

        var act = () => sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        await act.Should().ThrowAsync<ImportFileException>();
    }

    [Fact]
    public async Task ConfirmAsync_ShouldThrowNotFound_WhenSessionIdIsUnknown()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);

        var act = () => sut.ConfirmAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ConfirmAsync_ShouldImportNothing_WhenEmailWasTakenAfterPreview()
    {
        // All-or-nothing: a row that was Valid at Preview time can still lose
        // the race to a separate request before Confirm runs — must abort the
        // whole batch, not silently skip just that one row.
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);
        var bytes = BuildXlsx(new[] { Row(1), Row(2) });
        var preview = await sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        var customerService = new CustomerService(context, new FakeCustomerCodeGenerator());
        await customerService.CreateAsync(new()
        {
            FullName = "Raced In First",
            Email = "customer1@example.com", // same as Row(1)
            PhoneNumber = "0900000077",
            DateOfBirth = new DateOnly(1990, 1, 1),
            IsActive = true
        }, CancellationToken.None);

        var act = () => sut.ConfirmAsync(preview.ImportSessionId, CancellationToken.None);

        await act.Should().ThrowAsync<ImportValidationFailedException>();
        context.Customers.Count().Should().Be(1); // only the one seeded above — nothing from the import was written
    }

    [Fact]
    public async Task PreviewAsync_ShouldReadExcelDateSerialCell_ForDateOfBirth()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sheet1");
        sheet.Cell(1, 1).Value = "FullName";
        sheet.Cell(1, 2).Value = "Email";
        sheet.Cell(1, 3).Value = "PhoneNumber";
        sheet.Cell(1, 4).Value = "DateOfBirth";
        sheet.Cell(2, 1).Value = "Nguyen Van A";
        sheet.Cell(2, 2).Value = "dateserial@example.com";
        sheet.Cell(2, 3).SetValue("0900000001");
        sheet.Cell(2, 4).Value = new DateTime(1990, 1, 15); // written as a real Excel date, not text
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var bytes = stream.ToArray();

        var result = await sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        result.Rows.Single().Status.Should().Be(ImportRowStatus.Valid);
        result.Rows.Single().DateOfBirthRaw.Should().Be("15/01/1990");
    }

    [Fact]
    public async Task BuildTemplate_ShouldProduceAFileThatParsesBackSuccessfully()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);

        var templateBytes = sut.BuildTemplate();
        var result = await sut.PreviewAsync(new MemoryStream(templateBytes), "customer-import-template.xlsx", templateBytes.Length, CancellationToken.None);

        result.TotalRows.Should().Be(3); // the 3 sample rows baked into the template
        result.InvalidRows.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmAsync_ShouldImportSuccessfully_ForABulkFile()
    {
        await using var context = TestDbContextFactory.Create();
        var sut = CreateSut(context);
        var rows = Enumerable.Range(1, 200).Select(Row).ToList();
        var bytes = BuildXlsx(rows);
        var preview = await sut.PreviewAsync(new MemoryStream(bytes), "customers.xlsx", bytes.Length, CancellationToken.None);

        var result = await sut.ConfirmAsync(preview.ImportSessionId, CancellationToken.None);

        result.ImportedRows.Should().Be(200);
        context.Customers.Count().Should().Be(200);
    }
}
