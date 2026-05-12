namespace Spice86.DebuggerKnowledgeBase.Dos;

using System.Collections.Generic;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;

/// <summary>
/// Decodes DOS INT 24h: Critical Error Handler. Called by DOS when a hardware/disk error
/// occurs. AH conveys the error class flags, AL the failing drive (for disk errors), DI's low
/// byte the extended error code, and BP:SI a pointer to the failing device's driver header.
/// The handler returns a recovery action in AL: 00=Ignore, 01=Retry, 02=Abort, 03=Fail.
/// </summary>
public sealed class DosInt24Decoder : IInterruptDecoder {
    private const string Subsystem = "DOS INT 24h";

    /// <inheritdoc />
    public bool CanDecode(byte vector) {
        return vector == 0x24;
    }

    /// <inheritdoc />
    public DecodedCall Decode(byte vector, State state, IMemory memory) {
        byte ah = state.AH;
        byte al = state.AL;
        byte errorCode = (byte)(state.DI & 0xFF);
        bool isDiskError = (ah & 0x80) == 0;
        string flags = DescribeAhFlags(ah);
        List<DecodedParameter> parameters = [
            new DecodedParameter(
                "error flags",
                "AH",
                DecodedParameterKind.Register,
                ah,
                $"0x{ah:X2} ({flags})",
                null)
        ];
        if (isDiskError) {
            parameters.Add(new DecodedParameter(
                "drive",
                "AL",
                DecodedParameterKind.Register,
                al,
                $"{(char)('A' + al)}: (0x{al:X2})",
                "Drive number for the disk error (0=A, 1=B, ...)."));
        }
        parameters.Add(new DecodedParameter(
            "extended error code",
            "DI (low)",
            DecodedParameterKind.Register,
            errorCode,
            $"0x{errorCode:X2} ({DescribeErrorCode(errorCode)})",
            null));
        parameters.Add(new DecodedParameter(
            "device driver header",
            "BP:SI",
            DecodedParameterKind.Register,
            ((long)state.BP << 16) | state.SI,
            $"{state.BP:X4}:{state.SI:X4}",
            "Far pointer to the failing device's driver header."));
        return new DecodedCall(
            Subsystem,
            "Critical Error Handler",
            "Hardware or disk I/O error; return recovery action in AL (0=Ignore, 1=Retry, 2=Abort, 3=Fail).",
            parameters,
            []);
    }

    private static string DescribeAhFlags(byte ah) {
        List<string> parts = [];
        if ((ah & 0x80) != 0) {
            parts.Add("non-disk error");
        } else {
            parts.Add("disk error");
            parts.Add((ah & 0x01) == 0 ? "read" : "write");
            int area = (ah >> 1) & 0x03;
            parts.Add(area switch {
                0 => "DOS area",
                1 => "FAT",
                2 => "directory",
                _ => "data area"
            });
            if ((ah & 0x08) != 0) {
                parts.Add("Ignore allowed");
            }
            if ((ah & 0x10) != 0) {
                parts.Add("Retry allowed");
            }
            if ((ah & 0x20) != 0) {
                parts.Add("Fail allowed");
            }
        }
        return string.Join(", ", parts);
    }

    private static string DescribeErrorCode(byte code) {
        return code switch {
            0x00 => "write protect",
            0x01 => "unknown unit",
            0x02 => "drive not ready",
            0x03 => "unknown command",
            0x04 => "CRC error",
            0x05 => "bad request structure length",
            0x06 => "seek error",
            0x07 => "unknown media type",
            0x08 => "sector not found",
            0x09 => "printer out of paper",
            0x0A => "write fault",
            0x0B => "read fault",
            0x0C => "general failure",
            _ => "unknown"
        };
    }
}
