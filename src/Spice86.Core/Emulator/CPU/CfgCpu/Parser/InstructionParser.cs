namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Linq;

using InstructionNode = Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.InstructionNode;

public class InstructionParser {
    // Handler tables: single-byte opcodes (0x00-0xFF) and 0F-prefixed opcodes (indexed by second byte)
    private readonly Func<ParsingContext, CfgInstruction>?[] _handlers = new Func<ParsingContext, CfgInstruction>?[256];
    private readonly Func<ParsingContext, CfgInstruction>?[] _handlers0F = new Func<ParsingContext, CfgInstruction>?[256];
    private readonly ParsingTools _parsingTools;

    private readonly AluOperationParser _aluOperationParser;
    private readonly BcdAdjustParser _bcdAdjustParser;
    private readonly BitScanRmParser _bitScanBsfParser;
    private readonly BitScanRmParser _bitScanBsrParser;
    private readonly BitTestImmediateParser _bitTestImmediateParser;
    private readonly BitTestRmParser _bitTestRmParser;
    private readonly BoundParser _boundParser;
    private readonly BswapParser _bswapParser;
    private readonly CallParser _callParser;
    private readonly CbwParser _cbwParser;
    private readonly CmpxchgRmParser _cmpxchgRmParser;
    private readonly CwdParser _cwdParser;
    private readonly EnterParser _enterParser;
    private readonly FlagControlParser _flagControlParser;
    private readonly FlagTransferParser _flagTransferParser;
    private readonly FpuParser _fpuParser;
    private readonly Grp1Parser _grp1Parser;
    private readonly Grp2Parser _grp2ParserImm;
    private readonly Grp2Parser _grp2ParserOne;
    private readonly Grp2Parser _grp2ParserCl;
    private readonly Grp3Parser _grp3Parser;
    private readonly Grp45Parser _grp45Parser;
    private readonly ImulImmRmParser _imulImmRmParser;
    private readonly ImulRmParser _imulRmParser;
    private readonly InterruptParser _interruptParser;
    private readonly IoAccDxParser _ioAccDxParser;
    private readonly IoAccImmParser _ioAccImmParser;
    private readonly IoStringParser _ioStringParser;
    private readonly JccParser _jccParser;
    private readonly JcxzParser _jcxzParser;
    private readonly JmpParser _jmpParser;
    private readonly LeaParser _leaParser;
    private readonly LeaveParser _leaveParser;
    private readonly LoopParser _loopParser;
    private readonly LxsParser _lxsParser;
    private readonly MemoryStringOpParser _memoryStringOpParser;
    private readonly MovMoffsParser _movMoffsParser;
    private readonly MovModRmParser _movModRmParser;
    private readonly MovRegImmParser _movRegImmParser;
    private readonly MovRmExtendByteParser _movRmExtendByteZeroParser;
    private readonly MovRmExtendByteParser _movRmExtendByteSignParser;
    private readonly MovRmExtendWordParser _movRmExtendWordParser;
    private readonly MovRmImmParser _movRmImmParser;
    private readonly MovRmSregParser _movRmSregParser;
    private readonly MovSregRm16Parser _movSregRm16Parser;
    private readonly PopaParser _popaParser;
    private readonly PopFParser _popFParser;
    private readonly PopRmParser _popRmParser;
    private readonly PushaParser _pushaParser;
    private readonly RegisterOpParser _registerOpParser;
    private readonly ReturnParser _returnParser;
    private readonly PushFParser _pushFParser;
    private readonly PushImmParser _pushImmParser;
    private readonly SegRegPushPopParser _segRegPushPopParser;
    private readonly SetRmccParser _setRmccParser;
    private readonly ShxdRmParser _shxdRmParser;
    private readonly SimpleInstructionParser _simpleInstructionParser;
    private readonly TestAccImmParser _testAccImmParser;
    private readonly TestRmRegParser _testRmRegParser;
    private readonly XaddRmParser _xaddRmParser;
    private readonly XchgRmParser _xchgRmParser;
    private readonly XlatParser _xlatParser;

