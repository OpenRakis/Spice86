namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Memory.Indexable;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

public class InstructionParser : BaseInstructionParser {
    private readonly AdcAluOperationParser _adcAluOperationParser;
    private readonly AddAluOperationParser _addAluOperationParser;
    private readonly AndAluOperationParser _andAluOperationParser;
    private readonly CbwParser _cbwParser;
    private readonly CmpAluOperationParser _cmpAluOperationParser;
    private readonly CmpsParser _cmpsParser;
    private readonly CwdParser _cwdParser;
    private readonly DecRegIndexParser _decRegParser;
    private readonly Grp1Parser _grp1Parser;
    private readonly Grp2Parser _grp2Parser;
    private readonly Grp3Parser _grp3Parser;
    private readonly Grp45Parser _grp45Parser;
    private readonly ImulImm8RmParser _imm8RmParser;
    private readonly ImulImmRmParser _imulImmRmParser;
    private readonly ImulRmParser _imulRmParser;
    private readonly InAccDxParser _inAccDxParser;
    private readonly InAccImmParser _inAccImmParser;
    private readonly IncRegIndexParser _incRegParser;
    private readonly InsDxParser _insDxParser;
    private readonly JccParser _jccParser;
    private readonly JcxzParser _jcxzParser;
    private readonly LdsParser _ldsParser;
    private readonly LeaParser _leaParser;
    private readonly LeaveParser _leaveParser;
    private readonly LesParser _lesParser;
    private readonly LesParser _lssParser;
    private readonly LfsParser _lfsParser;
    private readonly LgsParser _lgsParser;
    private readonly LodsParser _lodsParser;
    private readonly LoopParser _loopParser;
    private readonly MovAccMoffsParser _movAccMoffsParser;
    private readonly MovMoffsAccParser _movMoffsAccParser;
    private readonly MovRegImmParser _movRegImmParser;
    private readonly MovRegRmParser _movRegRmParser;
    private readonly MovRmImmParser _movRmImmParser;
    private readonly MovRmRegParser _movRmRegParser;
    private readonly MovRmSignExtendByteParser _movRmSignExtendByteParser;
    private readonly MovRmSregParser _movRmSregParser;
    private readonly MovRmZeroExtendByteParser _movRmZeroExtendByteParser;
    private readonly MovsParser _movsParser;
    private readonly OrAluOperationParser _orAluOperationParser;
    private readonly OutAccDxParser _outAccDxParser;
    private readonly OutAccImmParser _outAccImmParser;
    private readonly OutsDxParser _outsDxParser;
    private readonly PopaParser _popaParser;
    private readonly PopFParser _popFParser;
    private readonly PopRegIndexParser _popRegParser;
    private readonly PopRmParser _popRmParser;
    private readonly PushaParser _pushaParser;
    private readonly PushFParser _pushFParser;
    private readonly PushImm8SignExtendedParser _pushImm8SignExtendedParser;
    private readonly PushImmParser _pushImmParser;
    private readonly PushRegIndexParser _pushRegParser;
    private readonly SbbAluOperationParser _sbbAluOperationParser;
    private readonly ScasParser _scasParser;
    private readonly SetRmccParser _setRmccParser;
    private readonly ShldClRmParser _shldClRmParser;
    private readonly ShldImm8RmParser _shldImm8RmParser;
    private readonly StosParser _stosParser;
    private readonly SubAluOperationParser _subAluOperationParser;
    private readonly TestAccImmParser _testAccImmParser;
    private readonly TestRmRegParser _testRmRegParser;
    private readonly XchgRegAccParser _xchgRegAccParser;
    private readonly XchgRmParser _xchgRmParser;
    private readonly XorAluOperationParser _xorAluOperationParser;

