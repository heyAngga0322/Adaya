namespace OrderManagement.Core.Exceptions;

public sealed class UnprocessableException(string message) : AppException(message)
{
    public override int StatusCode => HttpStatusCodes.UnprocessableEntity;
    public override string ErrorCode => "UNPROCESSABLE";
}
