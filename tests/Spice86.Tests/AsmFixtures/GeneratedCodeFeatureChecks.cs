namespace Spice86.Tests.AsmFixtures;

using static Spice86.Tests.AsmFixtures.GeneratedCodeCheckKind;

/// <summary>The program-only lines each scenario's generated source must keep, one group per
/// generated-source golden key. Each line names the generator feature the scenario exists for,
/// or a feature a generator commit of this branch introduced.</summary>
internal static class GeneratedCodeFeatureChecks {
    public static readonly GeneratedCodeCheck[] AluDeadFlags = [
        new(Contains, "flag1 = (AX & 1) != 0;"),
        new(Contains, "SsUInt32[0x0600] = (uint)SP | (uint)SS << 16;")
    ];

    public static readonly GeneratedCodeCheck[] DataStrings = [
        new(Contains, "// DOS 09h print \"HELLO$\""),
        new(Contains, "InterruptCall(cs1, 0x0111, 0x21)"),
        new(Contains, "Dos.WriteFile("),
        new(Contains, "// DOS 40h write handle 1 \""),
        new(Contains, "Dos.OpenFile(0, Dos.StringDecoder.GetDosString(DS, 0x0179, '\\0', \"NOFILE.TXT\"))"),
        new(DoesNotContain, "// DOS 3Dh")
    ];

    public static readonly GeneratedCodeCheck[] IdiomDeadStore = [
        new(Contains, "Segment2000Globals.Word0020 = 0x2222;"),
        new(DoesNotContain, "Word0020 = 0x1111")
    ];

    public static readonly GeneratedCodeCheck[] IdiomDiv16Named = [
        new(Contains, "(AX, DX) = Div16(divisor: 3, dividend: 0x00020000);")
    ];

    public static readonly GeneratedCodeCheck[] IdiomWideValues = [
        new(Contains, "AX = DsGlobals.Word0610;"),
        new(Contains, "EAX = DsGlobals.Dword0612 >> 16;")
    ];

    public static readonly GeneratedCodeCheck[] IdiomFlagIdioms = [
        new(Contains, "DsGlobals.Byte0601 = 1;"),
        new(Contains, "if (word1 > 5) {")
    ];

    public static readonly GeneratedCodeCheck[] IdiomLocalsNaming = [
        new(Contains, "UInt8[DS, index + 0x0610] = 0xAA;"),
        new(Contains, "index++;")
    ];

    public static readonly GeneratedCodeCheck[] IdiomTypedSignature = [
        new(Contains, "(byte result, byte result2) unknown_F000_0013_F0013(ushort word1)"),
        new(Contains, "(result, result2) = NearCall(biosSegment, 0x000B, () => unknown_F000_0013_F0013(5));")
    ];

    public static readonly GeneratedCodeCheck[] IdiomStackSaveRestore = [
        new(Contains, "Stack.Push16(0x1111);"),
        new(Contains, "Stack.Release(1);")
    ];

    public static readonly GeneratedCodeCheck[] IdiomCallSites = [
        new(Contains, "AL = CallWithStackArguments(biosSegment, 0x000A, () => unknown_F000_001D_F001D((ushort)0x0042), (ushort)0x0042);"),
        new(Contains, "result = NearCall32(biosSegment, 0x0019, () => unknown_F000_0029_F0029());")
    ];

    public static readonly GeneratedCodeCheck[] IdiomContracts = [
        new(Contains, "/// <summary>Inputs: none. Outputs: AL. Changes: AL, flags, DF.</summary>"),
        new(Contains, "InterruptCall(biosSegment, 0x001D, 0x80,")
    ];

    public static readonly GeneratedCodeCheck[] IdiomJumpTableSwitch = [
        new(Contains, "switch (UInt16[CS, BX + 0x2F]) {"),
        new(Contains, "case 0x001A:")
    ];

    public static readonly GeneratedCodeCheck[] IdiomFailAsUntested = [
        new(Contains, "throw FailAsUntested(\"Untested indirect jump at F000:0019\");"),
        new(Contains, "VerifySpeculativeEntryOrFail(biosSegment, 0x001B")
    ];

