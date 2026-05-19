using DkcDesktopClient.Core.Protocol;

namespace DkcDesktopClient.Core.Services;

/// <summary>
/// Preferred compression for outgoing Protobuf envelope payloads.
///
/// LZ4 is the preferred encoding. If the server signals it cannot accept LZ4
/// (or compression is forced to be disabled by the caller, e.g. for debugging),
/// the client falls back to Gzip, then Identity (no compression).
/// </summary>
public enum CompressionPreference
{
    /// <summary>Send uncompressed payloads. Useful for diagnostics.</summary>
    Identity = 0,

    /// <summary>Prefer LZ4 (dkc-lz4); fall back to Gzip then Identity.</summary>
    Lz4 = 1,

    /// <summary>Prefer Gzip; fall back to Identity. Used after LZ4 failure.</summary>
    Gzip = 2,
}

/// <summary>
/// Strongly-typed Protobuf API exception raised when the server returns a
/// failed <see cref="ApiResponse"/>. Maps <see cref="ApiError"/> 1:1.
/// </summary>
public sealed class DkcProtobufApiException : Exception
{
    public ErrorCode Code { get; }
    public IReadOnlyDictionary<string, string> Details { get; }
    public int RetryAfterSeconds { get; }
    public string RequestId { get; }

    internal DkcProtobufApiException(
        ErrorCode code,
        string message,
        IReadOnlyDictionary<string, string> details,
        int retryAfterSeconds,
        string requestId)
        : base(message)
    {
        Code = code;
        Details = details;
        RetryAfterSeconds = retryAfterSeconds;
        RequestId = requestId;
    }
}