    public InstructionParser(IIndexable memory, State state) {
        _parsingTools = new(memory, state);
        _aluOperationParser = new(_parsingTools);
        _bcdAdjustParser = new(_parsingTools);
        _bitScanBsfParser = new(_parsingTools, InstructionOperation.BSF, "BitScanForward");
        _bitScanBsrParser = new(_parsingTools, InstructionOperation.BSR, "BitScanReverse");
        _bitTestImmediateParser = new BitTestImmediateParser(_parsingTools);
        _bitTestRmParser = new(_parsingTools);
        _boundParser = new(_parsingTools);
        _bswapParser = new(_parsingTools);
        _callParser = new(_parsingTools);
        _cbwParser = new(_parsingTools);
        _cmpxchgRmParser = new CmpxchgRmParser(_parsingTools);
        _cwdParser = new(_parsingTools);
        _enterParser = new(_parsingTools);
        _flagControlParser = new(_parsingTools);
        _flagTransferParser = new(_parsingTools);
        _fpuParser = new(_parsingTools);
        _grp1Parser = new(_parsingTools);
        _grp2ParserImm = new(_parsingTools, Grp2CountSource.Immediate);
        _grp2ParserOne = new(_parsingTools, Grp2CountSource.One);
        _grp2ParserCl = new(_parsingTools, Grp2CountSource.Cl);
        _grp3Parser = new(_parsingTools);
        _grp45Parser = new(_parsingTools);
        _imulImmRmParser = new(_parsingTools);
        _imulRmParser = new(_parsingTools);
        _interruptParser = new(_parsingTools);
        _ioAccDxParser = new(_parsingTools);
        _ioAccImmParser = new(_parsingTools);
        _ioStringParser = new(_parsingTools);
        _jccParser = new(_parsingTools);
        _jcxzParser = new(_parsingTools);
        _jmpParser = new(_parsingTools);
        _leaParser = new(_parsingTools);
        _leaveParser = new(_parsingTools);
        _loopParser = new(_parsingTools);
        _lxsParser = new(_parsingTools);
        _memoryStringOpParser = new(_parsingTools);
        _movMoffsParser = new(_parsingTools);
        _movModRmParser = new(_parsingTools);
        _movRegImmParser = new(_parsingTools);
        _movRmExtendByteZeroParser = new(_parsingTools, false);
        _movRmExtendByteSignParser = new(_parsingTools, true);
        _movRmExtendWordParser = new(_parsingTools);
        _movRmImmParser = new(_parsingTools);
        _movRmSregParser = new(_parsingTools);
        _movSregRm16Parser = new(_parsingTools);
        _popaParser = new(_parsingTools);
        _popFParser = new(_parsingTools);
        _popRmParser = new(_parsingTools);
        _pushaParser = new(_parsingTools);
        _registerOpParser = new(_parsingTools);
        _returnParser = new(_parsingTools);
        _pushFParser = new(_parsingTools);
        _pushImmParser = new(_parsingTools);
        _segRegPushPopParser = new(_parsingTools);
        _setRmccParser = new(_parsingTools);
        _shxdRmParser = new(_parsingTools);
        _simpleInstructionParser = new(_parsingTools);
        _testAccImmParser = new(_parsingTools);
        _testRmRegParser = new(_parsingTools);
        _xaddRmParser = new(_parsingTools);
        _xchgRmParser = new(_parsingTools);
        _xlatParser = new(_parsingTools);
        PopulateHandlerTables();
    }

    private InstructionField<ushort> ReadOpcode() {
        byte opcode = _parsingTools.InstructionReader.UInt8.PeekField(true).Value;
        if (opcode == 0x0F) {
            return _parsingTools.InstructionReader.UInt16BigEndian.NextField(true);
        }

        return _parsingTools.InstructionReader.UInt8AsUshort.NextField(true);
    }

    public CfgInstruction ParseInstructionAt(SegmentedAddress address) {
        _parsingTools.InstructionReader.InstructionReaderAddressSource.InstructionAddress = address;
        List<InstructionPrefix> prefixes = ParsePrefixes();
        InstructionField<ushort> opcodeField = ReadOpcode();
        ParsingContext context = new(address, opcodeField, prefixes);
        try {
            CfgInstruction parsed = ParseCfgInstruction(context);
            ValidateLockPrefix(parsed, prefixes);
            return parsed;
        } catch (CpuInvalidOpcodeException e) {
            CfgInstruction instruction = new(address, opcodeField, prefixes, null) {
                Kind = InstructionKind.Invalid
            };
            instruction.AttachAsts(
                new InstructionNode(InstructionOperation.INVALID),
                new InvalidInstructionNode(instruction, e));
            return instruction;
        }
    }

    private void PopulateHandlerTables() {
        PopulateSingleByteHandlers();
        Populate0FHandlers();
    }