    public static readonly GeneratedCodeCheck[] IdiomMemoryAccessors = [
        new(Contains, "UInt8[DS, index + 2] = 0x42;"),
        new(Contains, "AX = DsGlobals.Word0610;")
    ];

    public static readonly GeneratedCodeCheck[] IdiomStackFrames = [
        new(Contains, "unknown_F000_000F_F000FFrame frame = new(Memory, State.SegmentRegisters, SP);"),
        new(Contains, "frame.Local4 = word1;")
    ];

    public static readonly GeneratedCodeCheck[] IdiomFaultDispatch = [
        new(Contains, "catch (CpuException cpuException) {"),
        new(Contains, "DispatchCpuFault(cpuException")
    ];

    public static readonly GeneratedCodeCheck[] IdiomStrictStack = [
        new(Contains, "// Relaxations: none"),
        new(Contains, "AL = CallWithStackArguments(biosSegment, 0x0024, () => unknown_F000_002D_F002D(0x1234), 0x1234);"),
        new(DoesNotContain, "CallWithStackSpace")
    ];

    public static readonly GeneratedCodeCheck[] IdiomStrictRam = [
        new(Contains, "// Relaxations: none"),
        new(Contains, "DsGlobals.Byte0601 = UInt8[ES, index];"),
        new(DoesNotContain, "DsGlobals.Byte0601 = 0x42;")
    ];

    public static readonly GeneratedCodeCheck[] IdiomRelaxationRam = [
        new(Contains, "// Relaxations: UnknownMemoryIsRam"),
        new(Contains, "DsGlobals.Byte0601 = 0x42;")
    ];

    public static readonly GeneratedCodeCheck[] IdiomStrictSegment = [
        new(Contains, "// Relaxations: none"),
        new(Contains, "UInt16[DS, index + 1] = 0x1234;"),
        new(DoesNotContain, "UInt32[DS, index + 1] = 0x56781234;")
    ];

    public static readonly GeneratedCodeCheck[] IdiomRelaxationSegment = [
        new(Contains, "// Relaxations: AccessesStayInSegment"),
        new(Contains, "UInt32[DS, index + 1] = 0x56781234;")
    ];

    public static readonly GeneratedCodeCheck[] IdiomRelaxationStack = [
        new(Contains, "// Relaxations: AbandonedStackSlotsUnobserved"),
        new(Contains, "DX = SsUInt16[SP + 2];")
    ];

    public static readonly GeneratedCodeCheck[] IdiomStrictSaved = [
        new(Contains, "// Relaxations: none"),
        new(Contains, "/// <summary>Inputs: AL, SI, DI. Outputs: SI, DI. Changes: SI, DI.</summary>"),
        new(DoesNotContain, "/// <summary>Inputs: AL. Outputs: none. Changes: none.</summary>")
    ];

    public static readonly GeneratedCodeCheck[] IdiomRelaxationSaved = [
        new(Contains, "// Relaxations: SavedSlotsWrittenOnlyByPops"),
        new(Contains, "/// <summary>Inputs: AL. Outputs: none. Changes: none.</summary>")
    ];

    public static readonly GeneratedCodeCheck[] BatchCallLabelNested = [
        new(Contains, "return TerminateCall(cs1, 0x0112, () => Dos.Terminate(AL));")
    ];

    public static readonly GeneratedCodeCheck[] BatchCallLabelUnknown = [
        new(Contains, "return TerminateCall(cs1, 0x0112, () => Dos.Terminate(AL));")
    ];

    public static readonly GeneratedCodeCheck[] BatchCallNoextFallsBackToCom = [
        new(DoesNotContain, "new CallLanding")
    ];

    public static readonly GeneratedCodeCheck[] CcCall32Bit = [
        new(Contains, "EAX = NearCall32(biosSegment, 0x001D, () => unknown_F000_0044_F0044(0x11112222, 0x00003333));")
    ];

    public static readonly GeneratedCodeCheck[] CcCallShapes = [
        new(Contains, "/// <summary>Inputs: none. Outputs: AX. Changes: AX.</summary>")
    ];

    public static readonly GeneratedCodeCheck[] CcCallsKeepStackOffset = [
        new(Contains, "(result, result2) = NearCall(biosSegment, 0x0011, () => unknown_F000_0023_F0023());"),
        new(Contains, "AX = CallWithStackArguments(biosSegment, 0x0029, () => unknown_F000_0032_F0032(0x1234), 0x1234);")
    ];

