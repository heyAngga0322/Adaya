namespace OrderManagement.Core.Exceptions;

public sealed class ConflictException(string message) : AppException(message)
{
    public override int StatusCode => HttpStatusCodes.Conflict;
    public override string ErrorCode => "CONFLICT";
}