    public InstructionParser(IIndexable memory, State state) : base(new(memory), state) {
        _adcAluOperationParser = new(this);
        _addAluOperationParser = new(this);
        _andAluOperationParser = new(this);
        _cbwParser = new(this);
        _cmpAluOperationParser = new(this);
        _cmpsParser = new(this);
        _cwdParser = new(this);
        _decRegParser = new(this);
        _grp1Parser = new(this);
        _grp2Parser = new(this);
        _grp3Parser = new(this);
        _grp45Parser = new(this);
        _imm8RmParser = new(this);
        _imulImmRmParser = new(this);
        _imulRmParser = new(this);
        _inAccDxParser = new(this);
        _inAccImmParser = new(this);
        _incRegParser = new(this);
        _insDxParser = new(this);
        _jccParser = new(this);
        _jcxzParser = new(this);
        _ldsParser = new(this);
        _leaParser = new(this);
        _leaveParser = new(this);
        _lesParser = new(this);
        _lfsParser = new(this);
        _lgsParser = new(this);
        _lodsParser = new(this);
        _loopParser = new(this);
        _lssParser = new(this);
        _movAccMoffsParser = new(this);
        _movMoffsAccParser = new(this);
        _movRegImmParser = new(this);
        _movRegRmParser = new(this);
        _movRmImmParser = new(this);
        _movRmRegParser = new(this);
        _movRmSignExtendByteParser = new(this);
        _movRmSregParser = new(this);
        _movRmZeroExtendByteParser = new(this);
        _movsParser = new(this);
        _orAluOperationParser = new(this);
        _outAccDxParser = new(this);
        _outAccImmParser = new(this);
        _outsDxParser = new(this);
        _popaParser = new(this);
        _popFParser = new(this);
        _popRegParser = new(this);
        _popRmParser = new(this);
        _pushaParser = new(this);
        _pushFParser = new(this);
        _pushImm8SignExtendedParser = new(this);
        _pushImmParser = new(this);
        _pushRegParser = new(this);
        _sbbAluOperationParser = new(this);
        _scasParser = new(this);
        _setRmccParser = new(this);
        _shldClRmParser = new(this);
        _shldImm8RmParser = new(this);
        _stosParser = new(this);
        _subAluOperationParser = new(this);
        _testAccImmParser = new(this);
        _testRmRegParser = new(this);
        _xchgRegAccParser = new(this);
        _xchgRmParser = new(this);
        _xorAluOperationParser = new(this);
    }

    private InstructionField<ushort> ReadOpcode() {
        byte opcode = _instructionReader.UInt8.PeekField(true).Value;
        if (opcode == 0x0F || opcode == 0xDB) {
            // 0F is subopcode
            // DB is FPU opcode
            return _instructionReader.UInt16BigEndian.NextField(true);
        }

        return _instructionReader.UInt8AsUshort.NextField(true);
    }

    public CfgInstruction ParseInstructionAt(SegmentedAddress address) {
        _instructionReader.InstructionReaderAddressSource.InstructionAddress = address;
        List<InstructionPrefix> prefixes = ParsePrefixes();
        InstructionField<ushort> opcodeField = ReadOpcode();
        ParsingContext context = new(address, opcodeField, prefixes);
        CfgInstruction res = ParseCfgInstruction(context);

        res.PostInit();
        return res;
    }

    private bool HasOperandSize32(IList<InstructionPrefix> prefixes) {
        return prefixes.Where(p => p is OperandSize32Prefix).Any();
    }

