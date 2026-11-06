using System.Net;

namespace MianMianPet.Services;

public sealed class DeepSeekApiException : Exception
{
    public DeepSeekApiException(HttpStatusCode statusCode, string userMessage)
        : base(userMessage)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
