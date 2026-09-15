using CustomerManager.Contracts.Customers;
using FluentValidation;

namespace CustomerManager.Application.Validators;

public class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ và tên không được để trống.")
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống.")
            .EmailAddress().WithMessage("Email không đúng định dạng.")
            .MaximumLength(150);

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .Matches(@"^0\d{9}$").WithMessage("Số điện thoại phải gồm 10 chữ số và bắt đầu bằng 0.");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty()
            .LessThan(_ => DateOnly.FromDateTime(DateTime.UtcNow)).WithMessage("Ngày sinh phải là một ngày trong quá khứ.")
            .GreaterThan(_ => DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-120))).WithMessage("Ngày sinh không hợp lệ.");

        RuleFor(x => x.RowVersion)
            .NotEmpty().WithMessage("Thiếu RowVersion — không thể xác định phiên bản dữ liệu đang chỉnh sửa.");
    }
}
