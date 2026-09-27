using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CustomerManager.WebApi.Filters;

/// <summary>
/// Runs the registered FluentValidation validator for every action argument
/// that has one, before the action executes. Controllers no longer call
/// ValidateAndThrowAsync themselves, so a new endpoint can't forget to.
/// Failures throw ValidationException, which GlobalExceptionHandler turns into
/// a 400 ProblemDetails with an "errors" dictionary.
/// </summary>
public sealed class FluentValidationFilter : IAsyncActionFilter
{
    private readonly IServiceProvider _services;

    public FluentValidationFilter(IServiceProvider services)
    {
        _services = services;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (_services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
        }

        await next();
    }
}
