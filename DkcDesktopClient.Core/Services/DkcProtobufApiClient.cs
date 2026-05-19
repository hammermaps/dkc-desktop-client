using System.Net.Http.Headers;

namespace DkcDesktopClient.Core.Services;

public sealed class DkcProtobufApiClient : IDisposable
{
    public const string EndpointPath = "/api.php";
    public const string ProtocolHeaderName = "X-DKC-Protocol";
    public const string ProtocolHeaderValue = "protobuf";
    public const string ProtobufMediaType = "application/x-protobuf";
    public static string UserAgent { get; } = CreateUserAgent();

    private readonly HttpClient _httpClient;
    private readonly Func<string?>? _tokenProvider;
    private readonly bool _disposeHttpClient;
    private bool _disposed;

    public DkcProtobufApiClient(
        HttpClient httpClient,
        Func<string?>? tokenProvider = null,
        bool disposeHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider;
        _disposeHttpClient = disposeHttpClient;
    }

    public async Task<byte[]> SendAsync(byte[] protobufPayload, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_disposeHttpClient)
            _httpClient.Dispose();

        _disposed = true;
    }

    private static string CreateUserAgent()
    {
        var version = typeof(DkcProtobufApiClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return $"DkcDesktopClient/{version} (Avalonia; .NET {Environment.Version.ToString(3)}; protobuf)";
    }
}