    private void PopulateSingleByteHandlers() {
        // Loop-based handlers — entries interleave with explicit assignments below
        PopulateAluHandlers();              // 0x00-0x3D (lo3 <= 5)
        PopulateSegPushPopHandlers();       // 0x06, 0x07, 0x0E, 0x16, 0x17, 0x1E, 0x1F
        PopulateIncDecPushPopRegHandlers(); // 0x40-0x5F
        PopulateJccShortHandlers();         // 0x70-0x7F
        PopulateGrp1Handlers();            // 0x80-0x83
        PopulateModRmOpsHandlers();        // 0x84-0x8B
        PopulateXchgRegAccHandlers();      // 0x91-0x97
        PopulateMovRegImmHandlers();       // 0xB0-0xBF

        // 0x00-0x25: ALU + segment push/pop — populated by PopulateAluHandlers and PopulateSegPushPopHandlers

        // BCD arithmetic: lo3=7 in upper 4 ALU blocks (mid3=4..7 → opcodes 0x27, 0x2F, 0x37, 0x3F)
        // DAA(mid3=4), DAS(mid3=5), AAA(mid3=6), AAS(mid3=7). Operations differ enough that a loop doesn't simplify.
        _handlers[0x27] = ctx => _bcdAdjustParser.ParseDecimalAdjust(ctx, adjustOp: BinaryOperation.PLUS, displayOp: InstructionOperation.DAA);
        // 0x28-0x2D: SUB — populated by PopulateAluHandlers
        _handlers[0x2F] = ctx => _bcdAdjustParser.ParseDecimalAdjust(ctx, adjustOp: BinaryOperation.MINUS, displayOp: InstructionOperation.DAS);
        // 0x30-0x35: XOR — populated by PopulateAluHandlers
        _handlers[0x37] = ctx => _bcdAdjustParser.ParseAsciiAdjust(ctx, adjustOp: BinaryOperation.PLUS, displayOp: InstructionOperation.AAA);
        // 0x38-0x3D: CMP — populated by PopulateAluHandlers
        _handlers[0x3F] = ctx => _bcdAdjustParser.ParseAsciiAdjust(ctx, adjustOp: BinaryOperation.MINUS, displayOp: InstructionOperation.AAS);

        // 0x40-0x5F: INC/DEC/PUSH/POP reg — populated by PopulateIncDecPushPopRegHandlers

        // Miscellaneous 0x60-0x6F
        _handlers[0x60] = _pushaParser.Parse;
        _handlers[0x61] = _popaParser.Parse;
        _handlers[0x62] = _boundParser.Parse;
        _handlers[0x68] = ctx => _pushImmParser.Parse(ctx, imm8SignExtended: false);
        _handlers[0x69] = ctx => _imulImmRmParser.Parse(ctx, imm8: false);
        _handlers[0x6A] = ctx => _pushImmParser.Parse(ctx, imm8SignExtended: true);
        _handlers[0x6B] = ctx => _imulImmRmParser.Parse(ctx, imm8: true);
        _handlers[0x6C] = ctx => _ioStringParser.Parse(ctx, isInput: true);
        _handlers[0x6D] = ctx => _ioStringParser.Parse(ctx, isInput: true);
        _handlers[0x6E] = ctx => _ioStringParser.Parse(ctx, isInput: false);
        _handlers[0x6F] = ctx => _ioStringParser.Parse(ctx, isInput: false);

        // 0x70-0x7F: Jcc short — populated by PopulateJccShortHandlers
        // 0x80-0x83: GRP1 — populated by PopulateGrp1Handlers
        // 0x84-0x8B: TEST/XCHG/MOV rm — populated by PopulateModRmOpsHandlers

        _handlers[0x8C] = _movRmSregParser.Parse;
        _handlers[0x8D] = _leaParser.Parse;
        _handlers[0x8E] = _movSregRm16Parser.Parse;
        _handlers[0x8F] = _popRmParser.Parse;

        _handlers[0x90] = _simpleInstructionParser.ParseNop;
        // 0x91-0x97: XCHG reg,acc — populated by PopulateXchgRegAccHandlers

        // 0x98-0x9F
        _handlers[0x98] = _cbwParser.Parse;
        _handlers[0x99] = _cwdParser.Parse;
        _handlers[0x9A] = _callParser.ParseCallFarImm;
        _handlers[0x9B] = _simpleInstructionParser.ParseFwait;
        _handlers[0x9C] = _pushFParser.Parse;
        _handlers[0x9D] = _popFParser.Parse;
        _handlers[0x9E] = _flagTransferParser.ParseSahf;
        _handlers[0x9F] = _flagTransferParser.ParseLahf;

        // MOV acc,moffs / MOV moffs,acc + string operations
        _handlers[0xA0] = ctx => _movMoffsParser.Parse(ctx, isLoad: true);
        _handlers[0xA1] = ctx => _movMoffsParser.Parse(ctx, isLoad: true);
        _handlers[0xA2] = ctx => _movMoffsParser.Parse(ctx, isLoad: false);
        _handlers[0xA3] = ctx => _movMoffsParser.Parse(ctx, isLoad: false);
        _handlers[0xA4] = ctx => _memoryStringOpParser.Parse(ctx, MemoryStringOpKind.Movs);
        _handlers[0xA5] = ctx => _memoryStringOpParser.Parse(ctx, MemoryStringOpKind.Movs);
        _handlers[0xA6] = ctx => _memoryStringOpParser.Parse(ctx, MemoryStringOpKind.Cmps);
        _handlers[0xA7] = ctx => _memoryStringOpParser.Parse(ctx, MemoryStringOpKind.Cmps);
        _handlers[0xA8] = _testAccImmParser.Parse;
        _handlers[0xA9] = _testAccImmParser.Parse;
        _handlers[0xAA] = ctx => _memoryStringOpParser.Parse(ctx, MemoryStringOpKind.Stos);
        _handlers[0xAB] = ctx => _memoryStringOpParser.Parse(ctx, MemoryStringOpKind.Stos);
        _handlers[0xAC] = ctx => _memoryStringOpParser.Parse(ctx, MemoryStringOpKind.Lods);
        _handlers[0xAD] = ctx => _memoryStringOpParser.Parse(ctx, MemoryStringOpKind.Lods);
        _handlers[0xAE] = ctx => _memoryStringOpParser.Parse(ctx, MemoryStringOpKind.Scas);
        _handlers[0xAF] = ctx => _memoryStringOpParser.Parse(ctx, MemoryStringOpKind.Scas);

        // 0xB0-0xBF: MOV reg,imm — populated by PopulateMovRegImmHandlers

        // GRP2 shifts/rotates + RET + MOV rm,imm + LES/LDS + ENTER/LEAVE
        _handlers[0xC0] = ctx => _grp2ParserImm.Parse(ctx);
        _handlers[0xC1] = ctx => _grp2ParserImm.Parse(ctx);
        _handlers[0xC2] = ctx => _returnParser.ParseRetNear(ctx, hasImm: true);
        _handlers[0xC3] = ctx => _returnParser.ParseRetNear(ctx, hasImm: false);
        _handlers[0xC4] = ctx => _lxsParser.Parse(ctx, InstructionOperation.LES, SegmentRegisterIndex.EsIndex);
        _handlers[0xC5] = ctx => _lxsParser.Parse(ctx, InstructionOperation.LDS, SegmentRegisterIndex.DsIndex);
        _handlers[0xC6] = _movRmImmParser.Parse;
        _handlers[0xC7] = _movRmImmParser.Parse;
        _handlers[0xC8] = _enterParser.Parse;
        _handlers[0xC9] = _leaveParser.Parse;
        _handlers[0xCA] = ctx => _returnParser.ParseRetFar(ctx, hasImm: true);
        _handlers[0xCB] = ctx => _returnParser.ParseRetFar(ctx, hasImm: false);

        // Interrupts
        _handlers[0xCC] = _interruptParser.ParseInterrupt3;
        _handlers[0xCD] = _interruptParser.ParseInterruptWithVector;
        _handlers[0xCE] = _interruptParser.ParseInterruptOverflow;
        _handlers[0xCF] = _interruptParser.ParseRetInterrupt;

        // GRP2 shifts (second set) + BCD + SALC + XLAT
        _handlers[0xD0] = ctx => _grp2ParserOne.Parse(ctx);
        _handlers[0xD1] = ctx => _grp2ParserOne.Parse(ctx);
        _handlers[0xD2] = ctx => _grp2ParserCl.Parse(ctx);
        _handlers[0xD3] = ctx => _grp2ParserCl.Parse(ctx);
        _handlers[0xD4] = _bcdAdjustParser.ParseAam;
        _handlers[0xD5] = _bcdAdjustParser.ParseAad;
        _handlers[0xD6] = _flagTransferParser.ParseSalc;
        _handlers[0xD7] = _xlatParser.Parse;

        // FPU escapes (0xD8-0xDF): only D9, DB, DD have partial support
        _handlers[0xD9] = ctx => _fpuParser.ParseFpuStoreWordGroup7(ctx, 0x37F, InstructionOperation.FNSTCW);
        _handlers[0xDB] = _fpuParser.ParseFnInitModRmE3;
        _handlers[0xDD] = ctx => _fpuParser.ParseFpuStoreWordGroup7(ctx, 0xFF, InstructionOperation.FNSTSW);

        // LOOP/JCXZ + IN/OUT imm
        _handlers[0xE0] = _loopParser.ParseLoopne;
        _handlers[0xE1] = _loopParser.ParseLoope;
        _handlers[0xE2] = _loopParser.ParseLoop;
        _handlers[0xE3] = _jcxzParser.Parse;
        // IN/OUT imm (0xE4-0xE7): bit 1 = direction (0=in, 1=out), bit 0 = size (0=8-bit, 1=16/32-bit)
        _handlers[0xE4] = ctx => _ioAccImmParser.Parse(ctx, isInput: true);
        _handlers[0xE5] = ctx => _ioAccImmParser.Parse(ctx, isInput: true);
        _handlers[0xE6] = ctx => _ioAccImmParser.Parse(ctx, isInput: false);
        _handlers[0xE7] = ctx => _ioAccImmParser.Parse(ctx, isInput: false);

        // CALL/JMP near/far + JMP short
        _handlers[0xE8] = _callParser.ParseCallNearImm;
        _handlers[0xE9] = _jmpParser.ParseJmpNearImm;
        _handlers[0xEA] = _jmpParser.ParseJmpFarImm;
        _handlers[0xEB] = _jmpParser.ParseJmpNearImm8;

        // IN/OUT dx (0xEC-0xEF): bit 1 = direction (0=in, 1=out), bit 0 = size (0=8-bit, 1=16/32-bit)
        _handlers[0xEC] = ctx => _ioAccDxParser.Parse(ctx, isInput: true);
        _handlers[0xED] = ctx => _ioAccDxParser.Parse(ctx, isInput: true);
        _handlers[0xEE] = ctx => _ioAccDxParser.Parse(ctx, isInput: false);
        _handlers[0xEF] = ctx => _ioAccDxParser.Parse(ctx, isInput: false);

        // 0xF4-0xFF: HLT, CMC, GRP3, flags, GRP4/5
        _handlers[0xF4] = _simpleInstructionParser.ParseHlt;
        _handlers[0xF5] = _flagControlParser.ParseCmc;
        _handlers[0xF6] = _grp3Parser.Parse;
        _handlers[0xF7] = _grp3Parser.Parse;
        // Flag control (0xF8-0xFD): lo3/2 selects flag (0=carry, 1=interrupt, 2=direction), lo3%2 selects value.
        // STI (0xFB) has special interrupt-shadowing logic that breaks the pattern.
        _handlers[0xF8] = ctx => _flagControlParser.ParseFlagControl(ctx, flagNode: _parsingTools.AstBuilder.Flag.Carry(), value: 0UL, displayOp: InstructionOperation.CLC);
        _handlers[0xF9] = ctx => _flagControlParser.ParseFlagControl(ctx, flagNode: _parsingTools.AstBuilder.Flag.Carry(), value: 1UL, displayOp: InstructionOperation.STC);
        _handlers[0xFA] = ctx => _flagControlParser.ParseFlagControl(ctx, flagNode: _parsingTools.AstBuilder.Flag.Interrupt(), value: 0UL, displayOp: InstructionOperation.CLI);
        _handlers[0xFB] = _flagControlParser.ParseSti;
        _handlers[0xFC] = ctx => _flagControlParser.ParseFlagControl(ctx, flagNode: _parsingTools.AstBuilder.Flag.Direction(), value: 0UL, displayOp: InstructionOperation.CLD);
        _handlers[0xFD] = ctx => _flagControlParser.ParseFlagControl(ctx, flagNode: _parsingTools.AstBuilder.Flag.Direction(), value: 1UL, displayOp: InstructionOperation.STD);
        _handlers[0xFE] = _grp45Parser.Parse;
        _handlers[0xFF] = _grp45Parser.Parse;
    }

