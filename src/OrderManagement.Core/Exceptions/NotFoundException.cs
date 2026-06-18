namespace OrderManagement.Core.Exceptions;

public sealed class NotFoundException(string message) : AppException(message)
{
    public override int StatusCode => HttpStatusCodes.NotFound;
    public override string ErrorCode => "NOT_FOUND";
}
