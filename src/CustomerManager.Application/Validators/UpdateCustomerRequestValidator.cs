using CustomerManager.Contracts.Customers;
using FluentValidation;

namespace CustomerManager.Application.Validators;

public class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator(TimeProvider timeProvider)
    {
        Include(new CustomerFieldsValidator(timeProvider));

        // A malformed RowVersion is bad input (400), not a concurrency
        // conflict (409) — validate the format here instead of letting
        // CustomerService turn a FormatException into a misleading 409.
        RuleFor(x => x.RowVersion)
            .NotEmpty().WithMessage("Thiếu RowVersion — không thể xác định phiên bản dữ liệu đang chỉnh sửa.")
            .Must(RowVersionFormat.IsValid).WithMessage("RowVersion không hợp lệ.");
    }
}