    /// <summary>
    /// Populates ALU operation handlers at 0x00-0x3D (lo3 &lt;= 5).
    /// 8 operations x 6 variants = 48 entries. mid3 selects ALU operation, lo3 selects variant.
    /// </summary>
    private void PopulateAluHandlers() {
        for (int op = 0; op < 8; op++) {
            int capturedOp = op;
            int baseOpcode = op << 3;
            for (int variant = 0; variant <= 5; variant++) {
                _handlers[baseOpcode + variant] = ctx => _aluOperationParser.Parse(ctx, operationIndex: capturedOp);
            }
        }
    }

    /// <summary>
    /// Populates segment register push/pop in ALU block gaps (lo3=6/7).
    /// mid3 = segment register index (ES=0, CS=1, SS=2, DS=3).
    /// lo3=6 → PUSH, lo3=7 → POP. 0x0F (seg=1, lo3=7) is the two-byte prefix, not POP CS.
    /// </summary>
    private void PopulateSegPushPopHandlers() {
        for (int seg = 0; seg < 4; seg++) {
            int capturedSeg = seg;
            int baseOpcode = seg << 3;
            _handlers[baseOpcode + 6] = ctx => _segRegPushPopParser.ParsePushSReg(ctx, segRegIndex: capturedSeg);
            if (seg != 1) { // 0x0F is the two-byte prefix, not POP CS
                _handlers[baseOpcode + 7] = ctx => _segRegPushPopParser.ParsePopSReg(ctx, segRegIndex: capturedSeg);
            }
        }
    }