    public static readonly GeneratedCodeCheck[] CcCallsKeepStackOffsetStk = [
        new(Contains, "AX = CallWithStackSpace(biosSegment, 0x0029, 0x2, () => unknown_F000_0032_F0032(0x1234));")
    ];

    public static readonly GeneratedCodeCheck[] CcDispatchTargets = [
        new(Contains, "AX = unknown_F000_0045_F0045(CX);")
    ];

    public static readonly GeneratedCodeCheck[] CcIrqDeliveredFlagsBranch = [
        new(Contains, "/// <summary>Inputs: BX, DS, flags, DF. Outputs: AX, BX, DS, flags, DF. Changes: AX, BX, DS, flags, DF.</summary>")
    ];

    public static readonly GeneratedCodeCheck[] CcSavedRegisters = [
        new(Contains, "(DI, SI) = Stack.Restore2();"),
        new(Contains, "AX = word1;")
    ];

    public static readonly GeneratedCodeCheck[] CcSavedRegistersSav = [
        new(Contains, "// Relaxations: SavedSlotsWrittenOnlyByPops")
    ];

    public static readonly GeneratedCodeCheck[] CcStackFrames = [
        new(Contains, "(AX, BP) = unknown_F000_0278_F0278();"),
        new(Contains, "return (word1, BP);")
    ];

    public static readonly GeneratedCodeCheck[] CcStackFramesStk = [
        new(Contains, "// Relaxations: AbandonedStackSlotsUnobserved")
    ];

    public static readonly GeneratedCodeCheck[] CcUntypedCallPassthrough = [
        new(Contains, "CX = unknown_F000_0036_F0036();")
    ];

    public static readonly GeneratedCodeCheck[] ChildExecTerminateReleasesHandles = [
        new(MethodSignature, "public virtual Action unknown_016E_0100_017E0() {"),
        new(Contains, "(AX, failed) = ServiceCall(Dos, () => Dos.OpenFile(0, Dos.StringDecoder.GetDosString(DS, 0x0121, '\\0')));"),
        new(Contains, "return TerminateCall(cs2, 0x011C, () => Dos.Terminate(AL));"),
        new(Contains, "(AX, CarryFlag) = ServiceCall(Dos, () => Dos.CloseFile(BX, 0x3E));"),
        new(Contains, "throw FailAsUntested(\"Untested interrupt at 117D:011F\");"),
        new(Contains, "bool failed = false;"),
        new(DoesNotContain, "// F000:0000 mov CX,0")
    ];

    public static readonly GeneratedCodeCheck[] ConsoleAh09PrintString = [
        new(MethodSignature, "public virtual Action unknown_016E_0100_017E0_Asm() {")
    ];

    public static readonly GeneratedCodeCheck[] ConventionConditionalFlagInput = [
        new(Contains, "(_, DL) = NearCall(biosSegment, 0x001A, () => unknown_F000_0039_F0039(AX, 0, CarryFlag));")
    ];

    public static readonly GeneratedCodeCheck[] ConventionResolvedCalls = [
        new(Contains, "(AL, CX) = NearCall(biosSegment, 0x0120, () => unknown_F000_0123_F0123(word1));"),
        new(Contains, "NearCall(biosSegment, 0x004F, () => unknown_F000_0142_F0142(0x1111, DS));"),
        new(Contains, "(AL, CH) = NearCall(biosSegment, 0x00B7, () => unknown_F000_011D_F011D(DS));"),
        new(Contains, "(AX, DX) = Div16(divisor: CX, dividend: (uint)DX << 16 | (uint)AX);")
    ];

    public static readonly GeneratedCodeCheck[] ConventionReturnOutputs = [
        new(Contains, "return (AX, 9);"),
        new(Contains, "return (AX, 9);")
    ];

    public static readonly GeneratedCodeCheck[] DataflowCompilerIdioms = [
        new(Contains, "DX = (ushort)(((uint)word3 | (uint)word4 << 16) << 1 >> 16);"),
        new(Contains, "(AX, word4) = Div16(divisor: UInt16[CS, index2 + 2], dividend: (uint)word3);"),
        new(Contains, "AX ^= word4;")
    ];

