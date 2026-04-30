namespace Spice86.Tests.CpuTests.SingleStepTests;

/// <summary>
/// Filters out test cases whose instruction bytes encode one of the three
/// undefined-behavior rows of the SIB table: index field == 100b (no index)
/// combined with a non-zero scale. These encodings are intentionally included
/// in the SingleStepTests data, but the documented 386 behavior is undefined
/// and the JSON's convenience "ea" field is wrong for them.
///
/// Rather than emulating the UB per-opcode, we skip these cases.
/// </summary>
internal static class InvalidSibFilter {
    private const byte LegacyPrefix26 = 0x26;
    private const byte LegacyPrefix2E = 0x2E;
    private const byte LegacyPrefix36 = 0x36;
    private const byte LegacyPrefix3E = 0x3E;
    private const byte LegacyPrefix64 = 0x64;
    private const byte LegacyPrefix65 = 0x65;
    private const byte LegacyPrefixOpSize66 = 0x66;
    private const byte LegacyPrefixAddrSize67 = 0x67;
    private const byte LegacyPrefixLockF0 = 0xF0;
    private const byte LegacyPrefixRepF2 = 0xF2;
    private const byte LegacyPrefixRepF3 = 0xF3;

    private const byte TwoByteOpcodeEscape0F = 0x0F;

    private static readonly bool[] OneByteHasModRm = BuildOneByteModRmTable();
    private static readonly bool[] TwoByteHasModRm = BuildTwoByteModRmTable();

    /// <summary>
    /// Returns true if the encoded instruction contains a SIB byte that falls
    /// in one of the three invalid SIB rows (index == ESP with scale != 1).
    /// Returns false for any malformed/short instruction or any encoding that
    /// does not reach a SIB byte (16-bit addressing, register-form ModRM, etc.).
    /// </summary>
    public static bool IsInvalidSibEncoding(byte[] bytes) {
        int position = 0;
        bool hasAddressSizeOverride = false;

        while (position < bytes.Length && IsLegacyPrefix(bytes[position])) {
            if (bytes[position] == LegacyPrefixAddrSize67) {
                hasAddressSizeOverride = true;
            }
            position++;
        }

        // SIB is a 32-bit-addressing-only feature. In real-mode tests, only the
        // 0x67 prefix selects 32-bit addressing, so without it there is no SIB.
        if (!hasAddressSizeOverride) {
            return false;
        }

        if (position >= bytes.Length) {
            return false;
        }

        bool hasModRm;
        byte opcode = bytes[position];
        if (opcode == TwoByteOpcodeEscape0F) {
            position++;
            if (position >= bytes.Length) {
                return false;
            }
            byte secondOpcode = bytes[position];
            hasModRm = TwoByteHasModRm[secondOpcode];
        } else {
            hasModRm = OneByteHasModRm[opcode];
        }
        position++;

        if (!hasModRm) {
            return false;
        }

        if (position >= bytes.Length) {
            return false;
        }

        byte modRm = bytes[position];
        position++;

        int mod = (modRm >> 6) & 0x3;
        int rm = modRm & 0x7;

        // Register-form (mod == 11) has no SIB. SIB only present when mod != 11 and rm == 100.
        if (mod == 0x3 || rm != 0x4) {
            return false;
        }

        if (position >= bytes.Length) {
            return false;
        }

        byte sib = bytes[position];
        int scale = (sib >> 6) & 0x3;
        int index = (sib >> 3) & 0x7;

        // The three invalid SIB rows: index == 100b (no index) with scale != 0.
        return index == 0x4 && scale != 0;
    }

    private static bool IsLegacyPrefix(byte value) {
        return value == LegacyPrefix26
            || value == LegacyPrefix2E
            || value == LegacyPrefix36
            || value == LegacyPrefix3E
            || value == LegacyPrefix64
            || value == LegacyPrefix65
            || value == LegacyPrefixOpSize66
            || value == LegacyPrefixAddrSize67
            || value == LegacyPrefixLockF0
            || value == LegacyPrefixRepF2
            || value == LegacyPrefixRepF3;
    }

