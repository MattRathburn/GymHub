using Microsoft.AspNetCore.Http;

namespace GymHub.BuildingBlocks.Results;

public static class ResultHttpExtensions
{
    public static IResult ToProblem(this Error error) => error.Kind switch
    {
        ErrorKind.NotFound => TypedResults.Problem(error.Message, statusCode: StatusCodes.Status404NotFound),
        ErrorKind.Forbidden => TypedResults.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden),
        ErrorKind.Conflict => TypedResults.Problem(error.Message, statusCode: StatusCodes.Status409Conflict),
        ErrorKind.PaymentRequired => TypedResults.Problem(error.Message, statusCode: StatusCodes.Status402PaymentRequired),
        _ => TypedResults.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest)
    };

    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value!) : result.Error!.ToProblem();

    public static IResult ToHttp<T>(this Result<T> result) =>
        result.ToHttp(value => TypedResults.Ok(value));

    public static IResult ToHttp(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error!.ToProblem();
}
