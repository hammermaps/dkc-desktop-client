using System.Buffers.Binary;
using System.IO.Compression;
using DkcDesktopClient.Core.Protocol;
using Google.Protobuf;
using K4os.Compression.LZ4;

namespace DkcDesktopClient.Core.Services;

/// <summary>
/// Encoder/decoder for the <see cref="ApiRequest"/>/<see cref="ApiResponse"/>
/// wire envelope. Handles payload compression (LZ4 / Gzip / Identity) inside
/// the envelope and exposes helpers for the matching HTTP Content-Encoding
/// header tokens.
///
/// Payload framing:
/// <list type="bullet">
///   <item><b>Identity</b>: payload bytes as-is.</item>
///   <item><b>LZ4 (dkc-lz4)</b>: <c>uint32 LE original-size</c> | LZ4 block.
///   The original-size header makes the format trivially portable to PHP
///   (no external frame parser required).</item>
///   <item><b>Gzip</b>: standard RFC 1952 gzip stream.</item>
/// </list>
/// </summary>
public static class EnvelopeCodec
{
    public const uint ProtocolVersion = 1;

    /// <summary>HTTP Content-Encoding token for the project's LZ4 format.</summary>
    public const string ContentEncodingLz4 = "dkc-lz4";

    /// <summary>HTTP Content-Encoding token for gzip.</summary>
    public const string ContentEncodingGzip = "gzip";

    /// <summary>HTTP Content-Encoding token for an uncompressed payload.</summary>
    public const string ContentEncodingIdentity = "identity";

    /// <summary>
    /// Payloads below this size are not worth compressing and are sent
    /// uncompressed regardless of caller preference.
    /// </summary>
    public const int MinCompressionPayloadBytes = 256;

    /// <summary>
    /// Build an <see cref="ApiRequest"/> envelope with the given action and
    /// (optional) Protobuf payload, applying the requested compression to the
    /// payload bytes only.
    /// </summary>
    public static ApiRequest BuildRequest(
        Protocol.Action action,
        IMessage? payload,
        AuthContext? auth,
        string requestId,
        CompressionPreference preference,
        out Compression compressionUsed)
    {
        var payloadBytes = payload?.ToByteArray() ?? Array.Empty<byte>();
        var (compressed, used) = CompressPayload(payloadBytes, preference);
        compressionUsed = used;

        var envelope = new ApiRequest
        {
            ProtocolVersion = ProtocolVersion,
            RequestId = requestId,
            Action = action,
            Compression = used,
            Payload = ByteString.CopyFrom(compressed),
        };

        if (auth != null)
            envelope.Auth = auth;

        return envelope;
    }

    /// <summary>
    /// Parse the response envelope bytes. Does NOT decompress the inner payload;
    /// use <see cref="DecompressPayload(byte[], Compression)"/> for that.
    /// </summary>
    public static ApiResponse DecodeResponse(byte[] envelopeBytes)
    {
        ArgumentNullException.ThrowIfNull(envelopeBytes);
        return ApiResponse.Parser.ParseFrom(envelopeBytes);
    }

    /// <summary>
    /// Decode the inner payload of a response envelope into a strongly-typed
    /// Protobuf message <typeparamref name="T"/>.
    /// </summary>
    public static T DecodePayload<T>(ApiResponse response) where T : IMessage<T>, new()
    {
        ArgumentNullException.ThrowIfNull(response);
        var raw = response.Payload?.ToByteArray() ?? Array.Empty<byte>();
        var decompressed = DecompressPayload(raw, response.Compression);
        var msg = new T();
        ((IMessage)msg).MergeFrom(decompressed);
        return msg;
    }