    public static readonly GeneratedCodeCheck[] DataflowDeviceOrder = [
        new(Contains, "dword1 = SegmentA000Globals.Dword0000;")
    ];

    public static readonly GeneratedCodeCheck[] DataflowDoublePrecision = [
        new(Contains, "uint dxAx_0127 = (uint)(UInt32[DS, word5 + 0x0400] + ((uint)word4 | (uint)word9 << 16));")
    ];

    public static readonly GeneratedCodeCheck[] DataflowForwardedRegisterRead = [
        new(Contains, "Segment2000Globals.Dword0808 = dword1;")
    ];

    public static readonly GeneratedCodeCheck[] DataflowJoinedMemorySeg = [
        new(Contains, "// Relaxations: AccessesStayInSegment")
    ];

    public static readonly GeneratedCodeCheck[] DataflowLegalityTraps = [
        new(Contains, "savedEs_022C = word9;"),
        new(Contains, "byte2 = SegmentF000Globals.Byte0451;")
    ];

    public static readonly GeneratedCodeCheck[] DataflowOverwrittenStores = [
        new(Contains, "(AX, DX) = Div16(divisor: SegmentF000Globals.Word0400, dividend: (uint)DX << 16 | (uint)AX);"),
        new(DoesNotContain, "Segment2000Globals.Word0060 = 0x1111;"),
        new(Contains, "UInt16[DS, BX + 0x52] = 0x2222;")
    ];

    public static readonly GeneratedCodeCheck[] DataflowRamPolicy = [
        new(Contains, "SegmentA000Globals.Word0030 = 0x1234;")
    ];

    public static readonly GeneratedCodeCheck[] DataflowRamPolicyRam = [
        new(Contains, "// Relaxations: UnknownMemoryIsRam")
    ];

    public static readonly GeneratedCodeCheck[] DataflowStackSaveRestore = [
        new(Contains, "index2 += 0x0010;"),
        new(Contains, "ushort index = 0;"),
        new(MethodSignature, "public virtual ushort unknown_F000_00C7_F00C7(ushort arg0) {")
    ];

    public static readonly GeneratedCodeCheck[] DataflowStackVram = [
        new(Contains, "BX = Stack.Pop16();")
    ];

    public static readonly GeneratedCodeCheck[] DataJumpTables = [
        new(Contains, "switch (UInt16[ES, SI + 0x030E]) {")
    ];

    public static readonly GeneratedCodeCheck[] DataSegmentValues = [
        new(Contains, "Stack.Release(1);")
    ];

    public static readonly GeneratedCodeCheck[] DbcsLeadByteTableAl0 = [
        new(Contains, "(status, tableOffset, DS, CarryFlag) = ServiceCall(Dos, () => Dos.ReadLeadByteTablePointer());")
    ];

    public static readonly GeneratedCodeCheck[] DosExecModesAndOverlays = [
        new(MethodSignature, "public virtual (ushort result, ushort result2, ushort result3, bool result4, bool result5, bool result6, bool result7, bool result8) unknown_016F_0246_01936(ushort word1, ushort word2) {"),
        new(Contains, "(AX, CarryFlag) = ServiceCall(cs1, 0x01E3, Dos, () => Dos.LoadProgramOnly(Dos.StringDecoder.GetDosString(DS, 0x02F1, '\\0'), ES, 0x0371, 0x4B01), r => CarryOnInterruptStack(r.failed));"),
        new(Contains, "return Returned;"),
        new(Contains, "InterruptCall(cs1, 0x01C0, 0x60, cs2, 0x0118, unknown_01D9_0118_01EA8Entry);"),
        new(Contains, "NearCall(cs1, 0x0126, unknown_016F_02E0_019D0_AsmEntry);")
    ];

    public static readonly GeneratedCodeCheck[] DosFcbChild = [
        new(Contains, "NearCall(cs1, 0x01B5, unknown_016F_01C0_018B0_AsmEntry);")
    ];

    public static readonly GeneratedCodeCheck[] EnvironmentBlockContainsProgramPath = [
        new(MethodSignature, "public virtual Action unknown_016E_0124_01804() {")
    ];

