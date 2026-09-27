using CustomerManager.Contracts.Customers;
using FluentValidation;

namespace CustomerManager.Application.Validators;

public class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator(TimeProvider timeProvider)
    {
        Include(new CustomerFieldsValidator(timeProvider));
    }
}
