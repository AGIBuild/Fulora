using Agibuild.Fulora.Plugin.HttpClient;
using System.Net;
using System.Net.Http;
using Xunit;

namespace Agibuild.Fulora.UnitTests.Plugins;

public class HttpClientServiceTests
{
    private static HttpClientService CreateService(
        HttpClientOptions? options = null,
        HttpMessageHandler? handler = null) =>
        new(options, handler);

    [Fact]
    public async Task Get_ReturnsResponse()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://api.example.com/users", req.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":1}")
            };
        });

        var svc = CreateService(handler: handler);
        var res = await svc.GetAsync("https://api.example.com/users");

        Assert.Equal(200, res.StatusCode);
        Assert.True(res.IsSuccess);
        Assert.Equal("{\"id\":1}", res.Body);
    }

    [Fact]
    public async Task Post_SendsBodyAndReturnsResponse()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"created\":true}")
            };
        });

        var svc = CreateService(handler: handler);
        var res = await svc.Post("https://api.example.com/items", "{\"name\":\"test\"}");

        Assert.Equal(201, res.StatusCode);
        Assert.Equal("{\"name\":\"test\"}", capturedBody);
        Assert.Equal("{\"created\":true}", res.Body);
    }

    [Fact]
    public async Task Post_NullBody_DoesNotSendContent()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Null(req.Content);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var svc = CreateService(handler: handler);
        await svc.Post("https://api.example.com/items", null);
    }

    [Fact]
    public async Task BaseUrl_ResolvesRelativeUrls()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("https://api.example.com/v1/users", req.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var options = new HttpClientOptions { BaseUrl = "https://api.example.com/v1" };
        var svc = CreateService(options, handler);
        await svc.GetAsync("/users");
    }

    [Fact]
    public async Task BaseUrl_AbsoluteUrl_BypassesBaseUrl()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal("https://other.example.com/users", req.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var options = new HttpClientOptions { BaseUrl = "https://api.example.com" };
        var svc = CreateService(options, handler);
        await svc.GetAsync("https://other.example.com/users");
    }

    [Fact]
    public async Task DefaultHeaders_AreApplied()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.True(req.Headers.TryGetValues("X-Api-Key", out var values));
            Assert.Equal("secret", values.First());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var options = new HttpClientOptions
        {
            DefaultHeaders = new Dictionary<string, string> { ["X-Api-Key"] = "secret" }
        };
        var svc = CreateService(options, handler);
        await svc.GetAsync("https://api.example.com/users");
    }

    [Fact]
    public async Task PerRequestHeaders_OverrideDefaults()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.True(req.Headers.TryGetValues("X-Custom", out var values));
            Assert.Equal("override", values.First());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var options = new HttpClientOptions
        {
            DefaultHeaders = new Dictionary<string, string> { ["X-Custom"] = "default" }
        };
        var svc = CreateService(options, handler);
        await svc.GetAsync("https://api.example.com/users", new Dictionary<string, string> { ["X-Custom"] = "override" });
    }

    [Fact]
    public async Task Timeout_IsConfigured()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var options = new HttpClientOptions { Timeout = TimeSpan.FromSeconds(5) };
        var svc = CreateService(options, handler);
        var res = await svc.GetAsync("https://api.example.com/users");
        Assert.Equal(200, res.StatusCode);
    }

    [Fact]
    public async Task Interceptors_ModifyRequest()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.True(req.Headers.TryGetValues("X-Intercepted", out var values));
            Assert.Equal("yes", values.First());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var interceptor = new AddHeaderInterceptor("X-Intercepted", "yes");
        var options = new HttpClientOptions { Interceptors = [interceptor] };
        var svc = CreateService(options, handler);
        await svc.GetAsync("https://api.example.com/users");
    }

    [Fact]
    public async Task Put_SendsBody()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var svc = CreateService(handler: handler);
        await svc.Put("https://api.example.com/items/1", "{\"name\":\"updated\"}");

        Assert.Equal("{\"name\":\"updated\"}", capturedBody);
    }

    [Fact]
    public async Task Delete_ReturnsResponse()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var svc = CreateService(handler: handler);
        var res = await svc.Delete("https://api.example.com/items/1");

        Assert.Equal(204, res.StatusCode);
    }

    [Fact]
    public async Task Patch_SendsBody()
    {
        string? capturedBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Patch, req.Method);
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var svc = CreateService(handler: handler);
        await svc.Patch("https://api.example.com/items/1", "{\"name\":\"patched\"}");

        Assert.Equal("{\"name\":\"patched\"}", capturedBody);
    }

    [Fact]
    public async Task Response_IncludesHeaders()
    {
        var handler = new MockHttpMessageHandler(_ =>
        {
            var res = new HttpResponseMessage(HttpStatusCode.OK);
            res.Headers.Add("X-Request-Id", "abc-123");
            res.Content = new StringContent("{}");
            res.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return res;
        });

        var svc = CreateService(handler: handler);
        var res = await svc.GetAsync("https://api.example.com/users");

        Assert.True(res.Headers.TryGetValue("X-Request-Id", out var value));
        Assert.Equal("abc-123", value);
    }

    [Fact]
    public async Task IsSuccess_ReflectsStatusCode()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var svc = CreateService(handler: handler);
        var res = await svc.GetAsync("https://api.example.com/missing");

        Assert.False(res.IsSuccess);
        Assert.Equal(404, res.StatusCode);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private sealed class AddHeaderInterceptor : IHttpRequestInterceptor
    {
        private readonly string _name;
        private readonly string _value;

        public AddHeaderInterceptor(string name, string value)
        {
            _name = name;
            _value = value;
        }

        public Task<HttpRequestMessage> InterceptAsync(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation(_name, _value);
            return Task.FromResult(request);
        }
    }
}
