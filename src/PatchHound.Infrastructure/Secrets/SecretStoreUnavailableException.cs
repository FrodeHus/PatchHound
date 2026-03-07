using System.Net;

namespace PatchHound.Infrastructure.Secrets;

public class SecretStoreUnavailableException : Exception
{
    public SecretStoreUnavailableException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
