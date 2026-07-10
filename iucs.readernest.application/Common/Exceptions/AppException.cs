namespace iucs.readernest.application.Common.Exceptions
{
    /// <summary>
    /// Base for expected business failures; the API exception middleware maps
    /// StatusCode to the HTTP response instead of returning a 500.
    /// </summary>
    public abstract class AppException : Exception
    {
        protected AppException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; }
    }

    public class NotFoundException : AppException
    {
        public NotFoundException(string entityName, Guid id)
            : base(404, $"{entityName} with id '{id}' was not found.")
        {
        }

        public NotFoundException(string message)
            : base(404, message)
        {
        }
    }

    public class ConflictException : AppException
    {
        public ConflictException(string message)
            : base(409, message)
        {
        }
    }

    public class DomainValidationException : AppException
    {
        public DomainValidationException(string message)
            : base(400, message)
        {
        }
    }

    public class UnauthorizedException : AppException
    {
        public UnauthorizedException(string message)
            : base(401, message)
        {
        }
    }
}