    /// <summary>
    /// Compress <paramref name="payload"/> according to <paramref name="preference"/>
    /// and skipping compression for trivially small payloads. Returns the compressed
    /// bytes together with the actual encoding that was applied.
    /// </summary>
    public static (byte[] Bytes, Compression Used) CompressPayload(
        byte[] payload,
        CompressionPreference preference)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Length < MinCompressionPayloadBytes || preference == CompressionPreference.Identity)
            return (payload, Compression.Identity);

        switch (preference)
        {
            case CompressionPreference.Lz4:
                return (EncodeLz4(payload), Compression.Lz4);
            case CompressionPreference.Gzip:
                return (EncodeGzip(payload), Compression.Gzip);
            default:
                return (payload, Compression.Identity);
        }
    }

    /// <summary>
    /// Decompress a payload according to the encoding indicated in the
    /// envelope. <see cref="Compression.Unspecified"/> is treated as identity.
    /// </summary>
    public static byte[] DecompressPayload(byte[] payload, Compression compression)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return compression switch
        {
            Compression.Unspecified or Compression.Identity => payload,
            Compression.Lz4 => DecodeLz4(payload),
            Compression.Gzip => DecodeGzip(payload),
            _ => throw new InvalidOperationException($"Unsupported compression: {compression}"),
        };
    }

    /// <summary>Map a <see cref="Compression"/> value to its HTTP Content-Encoding token.</summary>
    public static string ContentEncodingFor(Compression compression) => compression switch
    {
        Compression.Lz4 => ContentEncodingLz4,
        Compression.Gzip => ContentEncodingGzip,
        _ => ContentEncodingIdentity,
    };

    /// <summary>
    /// Parse a Content-Encoding header value into a <see cref="Compression"/>.
    /// Unknown / empty values resolve to <see cref="Compression.Identity"/>.
    /// </summary>
    public static Compression ParseContentEncoding(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
            return Compression.Identity;

        if (string.Equals(headerValue, ContentEncodingLz4, StringComparison.OrdinalIgnoreCase))
            return Compression.Lz4;

        if (string.Equals(headerValue, ContentEncodingGzip, StringComparison.OrdinalIgnoreCase))
            return Compression.Gzip;

        if (string.Equals(headerValue, ContentEncodingIdentity, StringComparison.OrdinalIgnoreCase))
            return Compression.Identity;

        return Compression.Identity;
    }

    // ─────────────────────────── compression internals ──────────────────────

    private static byte[] EncodeLz4(byte[] payload)
    {
        // Layout: [4-byte LE original size] [LZ4 block-compressed bytes]
        var maxCompressed = LZ4Codec.MaximumOutputSize(payload.Length);
        var buffer = new byte[4 + maxCompressed];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), (uint)payload.Length);
        var written = LZ4Codec.Encode(payload, 0, payload.Length, buffer, 4, maxCompressed);
        if (written <= 0)
            throw new InvalidOperationException("LZ4 compression failed");

        var result = new byte[4 + written];
        Buffer.BlockCopy(buffer, 0, result, 0, 4 + written);
        return result;
    }

    private static byte[] DecodeLz4(byte[] payload)
    {
        if (payload.Length < 4)
            throw new InvalidOperationException("LZ4 payload too short to contain length header");

        var originalSize = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
        // Guard against zip-bomb style attacks: cap inflated size at 64 MiB.
        const uint maxInflatedBytes = 64u * 1024u * 1024u;
        if (originalSize > maxInflatedBytes)
            throw new InvalidOperationException(
                $"LZ4 payload reports oversized output ({originalSize} bytes; max {maxInflatedBytes})");

        var output = new byte[originalSize];
        var decoded = LZ4Codec.Decode(payload, 4, payload.Length - 4, output, 0, output.Length);
        if (decoded != output.Length)
            throw new InvalidOperationException("LZ4 decompression produced unexpected length");
        return output;
    }

    private static byte[] EncodeGzip(byte[] payload)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(payload, 0, payload.Length);
        }
        return ms.ToArray();
    }

    private static byte[] DecodeGzip(byte[] payload)
    {
        // Guard against zip-bomb style attacks: cap inflated size at 64 MiB.
        const long maxInflatedBytes = 64L * 1024L * 1024L;
        using var ms = new MemoryStream(payload, writable: false);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        long total = 0;
        int read;
        while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxInflatedBytes)
                throw new InvalidOperationException(
                    $"Gzip payload exceeds inflated-size cap of {maxInflatedBytes} bytes");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
