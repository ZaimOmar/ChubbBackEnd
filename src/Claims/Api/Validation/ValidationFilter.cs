using Chubb.Claims.Api.Problems;
using FluentValidation;

namespace Chubb.Claims.Api;

public sealed class ValidationFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().Single();
        var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<TRequest>>();
        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(failure => ToCamelCase(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray());
        return ProblemResults.Validation(errors, CorrelationIdMiddleware.From(context.HttpContext)).ToResult();
    }

    private static string ToCamelCase(string name) =>
        string.Join('.', name.Split('.').Select(part => char.ToLowerInvariant(part[0]) + part[1..]));
}
