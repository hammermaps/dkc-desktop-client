using System.Net.Http.Headers;

namespace DkcDesktopClient.Core.Services;

public sealed class DkcProtobufApiClient
{
    public const string EndpointPath = "/api.php";
    public const string ProtocolHeaderName = "X-DKC-Protocol";
    public const string ProtocolHeaderValue = "protobuf";
    public const string ProtobufMediaType = "application/x-protobuf";
    public const string UserAgent = "DkcDesktopClient/1.0 (Avalonia; .NET8; protobuf)";

    private readonly HttpClient _httpClient;
    private readonly Func<string?>? _tokenProvider;

    public DkcProtobufApiClient(HttpClient httpClient, Func<string?>? tokenProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider;
    }

    public async Task<byte[]> SendAsync(byte[] protobufPayload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(protobufPayload);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointPath)
        {
            Content = new ByteArrayContent(protobufPayload)
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ProtobufMediaType));
        request.Headers.TryAddWithoutValidation(ProtocolHeaderName, ProtocolHeaderValue);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        var token = _tokenProvider?.Invoke();
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ProtobufMediaType);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
