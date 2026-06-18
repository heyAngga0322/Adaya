namespace OrderManagement.Core.Exceptions;

public sealed class ValidationException(string message) : AppException(message)
{
    public override int StatusCode => HttpStatusCodes.BadRequest;
    public override string ErrorCode => "VALIDATION_ERROR";
}
