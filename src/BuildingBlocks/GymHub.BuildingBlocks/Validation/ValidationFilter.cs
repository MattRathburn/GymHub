using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace GymHub.BuildingBlocks.Validation;

/// <summary>
/// Validates the endpoint's <typeparamref name="T"/> argument with its FluentValidation validator.
/// </summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();

        if (argument is null)
        {
            return TypedResults.Problem($"Request body of type {typeof(T).Name} is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);

        if (!result.IsValid)
        {
            return TypedResults.ValidationProblem(result.ToDictionary());
        }

        return await next(context);
    }
}

public static class ValidationExtensions
{
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