    public static readonly GeneratedCodeCheck[] EnvironmentBlockNotCorrupted = [
        new(Contains, "word1 = Dos.ReadPspAddress();")
    ];

    public static readonly GeneratedCodeCheck[] InterruptCallPushedFlags = [
        new(Contains, "SetStatusFlags(carry: false, parity: true, auxiliary: true, zero: false, sign: true, overflow: true);")
    ];

    public static readonly GeneratedCodeCheck[] InterruptContractUnseen1cHandler = [
        new(Contains, "return unknown_F000_000A_F000A();")
    ];

    public static readonly GeneratedCodeCheck[] InterruptHandlerChangesRegister = [
        new(Contains, "/// <summary>Inputs: flags, DF. Outputs: AX, flags, DF. Changes: AX, flags, DF.</summary>")
    ];

    public static readonly GeneratedCodeCheck[] IntServiceCalls = [
        new(Contains, "word1 = SsUInt16[index - 6];")
    ];

    public static readonly GeneratedCodeCheck[] Jmpmov = [
        new(Contains, "throw FailAsUntested($\"Untested near jump target 0x{AX:X4} at F000:0016\");")
    ];

    public static readonly GeneratedCodeCheck[] Jump1NoSpec = [
        new(Contains, "throw FailAsUntested(\"Untested fallthrough at F000:0014\");")
    ];

    public static readonly GeneratedCodeCheck[] Jump1Spec = [
        new(Contains, "if (CarryFlag || ZeroFlag) {")
    ];

    public static readonly GeneratedCodeCheck[] Jump2 = [
        new(Contains, "throw FailAsUntested($\"Untested far call target {targetSegment_FDE7:X4}:{targetOffset_FDE7:X4} at E342:FDE7\");")
    ];

    public static readonly GeneratedCodeCheck[] MouseIrq12Registered = [
        new(Contains, "(EAX, ECX, EDX, EBX, EBP, ESI, EDI, DS, FS, GS) = unknown_F000_002D_F002D();"),
        new(Contains, "(EAX, ECX, EDX, EBX, EBP, ESI, EDI, DS, FS, GS) = unknown_F000_002D_F002D();")
    ];

    public static readonly GeneratedCodeCheck[] PartitionJumpIntoFunctionMiddle = [
        new(Contains, "BX = unknown_F000_0029_F0029();")
    ];

    public static readonly GeneratedCodeCheck[] PartitionTwoFaultHandlers = [
        new(Contains, "return DispatchCpuFault(cpuException, biosSegment, 0x001F, [new CpuFaultTarget(new SegmentedAddress(biosSegment, 0x003A), unknown_F000_003A_F003A), new CpuFaultTarget(new SegmentedAddress(biosSegment, 0x004D), unknown_F000_004D_F004D)], [new SegmentedAddress(biosSegment, 0x001A)]);")
    ];

    public static readonly GeneratedCodeCheck[] ProgramArgumentsPrintArgv0 = [
        new(MethodSignature, "public virtual Action unknown_016E_0100_017E0() {")
    ];

    public static readonly GeneratedCodeCheck[] SaveRestorePairs = [
        new(Contains, "UInt16[word1, index + 8] = arg02;")
    ];

    public static readonly GeneratedCodeCheck[] Segpr = [
        new(Contains, "throw FailAsUntested(\"Untested return from call at F000:0074\");")
    ];

    public static readonly GeneratedCodeCheck[] SpeculativeCallEntry = [
        new(Contains, "throw FailAsUntested(\"Untested return from call at F000:0013\");")
    ];

    public static readonly GeneratedCodeCheck[] SpeculativeInvalidOpcode = [
        new(Contains, "throw FailAsUntested(\"Untested jump at F000:000A\");")
    ];

    public static readonly GeneratedCodeCheck[] SpeculativeTransferIntoData = [
        new(Contains, "throw FailAsUntested(\"Untested jump at F000:001F\");")
    ];

    public static readonly GeneratedCodeCheck[] SpeculativeZeroMemory = [
        new(Contains, "throw FailAsUntested(\"Untested jump at F000:000A\");")
    ];

    public static readonly GeneratedCodeCheck[] StackEspHighBits = [
        new(Contains, "Stack.Leave32();"),
        new(Contains, "Stack.Pop32();")
    ];
}
