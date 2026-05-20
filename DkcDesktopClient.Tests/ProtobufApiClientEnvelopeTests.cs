using System.Net;
using System.Net.Http.Headers;
using DkcDesktopClient.Core.Protocol;
using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.Services;
using Google.Protobuf;
using Xunit;
using PbAction = DkcDesktopClient.Core.Protocol.Action;

namespace DkcDesktopClient.Tests;

public class ProtobufApiClientEnvelopeTests
{
    [Fact]
    public async Task SendAsync_BuildsTypedEnvelope_AndParsesTypedResponse()
    {
        // Server returns a successful envelope with a known payload.
        var responseInner = new AuthLoginResponse
        {
            Token = "dkc_xyz",
            TokenType = "Bearer",
            User = new UserInfo { Id = 1, Username = "alice", IsAdmin = true },
        };
        var responseEnvelope = BuildSuccessEnvelope(responseInner);

        var handler = new EnvelopeHandler(responseEnvelope, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        using var client = new DkcProtobufApiClient(http);

        var response = await client.SendAsync<AuthLoginResponse>(
            PbAction.AuthLogin,
            new AuthLoginRequest { Username = "alice", Password = "pw" },
            CompressionPreference.Identity);

        Assert.Equal("dkc_xyz", response.Token);
        Assert.Equal("alice", response.User.Username);

        // Verify the request envelope was correctly built and the action was set.
        Assert.NotNull(handler.LastRequestEnvelope);
        Assert.Equal(PbAction.AuthLogin, handler.LastRequestEnvelope!.Action);
        Assert.False(string.IsNullOrEmpty(handler.LastRequestEnvelope.RequestId));
    }

    [Fact]
    public async Task SendAsync_SetsProtobufHeadersAndAcceptEncoding()
    {
        var responseEnvelope = BuildSuccessEnvelope<Ack>(new Ack { Message = "ok" });
        var handler = new EnvelopeHandler(responseEnvelope, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        using var client = new DkcProtobufApiClient(http, () => "tok");

        await client.SendAsync<Ack>(PbAction.AuthLogout, null, CompressionPreference.Identity);

        var req = handler.LastHttpRequest!;
        Assert.Equal("https://example.test/api.php", req.RequestUri!.ToString());
        Assert.Contains(req.Headers.Accept, h => h.MediaType == DkcProtobufApiClient.ProtobufMediaType);
        Assert.True(req.Headers.TryGetValues(DkcProtobufApiClient.ProtocolHeaderName, out var protocolValues));
        Assert.Contains(DkcProtobufApiClient.ProtocolHeaderValue, protocolValues);

        Assert.True(req.Headers.TryGetValues("Accept-Encoding", out var acceptEnc));
        var enc = string.Join(',', acceptEnc);
        Assert.Contains(EnvelopeCodec.ContentEncodingLz4, enc);
        Assert.Contains(EnvelopeCodec.ContentEncodingGzip, enc);

        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("tok", req.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_FailedEnvelope_ThrowsTypedException()
    {
        var apiError = new ApiError
        {
            Code = ErrorCode.NotFound,
            Message = "no such mm",
        };
        apiError.Details.Add("field", "uid");

        var envelope = new ApiResponse
        {
            ProtocolVersion = EnvelopeCodec.ProtocolVersion,
            RequestId = "req-1",
            Success = false,
            Error = apiError,
            Compression = Compression.Identity,
        };

        var handler = new EnvelopeHandler(envelope.ToByteArray(), HttpStatusCode.NotFound);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        using var client = new DkcProtobufApiClient(http);

        var ex = await Assert.ThrowsAsync<DkcProtobufApiException>(() =>
            client.SendAsync<MmDetailResponse>(PbAction.MmDetail,
                new MmDetailRequest { Uid = "missing" },
                CompressionPreference.Identity));

        Assert.Equal(ErrorCode.NotFound, ex.Code);
        Assert.Equal("no such mm", ex.Message);
        Assert.Equal("uid", ex.Details["field"]);
        Assert.Equal("req-1", ex.RequestId);
    }

    [Fact]
    public async Task SendAsync_UnsupportedCompression_FallsBackToNextEncoding()
    {
        // First response: UNSUPPORTED_COMPRESSION; second response: success.
        var firstError = new ApiResponse
        {
            ProtocolVersion = EnvelopeCodec.ProtocolVersion,
            Success = false,
            Error = new ApiError { Code = ErrorCode.UnsupportedCompression, Message = "no lz4" },
            Compression = Compression.Identity,
        }.ToByteArray();

        var secondOk = BuildSuccessEnvelope(new Ack { Message = "ok" });

        var handler = new ScriptedHandler(new[]
        {
            (HttpStatusCode.BadRequest, firstError),
            (HttpStatusCode.OK,         secondOk),
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        using var client = new DkcProtobufApiClient(http);

        // Force LZ4 path with a payload large enough to actually be compressed.
        var bigPayload = new MmSaveRequest { Betreff = new string('a', 4096) };
        await client.SendAsync<Ack>(PbAction.MmCreate, bigPayload, CompressionPreference.Lz4);

        Assert.Equal(2, handler.Calls);
        // First attempt requested dkc-lz4; second attempt fell back to gzip (or identity).
        Assert.Equal(EnvelopeCodec.ContentEncodingLz4, handler.CapturedContentEncodings[0]);
        Assert.NotEqual(EnvelopeCodec.ContentEncodingLz4, handler.CapturedContentEncodings[1]);
    }

    [Fact]
    public async Task SendAsync_EmptyResponseBody_RaisesServiceUnavailable()
    {
        var handler = new EnvelopeHandler(Array.Empty<byte>(), HttpStatusCode.BadGateway);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        using var client = new DkcProtobufApiClient(http);

        var ex = await Assert.ThrowsAsync<DkcProtobufApiException>(() =>
            client.SendAsync<Ack>(PbAction.AuthStatus, null, CompressionPreference.Identity));

        Assert.Equal(ErrorCode.ServiceUnavailable, ex.Code);
    }

    [Fact]
    public async Task SendAsync_RequestIdIsEchoedThroughResponse()
    {
        var handler = new RequestIdEchoHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test") };
        using var client = new DkcProtobufApiClient(http);

        await client.SendAsync<Ack>(PbAction.AuthStatus, null, CompressionPreference.Identity);

        Assert.NotNull(handler.LastIncomingRequestId);
        Assert.Equal(handler.LastIncomingRequestId, handler.LastOutgoingRequestId);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static byte[] BuildSuccessEnvelope<T>(T inner) where T : IMessage<T>
    {
        var envelope = new ApiResponse
        {
            ProtocolVersion = EnvelopeCodec.ProtocolVersion,
            RequestId = "echo",
            Success = true,
            Compression = Compression.Identity,
            Payload = inner.ToByteString(),
        };
        return envelope.ToByteArray();
    }

    private sealed class EnvelopeHandler : HttpMessageHandler
    {
        private readonly byte[] _responseBytes;
        private readonly HttpStatusCode _status;
        public EnvelopeHandler(byte[] responseBytes, HttpStatusCode status)
        {
            _responseBytes = responseBytes;
            _status = status;
        }

        public HttpRequestMessage? LastHttpRequest { get; private set; }
        public ApiRequest? LastRequestEnvelope { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastHttpRequest = request;
            if (request.Content != null)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                LastRequestEnvelope = ApiRequest.Parser.ParseFrom(bytes);
            }
            return new HttpResponseMessage(_status)
            {
                Content = new ByteArrayContent(_responseBytes)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue(DkcProtobufApiClient.ProtobufMediaType) },
                },
            };
        }
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly (HttpStatusCode Status, byte[] Body)[] _responses;
        public int Calls { get; private set; }
        public List<string> CapturedContentEncodings { get; } = new();

        public ScriptedHandler((HttpStatusCode, byte[])[] responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedContentEncodings.Add(
                request.Content?.Headers.ContentEncoding.FirstOrDefault() ?? string.Empty);

            var idx = Math.Min(Calls, _responses.Length - 1);
            Calls++;
            var (status, body) = _responses[idx];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue(DkcProtobufApiClient.ProtobufMediaType) },
                },
            });
        }
    }

    private sealed class RequestIdEchoHandler : HttpMessageHandler
    {
        public string? LastIncomingRequestId { get; private set; }
        public string? LastOutgoingRequestId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bytes = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            var inEnv = ApiRequest.Parser.ParseFrom(bytes);
            LastIncomingRequestId = inEnv.RequestId;

            var outEnv = new ApiResponse
            {
                ProtocolVersion = EnvelopeCodec.ProtocolVersion,
                RequestId = inEnv.RequestId,
                Success = true,
                Compression = Compression.Identity,
                Payload = new Ack { Message = "ok" }.ToByteString(),
            };
            LastOutgoingRequestId = outEnv.RequestId;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(outEnv.ToByteArray())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue(DkcProtobufApiClient.ProtobufMediaType) },
                },
            };
        }
    }
}
