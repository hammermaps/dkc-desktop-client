using System.Net;
using DkcDesktopClient.Core.Services;
using Xunit;

namespace DkcDesktopClient.Tests;

public class ProtobufApiClientTests
{
    [Fact]
    public async Task SendAsync_UsesApiPhpEndpointWithoutPbPath()
    {
        var handler = new RecordingHandler(new byte[] { 0x08, 0x01 });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/app/")
        };
        var client = new DkcProtobufApiClient(httpClient);

        await client.SendAsync(new byte[] { 0x0A, 0x01, 0x61 });

        Assert.NotNull(handler.Request);
        Assert.Equal("https://example.test/api.php", handler.Request!.RequestUri!.ToString());
        Assert.DoesNotContain("/pb", handler.Request.RequestUri.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_SetsHeadersThatAllowApiPhpToDetectProtobufStream()
    {
        var handler = new RecordingHandler(Array.Empty<byte>());
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = new DkcProtobufApiClient(httpClient, () => "test-token");

        await client.SendAsync(new byte[] { 0x01, 0x02 });

        var request = handler.Request!;
        Assert.True(request.Headers.TryGetValues(DkcProtobufApiClient.ProtocolHeaderName, out var protocolValues));
        Assert.Contains(DkcProtobufApiClient.ProtocolHeaderValue, protocolValues);
        Assert.Contains(request.Headers.Accept, h => h.MediaType == DkcProtobufApiClient.ProtobufMediaType);
        Assert.Equal(DkcProtobufApiClient.ProtobufMediaType, request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-token", request.Headers.Authorization.Parameter);
        Assert.Contains("protobuf", request.Headers.UserAgent.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_ReturnsBinaryResponseUnchanged()
    {
        var responsePayload = new byte[] { 0x00, 0xFF, 0x10, 0x20 };
        var handler = new RecordingHandler(responsePayload);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test")
        };
        var client = new DkcProtobufApiClient(httpClient);

        var result = await client.SendAsync(new byte[] { 0x01 });

        Assert.Equal(responsePayload, result);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly byte[] _responsePayload;

        public RecordingHandler(byte[] responsePayload)
        {
            _responsePayload = responsePayload;
        }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_responsePayload)
                {
                    Headers =
                    {
                        ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(DkcProtobufApiClient.ProtobufMediaType)
                    }
                }
            });
        }
    }
}
