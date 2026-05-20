using DkcDesktopClient.Core.Protocol;
using DkcDesktopClient.Core.Protobuf;
using DkcDesktopClient.Core.Services;
using Google.Protobuf;
using Xunit;
using PbAction = DkcDesktopClient.Core.Protocol.Action;

namespace DkcDesktopClient.Tests;

public class EnvelopeCodecTests
{
    [Fact]
    public void BuildRequest_SetsProtocolVersionActionAndPayload()
    {
        var payload = new AuthLoginRequest { Username = "alice", Password = "x", TokenName = "t", TtlDays = 7 };

        var envelope = EnvelopeCodec.BuildRequest(
            PbAction.AuthLogin, payload, null, "req-123",
            CompressionPreference.Identity, out var compressionUsed);

        Assert.Equal(EnvelopeCodec.ProtocolVersion, envelope.ProtocolVersion);
        Assert.Equal(PbAction.AuthLogin, envelope.Action);
        Assert.Equal("req-123", envelope.RequestId);
        Assert.Equal(Compression.Identity, compressionUsed);

        // Inner payload round-trips through the bytes field.
        var roundtrip = AuthLoginRequest.Parser.ParseFrom(envelope.Payload);
        Assert.Equal("alice", roundtrip.Username);
        Assert.Equal(7, roundtrip.TtlDays);
    }

    [Fact]
    public void BuildRequest_WithAuth_PopulatesBearerToken()
    {
        var envelope = EnvelopeCodec.BuildRequest(
            PbAction.AuthStatus,
            null,
            new AuthContext { BearerToken = "dkc_abc" },
            "id",
            CompressionPreference.Identity,
            out _);

        Assert.NotNull(envelope.Auth);
        Assert.Equal("dkc_abc", envelope.Auth.BearerToken);
    }

    [Fact]
    public void BuildRequest_SmallPayload_IsNotCompressedEvenWhenLz4Preferred()
    {
        // Tiny payload below MinCompressionPayloadBytes – should be sent identity.
        var payload = new MmDetailRequest { Uid = "u" };
        EnvelopeCodec.BuildRequest(
            PbAction.MmDetail, payload, null, "id",
            CompressionPreference.Lz4, out var used);

        Assert.Equal(Compression.Identity, used);
    }

    [Fact]
    public void Compression_Lz4_RoundTrips()
    {
        var payload = MakePayload(2048);

        var (compressed, used) = EnvelopeCodec.CompressPayload(payload, CompressionPreference.Lz4);
        Assert.Equal(Compression.Lz4, used);
        Assert.NotEqual(payload.Length, compressed.Length);

        var roundtrip = EnvelopeCodec.DecompressPayload(compressed, Compression.Lz4);
        Assert.Equal(payload, roundtrip);
    }

    [Fact]
    public void Compression_Gzip_RoundTrips()
    {
        var payload = MakePayload(2048);

        var (compressed, used) = EnvelopeCodec.CompressPayload(payload, CompressionPreference.Gzip);
        Assert.Equal(Compression.Gzip, used);

        var roundtrip = EnvelopeCodec.DecompressPayload(compressed, Compression.Gzip);
        Assert.Equal(payload, roundtrip);
    }

    [Fact]
    public void Compression_Identity_ReturnsBytesUnchanged()
    {
        var payload = MakePayload(2048);
        var (compressed, used) = EnvelopeCodec.CompressPayload(payload, CompressionPreference.Identity);

        Assert.Equal(Compression.Identity, used);
        Assert.Same(payload, compressed);
    }

    [Fact]
    public void DecodePayload_DecompressesInnerMessage()
    {
        var inner = new MmListResponse
        {
            Page = new PageInfo { Total = 5, Limit = 50, Offset = 0 },
        };
        inner.Messages.Add(new MmMessage { Uid = "abc", Status = 1, Betreff = "x" });

        var innerBytes = inner.ToByteArray();
        var (compressed, used) = EnvelopeCodec.CompressPayload(innerBytes, CompressionPreference.Lz4);

        // The compressed payload is small here so it may not actually compress;
        // skip when below threshold by forcing the value through DecompressPayload.
        var response = new ApiResponse
        {
            ProtocolVersion = EnvelopeCodec.ProtocolVersion,
            Success = true,
            Compression = used,
            Payload = ByteString.CopyFrom(compressed),
        };

        var decoded = EnvelopeCodec.DecodePayload<MmListResponse>(response);
        Assert.Single(decoded.Messages);
        Assert.Equal("abc", decoded.Messages[0].Uid);
        Assert.Equal(5, decoded.Page.Total);
    }

    [Fact]
    public void DecompressPayload_LZ4_RejectsPayloadShorterThanLengthHeader()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EnvelopeCodec.DecompressPayload(new byte[] { 0x01, 0x02 }, Compression.Lz4));
    }

    [Fact]
    public void DecompressPayload_LZ4_RejectsClaimedSizeAboveSafetyCap()
    {
        // Original-size header claims >64 MiB.
        var payload = new byte[]
        {
            0x00, 0x00, 0x00, 0x10, // 256 MiB little-endian
        };
        Assert.Throws<InvalidOperationException>(() =>
            EnvelopeCodec.DecompressPayload(payload, Compression.Lz4));
    }

    [Theory]
    [InlineData("dkc-lz4", Compression.Lz4)]
    [InlineData("DKC-LZ4", Compression.Lz4)]
    [InlineData("gzip", Compression.Gzip)]
    [InlineData("identity", Compression.Identity)]
    [InlineData("", Compression.Identity)]
    [InlineData(null, Compression.Identity)]
    [InlineData("brotli", Compression.Identity)]
    public void ParseContentEncoding_MatchesExpectedValue(string? header, Compression expected)
    {
        Assert.Equal(expected, EnvelopeCodec.ParseContentEncoding(header));
    }

    [Theory]
    [InlineData(Compression.Lz4, "dkc-lz4")]
    [InlineData(Compression.Gzip, "gzip")]
    [InlineData(Compression.Identity, "identity")]
    [InlineData(Compression.Unspecified, "identity")]
    public void ContentEncodingFor_RoundTrips(Compression compression, string expected)
    {
        Assert.Equal(expected, EnvelopeCodec.ContentEncodingFor(compression));
    }

    private static byte[] MakePayload(int size)
    {
        var payload = new byte[size];
        // Repeating pattern compresses well so we can also assert the
        // compressed length is smaller than the original.
        for (var i = 0; i < size; i++)
            payload[i] = (byte)(i % 32);
        return payload;
    }
}
