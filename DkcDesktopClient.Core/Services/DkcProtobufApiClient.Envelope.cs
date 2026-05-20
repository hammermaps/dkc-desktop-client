using System.Net;
using System.Net.Http.Headers;
using DkcDesktopClient.Core.Protocol;
using Google.Protobuf;

namespace DkcDesktopClient.Core.Services;

public sealed partial class DkcProtobufApiClient
{
    /// <summary>HTTP Content-Encoding values the client accepts from the server.</summary>
    public const string AcceptEncodingValue = EnvelopeCodec.ContentEncodingLz4 + ", " + EnvelopeCodec.ContentEncodingGzip;

    /// <summary>
    /// Send a typed request envelope and parse the response into
    /// <typeparamref name="TResponse"/>. The action's response payload is
    /// decompressed using the encoding signalled in the response envelope.
    ///
    /// On a failed envelope (<see cref="ApiResponse.Success"/> == false) a
    /// <see cref="DkcProtobufApiException"/> is thrown carrying the
    /// <see cref="ApiError"/> fields. The same exception is raised for
    /// transport errors (non-2xx HTTP status without parseable envelope).
    /// </summary>
    public async Task<TResponse> SendAsync<TResponse>(
        Protocol.Action action,
        IMessage? requestPayload,
        CompressionPreference compression = CompressionPreference.Lz4,
        CancellationToken ct = default)
        where TResponse : IMessage<TResponse>, new()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var requestId = Guid.NewGuid().ToString("N");
        var auth = BuildAuthContext();
        var token = auth?.BearerToken;

        // Build envelope and attempt with the requested compression; on
        // UNSUPPORTED_COMPRESSION transparently retry once with the next
        // preference in the chain (Lz4 → Gzip → Identity).
        var preferenceChain = BuildPreferenceChain(compression);
        DkcProtobufApiException? lastError = null;

        foreach (var attempt in preferenceChain)
        {
            var envelope = EnvelopeCodec.BuildRequest(
                action, requestPayload, auth, requestId, attempt, out var used);

            HttpStatusCode status;
            byte[] responseBytes;
            try
            {
                (status, responseBytes) = await PostEnvelopeAsync(envelope.ToByteArray(), used, requestId, token, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new DkcProtobufApiException(
                    ErrorCode.ServiceUnavailable,
                    $"HTTP transport error: {ex.Message}",
                    new Dictionary<string, string>(), 0, requestId);
            }

            ApiResponse response;
            try
            {
                response = EnvelopeCodec.DecodeResponse(responseBytes);
            }
            catch (InvalidProtocolBufferException ex)
            {
                throw new DkcProtobufApiException(
                    ErrorCode.ServiceUnavailable,
                    $"Server returned non-protobuf response (HTTP {(int)status}): {ex.Message}",
                    new Dictionary<string, string>(), 0, requestId);
            }

            if (response.Success)
                return EnvelopeCodec.DecodePayload<TResponse>(response);

            var apiError = response.Error ?? new ApiError { Code = ErrorCode.InternalError, Message = "unknown error" };

            // Fall back to the next compression option if the server cannot
            // decode the current one.
            if (apiError.Code == ErrorCode.UnsupportedCompression && attempt != preferenceChain[^1])
            {
                lastError = ToException(apiError, response.RequestId);
                continue;
            }

            // For 401/403/etc. the HTTP status was non-2xx but PostEnvelopeAsync
            // already accepted the envelope body; we still surface the error
            // through the exception so callers see structured information.
            _ = status;
            throw ToException(apiError, response.RequestId);
        }

        throw lastError ?? new DkcProtobufApiException(
            ErrorCode.UnsupportedCompression,
            "Server did not accept any supported compression",
            new Dictionary<string, string>(), 0, requestId);
    }

    private static DkcProtobufApiException ToException(ApiError apiError, string requestId)
    {
        var details = new Dictionary<string, string>(apiError.Details ?? new Google.Protobuf.Collections.MapField<string, string>());
        return new DkcProtobufApiException(
            apiError.Code,
            apiError.Message ?? string.Empty,
            details,
            apiError.RetryAfterSeconds,
            requestId);
    }

    private static CompressionPreference[] BuildPreferenceChain(CompressionPreference initial) => initial switch
    {
        CompressionPreference.Lz4 => new[] { CompressionPreference.Lz4, CompressionPreference.Gzip, CompressionPreference.Identity },
        CompressionPreference.Gzip => new[] { CompressionPreference.Gzip, CompressionPreference.Identity },
        _ => new[] { CompressionPreference.Identity },
    };

    private AuthContext? BuildAuthContext()
    {
        var token = _tokenProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(token))
            return null;
        return new AuthContext { BearerToken = token };
    }

    private async Task<(HttpStatusCode Status, byte[] Body)> PostEnvelopeAsync(
        byte[] envelopeBytes,
        Compression compression,
        string requestId,
        string? bearerToken,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointPath)
        {
            Content = new ByteArrayContent(envelopeBytes),
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ProtobufMediaType));
        request.Headers.TryAddWithoutValidation(ProtocolHeaderName, ProtocolHeaderValue);
        request.Headers.TryAddWithoutValidation("Accept-Encoding", AcceptEncodingValue);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ProtobufMediaType);
        // Mirror the envelope's compression field as a Content-Encoding header
        // (informational; the envelope's own `compression` field is authoritative).
        request.Content.Headers.TryAddWithoutValidation(
            "Content-Encoding", EnvelopeCodec.ContentEncodingFor(compression));

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        // The server is expected to always return a protobuf-encoded ApiResponse
        // envelope, even on error. If it does not (length-0 / wrong content type),
        // surface the HTTP status as a transport-level exception.
        if (body.Length == 0)
        {
            throw new DkcProtobufApiException(
                ErrorCode.ServiceUnavailable,
                $"Empty response from server (HTTP {(int)response.StatusCode})",
                new Dictionary<string, string>(), 0, requestId);
        }

        return (response.StatusCode, body);
    }
}
