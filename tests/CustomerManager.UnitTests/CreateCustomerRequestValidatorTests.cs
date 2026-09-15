using CustomerManager.Application.Validators;
using CustomerManager.Contracts.Customers;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace CustomerManager.UnitTests;

public class CreateCustomerRequestValidatorTests
{
    private readonly CreateCustomerRequestValidator _validator = new();

    private static CreateCustomerRequest ValidRequest() => new()
    {
        FullName = "Nguyễn Văn A",
        Email = "a@example.com",
        PhoneNumber = "0901234567",
        DateOfBirth = new DateOnly(1990, 1, 1),
        IsActive = true
    };

    [Fact]
    public void Should_NotHaveErrors_WhenRequestIsValid()
    {
        var result = _validator.TestValidate(ValidRequest());
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_HaveError_WhenPhoneNumberIsEmpty()
    {
        var request = ValidRequest();
        request.PhoneNumber = string.Empty;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData("12345")]        // too short, wrong prefix
    [InlineData("190123456789")] // too long
    [InlineData("abcdefghij")]   // not digits
    public void Should_HaveError_WhenPhoneNumberFormatIsInvalid(string phoneNumber)
    {
        var request = ValidRequest();
        request.PhoneNumber = phoneNumber;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.PhoneNumber);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    [InlineData("")]
    public void Should_HaveError_WhenEmailIsInvalid(string email)
    {
        var request = ValidRequest();
        request.Email = email;

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Should_HaveError_WhenDateOfBirthIsInTheFuture()
    {
        var request = ValidRequest();
        request.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var result = _validator.TestValidate(request);

        result.ShouldHaveValidationErrorFor(x => x.DateOfBirth);
    }
}
