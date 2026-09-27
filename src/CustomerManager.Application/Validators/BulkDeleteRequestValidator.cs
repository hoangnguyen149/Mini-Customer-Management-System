using CustomerManager.Contracts.Customers;
using FluentValidation;

namespace CustomerManager.Application.Validators;

public class BulkDeleteRequestValidator : AbstractValidator<BulkDeleteRequest>
{
    public BulkDeleteRequestValidator()
    {
        RuleFor(x => x.Ids)
            .NotEmpty().WithMessage("Chưa chọn khách hàng nào để xoá.")
            .Must(ids => ids.Count <= BulkDeleteRequest.MaxItems)
            .WithMessage($"Chỉ được xoá tối đa {BulkDeleteRequest.MaxItems} khách hàng mỗi lần.");
    }
}
