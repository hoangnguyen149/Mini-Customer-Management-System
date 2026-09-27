using CustomerManager.Contracts.Customers;
using FluentValidation;

namespace CustomerManager.Application.Validators;

/// <summary>
/// The one place customer field rules live. Included by both
/// CreateCustomerRequestValidator and UpdateCustomerRequestValidator (and
/// therefore by the bulk import too, which reuses the Create validator).
/// Internal so AddValidatorsFromAssembly doesn't register it on its own.
/// </summary>
internal sealed class CustomerFieldsValidator : AbstractValidator<ICustomerWriteModel>
{
    public const string PhoneNumberPattern = @"^0\d{9}$";

    public CustomerFieldsValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ và tên không được để trống.")
            .MaximumLength(100).WithMessage("Họ và tên tối đa 100 ký tự.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống.")
            .EmailAddress().WithMessage("Email không đúng định dạng.")
            .MaximumLength(150).WithMessage("Email tối đa 150 ký tự.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .Matches(PhoneNumberPattern).WithMessage("Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty()
            .LessThan(_ => Today(timeProvider)).WithMessage("Ngày sinh phải là một ngày trong quá khứ.")
            .GreaterThan(_ => Today(timeProvider).AddYears(-120)).WithMessage("Ngày sinh không hợp lệ.");
    }

    private static DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}