    /// <summary>
    /// Populates INC/DEC/PUSH/POP register handlers at 0x40-0x5F.
    /// 4 operations x 8 registers = 32 entries. hi2=01, mid3 bits select operation, lo3 selects register.
    /// </summary>
    private void PopulateIncDecPushPopRegHandlers() {
        for (int reg = 0; reg < 8; reg++) {
            int capturedReg = reg;
            _handlers[0x40 + reg] = ctx => _registerOpParser.ParseIncDecReg(ctx, regIndex: capturedReg, aluOperation: "Inc", displayOp: InstructionOperation.INC);
            _handlers[0x48 + reg] = ctx => _registerOpParser.ParseIncDecReg(ctx, regIndex: capturedReg, aluOperation: "Dec", displayOp: InstructionOperation.DEC);
            _handlers[0x50 + reg] = ctx => _registerOpParser.ParsePushReg(ctx, regIndex: capturedReg);
            _handlers[0x58 + reg] = ctx => _registerOpParser.ParsePopReg(ctx, regIndex: capturedReg);
        }
    }

    /// <summary>
    /// Populates Jcc short handlers at 0x70-0x7F. lo3+bit3 = condition code (16 total).
    /// </summary>
    private void PopulateJccShortHandlers() {
        for (int cc = 0; cc < 16; cc++) {
            int capturedCc = cc;
            _handlers[0x70 + cc] = ctx => _jccParser.Parse(ctx, conditionCode: capturedCc);
        }
    }

