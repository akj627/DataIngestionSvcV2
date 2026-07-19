using System.Net;

namespace DataIngestion.Tests.Helpers;

internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly byte[] _responseContent;
    private readonly HttpStatusCode _statusCode;

    public MockHttpMessageHandler(byte[] responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseContent = responseContent;
        _statusCode = statusCode;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new ByteArrayContent(_responseContent)
        };
        return Task.FromResult(response);
    }
}
