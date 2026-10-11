using System.Net;
using System.Text;

namespace API.Tests.AzureDevOps;

/// <summary>Routes requests to canned responses and records every request sent.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond)> _routes = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHttpMessageHandler On(Func<HttpRequestMessage, bool> match, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((match, (request, _) => Task.FromResult(respond(request))));
        return this;
    }

    public FakeHttpMessageHandler OnAsync(Func<HttpRequestMessage, bool> match, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        _routes.Add((match, respond));
        return this;
    }

    public FakeHttpMessageHandler OnJson(Func<HttpRequestMessage, bool> match, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(match, _ => Json(json, status));

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        foreach (var (match, respond) in _routes)
        {
            if (match(request))
                return await respond(request, cancellationToken);
        }

        return Json("{\"message\":\"No fake route for " + request.RequestUri!.PathAndQuery + "\"}", HttpStatusCode.NotFound);
    }
}