    /// <summary>
    /// Populates GRP1 handlers at 0x80-0x83. lo3 bits 1-0 select operand size/sign-extension variant.
    /// </summary>
    private void PopulateGrp1Handlers() {
        for (int v = 0; v < 4; v++) {
            _handlers[0x80 + v] = _grp1Parser.Parse;
        }
    }

    /// <summary>
    /// Populates TEST/XCHG/MOV rm handlers at 0x84-0x8B.
    /// lo3 bits 2-1 select operation, bit 0 selects size (8-bit vs 16/32-bit).
    /// lo3=0/1 (00x) → TEST; lo3=2/3 (01x) → XCHG; lo3=4/5 (10x) → MOV RM←REG; lo3=6/7 (11x) → MOV REG←RM.
    /// </summary>
    private void PopulateModRmOpsHandlers() {
        _handlers[0x84] = _testRmRegParser.Parse;
        _handlers[0x85] = _testRmRegParser.Parse;
        _handlers[0x86] = _xchgRmParser.Parse;
        _handlers[0x87] = _xchgRmParser.Parse;
        _handlers[0x88] = ctx => _movModRmParser.Parse(ctx, regIsDest: false);
        _handlers[0x89] = ctx => _movModRmParser.Parse(ctx, regIsDest: false);
        _handlers[0x8A] = ctx => _movModRmParser.Parse(ctx, regIsDest: true);
        _handlers[0x8B] = ctx => _movModRmParser.Parse(ctx, regIsDest: true);
    }

    /// <summary>
    /// Populates XCHG reg,acc handlers at 0x91-0x97. lo3 = register index (1-7, 0 is NOP).
    /// </summary>
    private void PopulateXchgRegAccHandlers() {
        for (int reg = 1; reg < 8; reg++) {
            int capturedReg = reg;
            _handlers[0x90 + reg] = ctx => _registerOpParser.ParseXchgRegAcc(ctx, regIndex: capturedReg);
        }
    }

    /// <summary>
    /// Populates MOV reg,imm handlers at 0xB0-0xBF.
    /// Bit 3 selects 8-bit (0xB0-0xB7) vs 16/32-bit (0xB8-0xBF), lo3 = register index.
    /// </summary>
    private void PopulateMovRegImmHandlers() {
        for (int reg = 0; reg < 8; reg++) {
            int capturedReg = reg;
            _handlers[0xB0 + reg] = ctx => _movRegImmParser.Parse(ctx, regIndex: capturedReg, is8Bit: true);
            _handlers[0xB8 + reg] = ctx => _movRegImmParser.Parse(ctx, regIndex: capturedReg, is8Bit: false);
        }
    }

