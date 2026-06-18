namespace OrderManagement.Core.Exceptions;

public abstract class AppException : Exception
{
    protected AppException(string message) : base(message)
    {
    }

    public abstract int StatusCode { get; }
    public abstract string ErrorCode { get; }
}