    private static bool[] BuildOneByteModRmTable() {
        bool[] table = new bool[256];
        // Arithmetic groups: ADD, OR, ADC, SBB, AND, SUB, XOR, CMP (00-3B, mr/rm forms)
        int[] arithBases = { 0x00, 0x08, 0x10, 0x18, 0x20, 0x28, 0x30, 0x38 };
        foreach (int b in arithBases) {
            table[b + 0] = true;
            table[b + 1] = true;
            table[b + 2] = true;
            table[b + 3] = true;
        }
        // BOUND, ARPL, IMUL imm, IMUL imm8
        table[0x62] = true;
        table[0x63] = true;
        table[0x69] = true;
        table[0x6B] = true;
        // Immediate groups, TEST, XCHG
        table[0x80] = true;
        table[0x81] = true;
        table[0x82] = true;
        table[0x83] = true;
        table[0x84] = true;
        table[0x85] = true;
        table[0x86] = true;
        table[0x87] = true;
        // MOV r/m, MOV Sreg, LEA, MOV Sreg, group POP r/m
        table[0x88] = true;
        table[0x89] = true;
        table[0x8A] = true;
        table[0x8B] = true;
        table[0x8C] = true;
        table[0x8D] = true;
        table[0x8E] = true;
        table[0x8F] = true;
        // Shift groups, LES, LDS, MOV imm to r/m
        table[0xC0] = true;
        table[0xC1] = true;
        table[0xC4] = true;
        table[0xC5] = true;
        table[0xC6] = true;
        table[0xC7] = true;
        table[0xD0] = true;
        table[0xD1] = true;
        table[0xD2] = true;
        table[0xD3] = true;
        // FPU escapes (not in 386 SingleStepTests data, but harmless to mark)
        table[0xD8] = true;
        table[0xD9] = true;
        table[0xDA] = true;
        table[0xDB] = true;
        table[0xDC] = true;
        table[0xDD] = true;
        table[0xDE] = true;
        table[0xDF] = true;
        // Unary groups, INC/DEC group
        table[0xF6] = true;
        table[0xF7] = true;
        table[0xFE] = true;
        table[0xFF] = true;
        return table;
    }

    private static bool[] BuildTwoByteModRmTable() {
        bool[] table = new bool[256];
        // 0F00..0F03: SLDT/STR/LLDT/LTR group, SGDT/SIDT/LGDT/LIDT group, LAR, LSL
        table[0x00] = true;
        table[0x01] = true;
        table[0x02] = true;
        table[0x03] = true;
        // 0F20..0F23: MOV to/from CR/DR
        table[0x20] = true;
        table[0x21] = true;
        table[0x22] = true;
        table[0x23] = true;
        // 0F90..0F9F: SETcc r/m8
        for (int i = 0x90; i <= 0x9F; i++) {
            table[i] = true;
        }
        // 0FA3 BT, 0FA4 SHLD imm, 0FA5 SHLD CL, 0FAB BTS, 0FAC SHRD imm, 0FAD SHRD CL, 0FAF IMUL
        table[0xA3] = true;
        table[0xA4] = true;
        table[0xA5] = true;
        table[0xAB] = true;
        table[0xAC] = true;
        table[0xAD] = true;
        table[0xAF] = true;
        // 0FB2 LSS, 0FB3 BTR, 0FB4 LFS, 0FB5 LGS, 0FB6/B7 MOVZX, 0FBA group BT*, 0FBB BTC,
        // 0FBC BSF, 0FBD BSR, 0FBE/BF MOVSX
        table[0xB2] = true;
        table[0xB3] = true;
        table[0xB4] = true;
        table[0xB5] = true;
        table[0xB6] = true;
        table[0xB7] = true;
        table[0xBA] = true;
        table[0xBB] = true;
        table[0xBC] = true;
        table[0xBD] = true;
        table[0xBE] = true;
        table[0xBF] = true;
        return table;
    }
}