    private void Populate0FHandlers() {
        // Jcc near: 16 condition codes (same encoding as Jcc short)
        for (int cc = 0; cc < 16; cc++) {
            int capturedCc = cc;
            _handlers0F[0x80 + cc] = ctx => _jccParser.Parse(ctx, conditionCode: capturedCc);
        }

        // SETcc: 16 condition codes
        for (int cc = 0; cc < 16; cc++) {
            int capturedCc = cc;
            _handlers0F[0x90 + cc] = ctx => _setRmccParser.Parse(ctx, conditionCode: capturedCc);
        }

        // Scattered octal-pattern blocks — entries interleave with explicit assignments below
        PopulateFsGsPushPopHandlers();  // 0xA0, 0xA1, 0xA8, 0xA9
        PopulateBitTestRegHandlers();   // 0xA3, 0xAB, 0xB3, 0xBB
        PopulateDoubleShiftHandlers();  // 0xA4, 0xA5, 0xAC, 0xAD

        // 0xA2-0xAF: remaining entries (gaps filled by methods above)
        _handlers0F[0xA2] = _simpleInstructionParser.ParseCpuid;
        _handlers0F[0xAF] = _imulRmParser.Parse;

        // 0xB0-0xBF (0xB3 and 0xBB filled by PopulateBitTestRegHandlers)
        _handlers0F[0xB0] = _cmpxchgRmParser.Parse;
        _handlers0F[0xB1] = _cmpxchgRmParser.Parse;
        _handlers0F[0xB2] = ctx => _lxsParser.Parse(ctx, InstructionOperation.LSS, SegmentRegisterIndex.SsIndex);
        // 0xB3: BTR — populated by PopulateBitTestRegHandlers
        _handlers0F[0xB4] = ctx => _lxsParser.Parse(ctx, InstructionOperation.LFS, SegmentRegisterIndex.FsIndex);
        _handlers0F[0xB5] = ctx => _lxsParser.Parse(ctx, InstructionOperation.LGS, SegmentRegisterIndex.GsIndex);
        // MOVZX/MOVSX: mid3=6 → zero-extend, mid3=7 → sign-extend. lo3=6 → byte source, lo3=7 → word source
        _handlers0F[0xB6] = ctx => _movRmExtendByteZeroParser.Parse(ctx);
        _handlers0F[0xB7] = ctx => _movRmExtendWordParser.Parse(ctx, signExtend: false);
        _handlers0F[0xBA] = _bitTestImmediateParser.Parse;
        // 0xBB: BTC — populated by PopulateBitTestRegHandlers
        _handlers0F[0xBC] = ctx => _bitScanBsfParser.Parse(ctx);
        _handlers0F[0xBD] = ctx => _bitScanBsrParser.Parse(ctx);
        _handlers0F[0xBE] = ctx => _movRmExtendByteSignParser.Parse(ctx);
        _handlers0F[0xBF] = ctx => _movRmExtendWordParser.Parse(ctx, signExtend: true);

        // XADD + BSWAP
        _handlers0F[0xC0] = _xaddRmParser.Parse;
        _handlers0F[0xC1] = _xaddRmParser.Parse;
        PopulateBswapHandlers(); // 0xC8-0xCF
    }

    /// <summary>
    /// Populates FS/GS push/pop at 0F A0/A1 and 0F A8/A9.
    /// mid3 = segment register index (FS=4, GS=5), lo3=0 → PUSH, lo3=1 → POP.
    /// </summary>
    private void PopulateFsGsPushPopHandlers() {
        for (int seg = 4; seg <= 5; seg++) {
            int capturedSeg = seg;
            int secondByte = (seg << 3) | 0x80; // 0xA0 for FS, 0xA8 for GS
            _handlers0F[secondByte] = ctx => _segRegPushPopParser.ParsePushSReg(ctx, segRegIndex: capturedSeg);
            _handlers0F[secondByte + 1] = ctx => _segRegPushPopParser.ParsePopSReg(ctx, segRegIndex: capturedSeg);
        }
    }

    /// <summary>
    /// Populates bit test register handlers at 0F A3/AB/B3/BB (BT/BTS/BTR/BTC).
    /// lo3=3, mid3 selects BT(4)/BTS(5)/BTR(6)/BTC(7). Opcodes spaced at +8 (one mid3 step).
    /// </summary>
    private void PopulateBitTestRegHandlers() {
        (InstructionOperation op, BitTestMutation mutation)[] bitTestOps = {
            (InstructionOperation.BT, BitTestMutation.None),
            (InstructionOperation.BTS, BitTestMutation.Set),
            (InstructionOperation.BTR, BitTestMutation.Reset),
            (InstructionOperation.BTC, BitTestMutation.Toggle),
        };
        for (int i = 0; i < 4; i++) {
            (InstructionOperation op, BitTestMutation mutation) = bitTestOps[i];
            _handlers0F[0xA3 + (i << 3)] = ctx => _bitTestRmParser.Parse(ctx, op, mutation);
        }
    }

