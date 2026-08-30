using System.Net;

namespace SurvivalBackend.Infrastructure.Edgegap;

public sealed class EdgegapApiException : Exception
{
    public EdgegapApiException(HttpStatusCode statusCode, string responseContent)
        : base($"Edgegap API returned {(int)statusCode}.")
    {
        StatusCode = statusCode;
        ResponseContent = responseContent;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseContent { get; }
}
