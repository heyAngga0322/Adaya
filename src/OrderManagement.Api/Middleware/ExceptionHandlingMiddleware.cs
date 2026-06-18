using System.Text.Json;
using OrderManagement.Core.Contracts;
using OrderManagement.Core.Exceptions;
using OrderManagement.Api.Middleware;

namespace OrderManagement.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items[CorrelationIdConstants.ItemKey]?.ToString();

        switch (exception)
        {
            case AppException appException:
                logger.LogWarning(
                    exception,
                    "Handled application exception {ErrorCode}: {Message}",
                    appException.ErrorCode,
                    appException.Message);
                await WriteErrorAsync(context, appException.StatusCode, appException.ErrorCode, appException.Message, correlationId);
                break;
            default:
                logger.LogError(exception, "Unhandled exception");
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "An unexpected error occurred.",
                    correlationId);
                break;
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string errorCode,
        string message,
        string? correlationId,
        IDictionary<string, string[]>? validationErrors = null)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var payload = new ErrorResponse(errorCode, message, correlationId, validationErrors);
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