    private CfgInstruction ParseCfgInstruction(ParsingContext context) {
        switch (context.OpcodeField.Value) {
            case 0x00:
            case 0x01:
            case 0x02:
            case 0x03:
            case 0x04:
            case 0x05:
                return _addAluOperationParser.Parse(context);
            case 0x06:
                return new PushSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.EsIndex);
            case 0x07:
                return new PopSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.EsIndex);
            case 0x08:
            case 0x09:
            case 0x0A:
            case 0x0B:
            case 0x0C:
            case 0x0D:
                return _orAluOperationParser.Parse(context);
            case 0x0E:
                return new PushSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.CsIndex);
            case 0x0F:
                return HandleInvalidOpcode(context);
            case 0x10:
            case 0x11:
            case 0x12:
            case 0x13:
            case 0x14:
            case 0x15:
                return _adcAluOperationParser.Parse(context);
            case 0x16:
                return new PushSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.SsIndex);
            case 0x17:
                return new PopSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.SsIndex);
            case 0x18:
            case 0x19:
            case 0x1A:
            case 0x1B:
            case 0x1C:
            case 0x1D:
                return _sbbAluOperationParser.Parse(context);
            case 0x1E:
                return new PushSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.DsIndex);
            case 0x1F:
                return new PopSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.DsIndex);
            case 0x20:
            case 0x21:
            case 0x22:
            case 0x23:
            case 0x24:
            case 0x25:
                return _andAluOperationParser.Parse(context);
            case 0x26:
                return HandleInvalidOpcodeBecausePrefix(context);
            case 0x27:
                return new Daa(context.Address, context.OpcodeField);
            case 0x28:
            case 0x29:
            case 0x2A:
            case 0x2B:
            case 0x2C:
            case 0x2D:
                return _subAluOperationParser.Parse(context);
            case 0x2E:
                return HandleInvalidOpcodeBecausePrefix(context);
            case 0x2F:
                return new Das(context.Address, context.OpcodeField);
            case 0x30:
            case 0x31:
            case 0x32:
            case 0x33:
            case 0x34:
            case 0x35:
                return _xorAluOperationParser.Parse(context);
            case 0x36:
                return HandleInvalidOpcodeBecausePrefix(context);
            case 0x37:
                return new Aaa(context.Address, context.OpcodeField);
            case 0x38:
            case 0x39:
            case 0x3A:
            case 0x3B:
            case 0x3C:
            case 0x3D:
                return _cmpAluOperationParser.Parse(context);
            case 0x3E:
                return HandleInvalidOpcodeBecausePrefix(context);
            case 0x3F:
                return new Aas(context.Address, context.OpcodeField);
            case 0x40:
            case 0x41:
            case 0x42:
            case 0x43:
            case 0x44:
            case 0x45:
            case 0x46:
            case 0x47:
                return _incRegParser.Parse(context);
            case 0x48:
            case 0x49:
            case 0x4A:
            case 0x4B:
            case 0x4C:
            case 0x4D:
            case 0x4E:
            case 0x4F:
                return _decRegParser.Parse(context);
            case 0x50:
            case 0x51:
            case 0x52:
            case 0x53:
            case 0x54:
            case 0x55:
            case 0x56:
            case 0x57:
                return _pushRegParser.Parse(context);
            case 0x58:
            case 0x59:
            case 0x5A:
            case 0x5B:
            case 0x5C:
            case 0x5D:
            case 0x5E:
            case 0x5F:
                return _popRegParser.Parse(context);
            case 0x60:
                return _pushaParser.Parse(context);
            case 0x61:
                return _popaParser.Parse(context);
            case 0x62: // BOUND
            case 0x63: // ARPL
                return HandleInvalidOpcode(context);
            case 0x64:
            case 0x65:
            case 0x66:
            case 0x67:
                return HandleInvalidOpcodeBecausePrefix(context);
            case 0x68:
                return _pushImmParser.Parse(context);
            case 0x69:
                return _imulImmRmParser.Parse(context);
            case 0x6A:
                return _pushImm8SignExtendedParser.Parse(context);
            case 0x6B:
                return _imm8RmParser.Parse(context);
            case 0x6C:
            case 0x6D:
                return _insDxParser.Parse(context);
            case 0x6E:
            case 0x6F:
                return _outsDxParser.Parse(context);
            case 0x70:
            case 0x71:
            case 0x72:
            case 0x73:
            case 0x74:
            case 0x75:
            case 0x76:
            case 0x77:
            case 0x78:
            case 0x79:
            case 0x7A:
            case 0x7B:
            case 0x7C:
            case 0x7D:
            case 0x7E:
            case 0x7F:
                return _jccParser.Parse(context);
            case 0x80:
            case 0x81:
            case 0x82:
            case 0x83:
                return _grp1Parser.Parse(context);
            case 0x84:
            case 0x85:
                return _testRmRegParser.Parse(context);
            case 0x86:
            case 0x87:
                return _xchgRmParser.Parse(context);
            case 0x88:
            case 0x89:
                return _movRmRegParser.Parse(context);
            case 0x8A:
            case 0x8B:
                return _movRegRmParser.Parse(context);
            case 0x8C:
                return _movRmSregParser.Parse(context);
            case 0x8D:
                return _leaParser.Parse(context);
            case 0x8E:
                return new MovSregRm16(context.Address, context.OpcodeField, context.Prefixes,
                    _modRmParser.ParseNext(context));
            case 0x8F:
                return _popRmParser.Parse(context);
            case 0x90:
                return new Nop(context.Address, context.OpcodeField);
            case 0x91:
            case 0x92:
            case 0x93:
            case 0x94:
            case 0x95:
            case 0x96:
            case 0x97:
                return _xchgRegAccParser.Parse(context);
            case 0x98:
                return _cbwParser.Parse(context);
            case 0x99:
                return _cwdParser.Parse(context);
            case 0x9A:
                return new CallFarImm16(context.Address, context.OpcodeField,
                    _instructionReader.SegmentedAddress.NextField(true));
            case 0x9B:
                return new Fwait(context.Address, context.OpcodeField);
            case 0x9C:
                return _pushFParser.Parse(context);
            case 0x9D:
                return _popFParser.Parse(context);
            case 0x9E:
                return new Sahf(context.Address, context.OpcodeField);
            case 0x9F:
                return new Lahf(context.Address, context.OpcodeField);
            case 0xA0:
            case 0xA1:
                return _movAccMoffsParser.Parse(context);
            case 0xA2:
            case 0xA3:
                return _movMoffsAccParser.Parse(context);
            case 0xA4:
            case 0xA5:
                return _movsParser.Parse(context);
            case 0xA6:
            case 0xA7:
                return _cmpsParser.Parse(context);
            case 0xA8:
            case 0xA9:
                return _testAccImmParser.Parse(context);
            case 0xAA:
            case 0xAB:
                return _stosParser.Parse(context);
            case 0xAC:
            case 0xAD:
                return _lodsParser.Parse(context);
            case 0xAE:
            case 0xAF:
                return _scasParser.Parse(context);
            case 0xB0:
            case 0xB1:
            case 0xB2:
            case 0xB3:
            case 0xB4:
            case 0xB5:
            case 0xB6:
            case 0xB7:
            case 0xB8:
            case 0xB9:
            case 0xBA:
            case 0xBB:
            case 0xBC:
            case 0xBD:
            case 0xBE:
            case 0xBF:
                return _movRegImmParser.ParseMovRegImm(context);
            case 0xC0:
                return _grp2Parser.Parse(context);
            case 0xC1:
                return _grp2Parser.Parse(context);
            case 0xC2:
                return new NearRetImm(context.Address, context.OpcodeField, _instructionReader.UInt16.NextField(false));
            case 0xC3:
                return new NearRet(context.Address, context.OpcodeField);
            case 0xC4:
                return _lesParser.Parse(context);
            case 0xC5:
                return _ldsParser.Parse(context);
            case 0xC6:
            case 0xC7:
                return _movRmImmParser.Parse(context);
            case 0xC8:
                InstructionField<ushort> storageField = _instructionReader.UInt16.NextField(false);
                InstructionField<byte> levelField = _instructionReader.UInt8.NextField(false);
                if (context.HasOperandSize32) {
                    return new Enter32(context.Address, context.OpcodeField, context.Prefixes, storageField,
                        levelField);
                }

                return new Enter16(context.Address, context.OpcodeField, context.Prefixes, storageField, levelField);
            case 0xC9:
                return _leaveParser.Parse(context);
            case 0xCA:
                return new FarRetImm(context.Address, context.OpcodeField, _instructionReader.UInt16.NextField(false));
            case 0xCB:
                return new FarRet(context.Address, context.OpcodeField);
            case 0xCC:
                return new Interrupt3(context.Address, context.OpcodeField);
            case 0xCD:
                return new Interrupt(context.Address, context.OpcodeField, _instructionReader.UInt8.NextField(true));
            case 0xCE:
                return new InterruptOverflow(context.Address, context.OpcodeField);
            case 0xCF:
                return new InterruptRet(context.Address, context.OpcodeField);
            case 0xD0:
                return _grp2Parser.Parse(context);
            case 0xD1:
                return _grp2Parser.Parse(context);
            case 0xD2:
                return _grp2Parser.Parse(context);
            case 0xD3:
                return _grp2Parser.Parse(context);
            case 0xD4:
                return new Aam(context.Address, context.OpcodeField, _instructionReader.UInt8.NextField(false));
            case 0xD5:
                return new Aad(context.Address, context.OpcodeField, _instructionReader.UInt8.NextField(false));
            case 0xD6:
                return new Salc(context.Address, context.OpcodeField);
            case 0xD7:
                return new Xlat(context.Address, context.OpcodeField, context.Prefixes,
                    SegmentFromPrefixesOrDs(context));
            case 0xD8:
                // FPU stuff
                return HandleInvalidOpcode(context);
            case 0xD9: {
                ModRmContext modRmContext = _modRmParser.ParseNext(context);
                int groupIndex = modRmContext.RegisterIndex;
                if (groupIndex != 7) {
                    throw new InvalidGroupIndexException(_state, groupIndex);
                }

                return new Fnstcw(context.Address, context.OpcodeField, context.Prefixes, modRmContext);
            }
            case 0xDA:
            case 0xDB:
            case 0xDC:
                // FPU stuff
                return HandleInvalidOpcode(context);
            case 0xDD: {
                ModRmContext modRmContext = _modRmParser.ParseNext(context);
                int groupIndex = modRmContext.RegisterIndex;
                if (groupIndex != 7) {
                    throw new InvalidGroupIndexException(_state, groupIndex);
                }

                return new Fnstsw(context.Address, context.OpcodeField, context.Prefixes, modRmContext);
            }
            case 0xDE:
            case 0xDF:
                // FPU stuff
                return HandleInvalidOpcode(context);
            case 0xE0:
            case 0xE1:
            case 0xE2:
                return _loopParser.Parse(context);
            case 0xE3:
                return _jcxzParser.Parse(context);
            case 0xE4:
            case 0xE5:
                return _inAccImmParser.Parse(context);
            case 0xE6:
            case 0xE7:
                return _outAccImmParser.Parse(context);
            case 0xE8:
                return new CallNearImm(context.Address, context.OpcodeField, context.Prefixes,
                    _instructionReader.Int16.NextField(true));
            case 0xE9:
                if (HasOperandSize32(context.Prefixes)) {
                    return new JmpNearImm32(context.Address, context.OpcodeField, context.Prefixes,
                        _instructionReader.Int32.NextField(true));
                }

                return new JmpNearImm16(context.Address, context.OpcodeField, context.Prefixes,
                    _instructionReader.Int16.NextField(true));
            case 0xEA:
                return new JmpFarImm(context.Address, context.OpcodeField,
                    _instructionReader.SegmentedAddress.NextField(true));
            case 0xEB:
                return new JmpNearImm8(context.Address, context.OpcodeField, context.Prefixes,
                    _instructionReader.Int8.NextField(true));
            case 0xEC:
            case 0xED:
                return _inAccDxParser.Parse(context);
            case 0xEE:
            case 0xEF:
                return _outAccDxParser.Parse(context);
            case 0xF0: // LOCK
                return HandleInvalidOpcodeBecausePrefix(context);
            case 0xF1:
                return HandleInvalidOpcode(context);
            case 0xF2:
            case 0xF3:
                return HandleInvalidOpcodeBecausePrefix(context);
            case 0xF4:
                return new Hlt(context.Address, context.OpcodeField);
            case 0xF5:
                return new Cmc(context.Address, context.OpcodeField);
            case 0xF6:
            case 0xF7:
                return _grp3Parser.Parse(context);
            case 0xF8:
                return new Clc(context.Address, context.OpcodeField);
            case 0xF9:
                return new Stc(context.Address, context.OpcodeField);
            case 0xFA:
                return new Cli(context.Address, context.OpcodeField);
            case 0xFB:
                return new Sti(context.Address, context.OpcodeField);
            case 0xFC:
                return new Cld(context.Address, context.OpcodeField);
            case 0xFD:
                return new Std(context.Address, context.OpcodeField);
            case 0xFE:
            case 0xFF:
                return _grp45Parser.Parse(context);
            // Sub-opcode
            case 0x0F80:
            case 0x0F81:
            case 0x0F82:
            case 0x0F83:
            case 0x0F84:
            case 0x0F85:
            case 0x0F86:
            case 0x0F87:
            case 0x0F88:
            case 0x0F89:
            case 0x0F8A:
            case 0x0F8B:
            case 0x0F8C:
            case 0x0F8D:
            case 0x0F8E:
            case 0x0F8F:
                return _jccParser.Parse(context);
            case 0x0F90:
            case 0x0F91:
            case 0x0F92:
            case 0x0F93:
            case 0x0F94:
            case 0x0F95:
            case 0x0F96:
            case 0x0F97:
            case 0x0F98:
            case 0x0F99:
            case 0x0F9A:
            case 0x0F9B:
            case 0x0F9C:
            case 0x0F9D:
            case 0x0F9E:
            case 0x0F9F:
                return _setRmccParser.Parse(context);
            case 0x0FA0:
                return new PushSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.FsIndex);
            case 0x0FA1:
                return new PopSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.FsIndex);
            case 0x0FA4:
                return _shldImm8RmParser.Parse(context);
            case 0x0FA5:
                return _shldClRmParser.Parse(context);
            case 0x0FA8:
                return new PushSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.GsIndex);
            case 0x0FA9:
                return new PopSReg(context.Address, context.OpcodeField, context.Prefixes,
                    (int)SegmentRegisterIndex.GsIndex);
            case 0x0FAF:
                return _imulRmParser.Parse(context);
            case 0x0FB2:
                return _lssParser.Parse(context);
            case 0x0FB4:
                return _lfsParser.Parse(context);
            case 0x0FB5:
                return _lgsParser.Parse(context);
            case 0x0FB6:
                return _movRmZeroExtendByteParser.Parse(context);
            case 0x0FB7:
                return new MovRmZeroExtendWord32(context.Address, context.OpcodeField, context.Prefixes,
                    _modRmParser.ParseNext(context));
            case 0x0FBE:
                return _movRmSignExtendByteParser.Parse(context);
            case 0x0FBF:
                return new MovRmSignExtendWord32(context.Address, context.OpcodeField, context.Prefixes,
                    _modRmParser.ParseNext(context));
            case 0xDBE3:
                return new FnInit(context.Address, context.OpcodeField, context.Prefixes);
        }

        return HandleInvalidOpcode(context);
    }

    private List<InstructionPrefix> ParsePrefixes() {
        List<InstructionPrefix> res = new();
        InstructionPrefix? nextPrefix = _instructionPrefixParser.ParseNextPrefix();
        while (nextPrefix != null) {
            res.Add(nextPrefix);
            nextPrefix = _instructionPrefixParser.ParseNextPrefix();
        }

        return res;
    }

    private CfgInstruction HandleInvalidOpcode(ParsingContext context) =>
        throw new InvalidOpCodeException(_state, context.OpcodeField.Value, false);

    private CfgInstruction HandleInvalidOpcodeBecausePrefix(ParsingContext context) =>
        throw new InvalidOpCodeException(_state, context.OpcodeField.Value, true);
}