    /// <summary>
    /// Populates double shift handlers at 0F A4/A5 (SHLD) and 0F AC/AD (SHRD).
    /// mid3 selects SHLD(4)/SHRD(5), lo3=4 → IMM8, lo3=5 → CL.
    /// </summary>
    private void PopulateDoubleShiftHandlers() {
        _handlers0F[0xA4] = ctx => _shxdRmParser.Parse(ctx, "Shld", InstructionOperation.SHLD, useImm8: true);
        _handlers0F[0xA5] = ctx => _shxdRmParser.Parse(ctx, "Shld", InstructionOperation.SHLD, useImm8: false);
        _handlers0F[0xAC] = ctx => _shxdRmParser.Parse(ctx, "Shrd", InstructionOperation.SHRD, useImm8: true);
        _handlers0F[0xAD] = ctx => _shxdRmParser.Parse(ctx, "Shrd", InstructionOperation.SHRD, useImm8: false);
    }

    /// <summary>
    /// Populates BSWAP handlers at 0F C8-CF. lo3 = register index.
    /// </summary>
    private void PopulateBswapHandlers() {
        for (int reg = 0; reg < 8; reg++) {
            int capturedReg = reg;
            _handlers0F[0xC8 + reg] = ctx => _bswapParser.Parse(ctx, regIndex: capturedReg);
        }
    }

    private CfgInstruction ParseCfgInstruction(ParsingContext context) {
        ushort opcodeValue = context.OpcodeField.Value;
        if (opcodeValue <= 0xFF) {
            Func<ParsingContext, CfgInstruction>? handler = _handlers[opcodeValue];
            if (handler != null) {
                return handler(context);
            }
        } else {
            // 0F-prefixed: extract second byte
            byte secondByte = (byte)(opcodeValue & 0xFF);
            Func<ParsingContext, CfgInstruction>? handler = _handlers0F[secondByte];
            if (handler != null) {
                return handler(context);
            }
        }

        return HandleInvalidOpcode(context);
    }

    private List<InstructionPrefix> ParsePrefixes() {
        List<InstructionPrefix> res = new();
        InstructionPrefix? nextPrefix = _parsingTools.InstructionPrefixParser.ParseNextPrefix();
        while (nextPrefix != null) {
            res.Add(nextPrefix);
            nextPrefix = _parsingTools.InstructionPrefixParser.ParseNextPrefix();
        }

        return res;
    }

    private CfgInstruction HandleInvalidOpcode(ParsingContext context) =>
        throw new CpuInvalidOpcodeException(
            $"Invalid opcode {ConvertUtils.ToHex(context.OpcodeField.Value)} at {context.Address}");

    private static void ValidateLockPrefix(CfgInstruction instruction, List<InstructionPrefix> prefixes) {
        bool hasLock = prefixes.Any(prefix => prefix is LockPrefix);
        if (!hasLock) {
            return;
        }
        InstructionOperation operation = instruction.DisplayAst.Operation;
        if (!LockAllowedOperations.Contains(operation)) {
            throw new CpuInvalidOpcodeException(
                $"LOCK prefix is invalid with instruction {operation} at {instruction.Address}: not in the LOCK-allowed instruction set");
        }
        if (!MemoryWriteDetectorVisitor.ContainsMemoryWrite(instruction.ExecutionAst)) {
            throw new CpuInvalidOpcodeException(
                $"LOCK prefix is invalid with instruction {operation} at {instruction.Address}: no memory destination operand");
        }
    }

    /// <summary>
    /// The set of instructions for which LOCK is architecturally valid,
    /// per Intel SDM Vol.2A (LOCK prefix): ADD, ADC, AND, BTC, BTR, BTS,
    /// CMPXCHG, DEC, INC, NEG, NOT, OR, SBB, SUB, XOR, XADD, XCHG.
    /// In all cases the destination must also be a memory operand.
    /// </summary>
    private static readonly HashSet<InstructionOperation> LockAllowedOperations = new() {
        InstructionOperation.ADD,
        InstructionOperation.ADC,
        InstructionOperation.AND,
        InstructionOperation.BTC,
        InstructionOperation.BTR,
        InstructionOperation.BTS,
        InstructionOperation.CMPXCHG,
        InstructionOperation.DEC,
        InstructionOperation.INC,
        InstructionOperation.NEG,
        InstructionOperation.NOT,
        InstructionOperation.OR,
        InstructionOperation.SBB,
        InstructionOperation.SUB,
        InstructionOperation.XOR,
        InstructionOperation.XADD,
        InstructionOperation.XCHG,
    };

}