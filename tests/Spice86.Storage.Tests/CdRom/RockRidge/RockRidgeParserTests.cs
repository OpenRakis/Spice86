namespace Spice86.Storage.Tests.CdRom.RockRidge;

using FluentAssertions;

using Spice86.Shared.Emulator.Storage.CdRom.RockRidge;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

using Xunit;

/// <summary>
/// Behavioural tests for <see cref="SuspParser"/> and <see cref="RockRidgeParser"/>.
/// </summary>
public sealed class RockRidgeParserTests
{
    private const byte NameFlagContinue = 0x01;
    private const byte NameFlagCurrent = 0x02;

    /// <summary>Parsing an empty system use area yields no entries.</summary>
    [Fact]
    public void SuspParser_EmptyBuffer_ReturnsEmpty()
    {
        // Arrange
        SuspParser parser = new();

        // Act
        IReadOnlyList<SuspEntry> result = parser.Parse(ReadOnlySpan<byte>.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>A single SP entry is parsed with the expected signature, version, and payload.</summary>
    [Fact]
    public void SuspParser_SingleSpEntry_PreservesHeaderAndPayload()
    {
        // Arrange
        byte[] buffer = SuspBuilder.Begin()
            .AddEntry("SP", version: 1, new byte[] { 0xBE, 0xEF, 0x00 })
            .Build();
        SuspParser parser = new();

        // Act
        IReadOnlyList<SuspEntry> result = parser.Parse(buffer);

        // Assert
        result.Should().HaveCount(1);
        result[0].Signature.Should().Be("SP");
        result[0].Version.Should().Be(1);
        result[0].Payload.Should().BeEquivalentTo(new byte[] { 0xBE, 0xEF, 0x00 });
    }

    /// <summary>Parsing stops at the canonical "ST" terminator: trailing bytes are ignored.</summary>
    [Fact]
    public void SuspParser_StopsAtStTerminator()
    {
        // Arrange
        byte[] buffer = SuspBuilder.Begin()
            .AddEntry("SP", version: 1, new byte[] { 0xBE })
            .AddEntry("ST", version: 1, Array.Empty<byte>())
            .AddEntry("NM", version: 1, new byte[] { 0x00, (byte)'A' })
            .Build();
        SuspParser parser = new();

        // Act
        IReadOnlyList<SuspEntry> result = parser.Parse(buffer);

        // Assert
        result.Should().HaveCount(2);
        result[0].Signature.Should().Be("SP");
        result[1].Signature.Should().Be("ST");
    }

    /// <summary>A length byte below the 4-byte header size aborts parsing gracefully.</summary>
    [Fact]
    public void SuspParser_MalformedLength_StopsGracefully()
    {
        // Arrange
        byte[] buffer = { (byte)'N', (byte)'M', 0x02 /* < 4 */, 0x01 };
        SuspParser parser = new();

        // Act
        IReadOnlyList<SuspEntry> result = parser.Parse(buffer);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>A length byte that overruns the buffer aborts parsing gracefully.</summary>
    [Fact]
    public void SuspParser_TruncatedEntry_StopsGracefully()
    {
        // Arrange: declares length 10 but only 5 bytes of payload follow the header
        byte[] buffer = { (byte)'N', (byte)'M', 0x14, 0x01, 0x00, (byte)'A', (byte)'B' };
        SuspParser parser = new();

        // Act
        IReadOnlyList<SuspEntry> result = parser.Parse(buffer);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>A system use area with no NM/PX entries yields metadata with all fields null.</summary>
    [Fact]
    public void RockRidgeParser_NoNmOrPx_ReturnsEmptyMetadata()
    {
        // Arrange
        byte[] buffer = SuspBuilder.Begin()
            .AddEntry("SP", version: 1, new byte[] { 0xBE, 0xEF, 0x00 })
            .AddEntry("ST", version: 1, Array.Empty<byte>())
            .Build();
        RockRidgeParser parser = new();

        // Act
        RockRidgeMetadata metadata = parser.Parse(buffer);

        // Assert
        metadata.HasAny.Should().BeFalse();
        metadata.AlternateName.Should().BeNull();
        metadata.PosixFileMode.Should().BeNull();
        metadata.FileLinkCount.Should().BeNull();
        metadata.UserId.Should().BeNull();
        metadata.GroupId.Should().BeNull();
    }

    /// <summary>A single NM entry with flags 0 exposes its payload as the alternate name.</summary>
    [Fact]
    public void RockRidgeParser_SingleNmEntry_ExtractsAlternateName()
    {
        // Arrange
        byte[] payload = BuildNmPayload(flags: 0, "readme.txt");
        byte[] buffer = SuspBuilder.Begin()
            .AddEntry("NM", version: 1, payload)
            .Build();
        RockRidgeParser parser = new();

        // Act
        RockRidgeMetadata metadata = parser.Parse(buffer);

        // Assert
        metadata.AlternateName.Should().Be("readme.txt");
        metadata.HasAny.Should().BeTrue();
    }

    /// <summary>Two NM entries flagged CONTINUE concatenate into a single alternate name.</summary>
    [Fact]
    public void RockRidgeParser_TwoNmEntriesWithContinueFlag_AreConcatenated()
    {
        // Arrange
        byte[] firstHalf = BuildNmPayload(flags: NameFlagContinue, "long-");
        byte[] secondHalf = BuildNmPayload(flags: 0, "filename");
        byte[] buffer = SuspBuilder.Begin()
            .AddEntry("NM", version: 1, firstHalf)
            .AddEntry("NM", version: 1, secondHalf)
            .Build();
        RockRidgeParser parser = new();

        // Act
        RockRidgeMetadata metadata = parser.Parse(buffer);

        // Assert
        metadata.AlternateName.Should().Be("long-filename");
    }

    /// <summary>An NM entry with the CURRENT flag (".") is ignored even if name bytes follow.</summary>
    [Fact]
    public void RockRidgeParser_NmWithCurrentFlag_IsIgnored()
    {
        // Arrange
        byte[] payload = BuildNmPayload(flags: NameFlagCurrent, "x");
        byte[] buffer = SuspBuilder.Begin()
            .AddEntry("NM", version: 1, payload)
            .Build();
        RockRidgeParser parser = new();

        // Act
        RockRidgeMetadata metadata = parser.Parse(buffer);

        // Assert
        metadata.AlternateName.Should().BeNull();
    }

    /// <summary>A 32-byte PX entry yields mode/links/uid/gid from its 4 BB-encoded fields.</summary>
    [Fact]
    public void RockRidgeParser_PxEntry_ExtractsPosixAttributes()
    {
        // Arrange
        byte[] payload = BuildPxPayload(mode: 0x000081A4u /* 0o100644 */, links: 1u, uid: 1000u, gid: 100u);
        byte[] buffer = SuspBuilder.Begin()
            .AddEntry("PX", version: 1, payload)
            .Build();
        RockRidgeParser parser = new();

        // Act
        RockRidgeMetadata metadata = parser.Parse(buffer);

        // Assert
        metadata.PosixFileMode.Should().Be(0x000081A4u);
        metadata.FileLinkCount.Should().Be(1u);
        metadata.UserId.Should().Be(1000u);
        metadata.GroupId.Should().Be(100u);
    }

    /// <summary>A PX payload that is shorter than 32 bytes leaves all POSIX fields null.</summary>
    [Fact]
    public void RockRidgeParser_PxPayloadTooShort_LeavesAttributesNull()
    {
        // Arrange
        byte[] truncated = new byte[16];
        byte[] buffer = SuspBuilder.Begin()
            .AddEntry("PX", version: 1, truncated)
            .Build();
        RockRidgeParser parser = new();

        // Act
        RockRidgeMetadata metadata = parser.Parse(buffer);

        // Assert
        metadata.PosixFileMode.Should().BeNull();
        metadata.FileLinkCount.Should().BeNull();
        metadata.UserId.Should().BeNull();
        metadata.GroupId.Should().BeNull();
    }

    /// <summary>NM and PX in the same system use area both populate the metadata.</summary>
    [Fact]
    public void RockRidgeParser_NmAndPxTogether_PopulatesBothNameAndAttributes()
    {
        // Arrange
        byte[] nmPayload = BuildNmPayload(flags: 0, "report.doc");
        byte[] pxPayload = BuildPxPayload(mode: 0x000081B6u, links: 1u, uid: 0u, gid: 0u);
        byte[] buffer = SuspBuilder.Begin()
            .AddEntry("PX", version: 1, pxPayload)
            .AddEntry("NM", version: 1, nmPayload)
            .AddEntry("ST", version: 1, Array.Empty<byte>())
            .Build();
        RockRidgeParser parser = new();

        // Act
        RockRidgeMetadata metadata = parser.Parse(buffer);

        // Assert
        metadata.AlternateName.Should().Be("report.doc");
        metadata.PosixFileMode.Should().Be(0x000081B6u);
    }

    private static byte[] BuildNmPayload(byte flags, string name)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        byte[] payload = new byte[1 + nameBytes.Length];
        payload[0] = flags;
        Buffer.BlockCopy(nameBytes, 0, payload, 1, nameBytes.Length);
        return payload;
    }

    private static byte[] BuildPxPayload(uint mode, uint links, uint uid, uint gid)
    {
        byte[] payload = new byte[32];
        WriteBb(payload, 0, mode);
        WriteBb(payload, 8, links);
        WriteBb(payload, 16, uid);
        WriteBb(payload, 24, gid);
        return payload;
    }

    private static void WriteBb(byte[] buffer, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset + 4, 4), value);
    }

    private sealed class SuspBuilder
    {
        private readonly List<byte> _bytes = new();

        public static SuspBuilder Begin() => new();

        public SuspBuilder AddEntry(string signature, byte version, byte[] payload)
        {
            int totalLength = 4 + payload.Length;
            _bytes.Add((byte)signature[0]);
            _bytes.Add((byte)signature[1]);
            _bytes.Add((byte)totalLength);
            _bytes.Add(version);
            _bytes.AddRange(payload);
            return this;
        }

        public byte[] Build() => _bytes.ToArray();
    }
}
