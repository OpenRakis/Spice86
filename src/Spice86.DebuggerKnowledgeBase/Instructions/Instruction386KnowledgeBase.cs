namespace Spice86.DebuggerKnowledgeBase.Instructions;

using System;
using System.Collections.Generic;

/// <summary>
/// Static knowledge base of high-level <see cref="InstructionInfo"/> for common
/// x86 / 386 instructions, keyed by canonical mnemonic name (case-insensitive).
/// </summary>
/// <remarks>
/// Coverage now spans the full 386 integer ISA plus the 80186/80286 additions and the
/// 80387 FPU instructions that real-mode DOS programs rely on (data movement, arithmetic,
/// logic, control flow, string ops, stack, flags, segment loads, BCD adjust, array
/// bounds, the 286/386 protected-mode system instructions - LGDT/SGDT/LIDT/SIDT/LMSW/
/// SMSW/LLDT/SLDT/LTR/STR/ARPL/CLTS/LAR/LSL/VERR/VERW - and the 387 FPU's load/store/
/// arithmetic/transcendental/control instructions). 486+/Pentium-only mnemonics
/// (CPUID, RDTSC, CMPXCHG, ...) are intentionally out of scope; lookups for unknown
/// mnemonics return <c>false</c> from <see cref="TryGet"/> so callers degrade gracefully.
///
/// Mnemonic names follow the Intel/MASM convention without a leading dot. Multiple
/// aliases (e.g. JE/JZ, JA/JNBE, FCLEX/FNCLEX) are registered for the same instruction
/// so callers can pass whichever form their disassembler emits.
/// </remarks>
public static class Instruction386KnowledgeBase {
    private static readonly IReadOnlyDictionary<string, InstructionInfo> Entries = Build();

    /// <summary>
    /// Tries to retrieve the high-level <see cref="InstructionInfo"/> for the given mnemonic.
    /// Lookup is case-insensitive.
    /// </summary>
    /// <param name="mnemonic">Canonical mnemonic, e.g. "MOV", "JE", "PUSHA". Aliases are accepted.</param>
    /// <param name="info">High-level info when the method returns true; null otherwise.</param>
    public static bool TryGet(string mnemonic, out InstructionInfo? info) {
        if (string.IsNullOrEmpty(mnemonic)) {
            info = null;
            return false;
        }
        return Entries.TryGetValue(mnemonic, out info);
    }

    /// <summary>
    /// Number of mnemonic keys (including aliases) currently covered by the knowledge base.
    /// </summary>
    public static int Count => Entries.Count;

    private static IReadOnlyDictionary<string, InstructionInfo> Build() {
        Dictionary<string, InstructionInfo> map = new(StringComparer.OrdinalIgnoreCase);

        // ---------- Data movement ----------
        Add(map, "Move",
            "Copies the source operand into the destination operand.",
            "Reads source register/memory/immediate; writes destination register/memory. Does not affect flags.",
            "The most common instruction. Used to load values into registers, store registers to memory, set up arguments, and copy data.",
            "MOV");
        Add(map, "Move with Sign-Extend",
            "Copies a smaller-width source into a larger destination, replicating the source sign bit into the high bits.",
            "Reads source (8/16-bit) register or memory; writes a wider destination register. Flags unaffected.",
            "Promote a signed byte/word to a wider register without losing its sign (typical 386 idiom for signed C 'int' from 'char'/'short').",
            "MOVSX");
        Add(map, "Move with Zero-Extend",
            "Copies a smaller-width source into a larger destination, zeroing the high bits.",
            "Reads source (8/16-bit) register or memory; writes a wider destination register. Flags unaffected.",
            "Promote an unsigned byte/word to a wider register (typical 386 idiom for unsigned C from 'unsigned char'/'unsigned short').",
            "MOVZX");
        Add(map, "Load Effective Address",
            "Computes the effective address of the source memory operand and stores it (without dereferencing) in the destination register.",
            "Reads the addressing-mode components (base/index/scale/displacement); writes destination register. Flags unaffected.",
            "Compute pointers, perform fast scaled-index arithmetic (e.g. 'a*5' as LEA EAX,[EAX+EAX*4]), and combine adds in one cycle.",
            "LEA");
        Add(map, "Exchange",
            "Exchanges the two operands.",
            "Reads and writes both operands; XCHG with a memory operand is implicitly LOCKed. Flags unaffected.",
            "Swap two registers without a temporary, or perform an atomic memory swap (LOCK semantics make it a synchronization primitive).",
            "XCHG");

        // ---------- Stack ----------
        Add(map, "Push onto Stack",
            "Decrements (E)SP by the operand size and stores the source operand at SS:[(E)SP].",
            "Reads source register/memory/immediate; reads and writes (E)SP and the stack memory. Flags unaffected.",
            "Pass arguments, save registers across calls, build stack frames.",
            "PUSH");
        Add(map, "Pop off Stack",
            "Loads the value at SS:[(E)SP] into the destination operand and increments (E)SP by the operand size.",
            "Reads (E)SP and stack memory; writes destination register/memory and (E)SP. POP SS/DS/etc. masks interrupts for one instruction.",
            "Restore registers after a call, retrieve return values that were pushed, tear down stack frames.",
            "POP");
        Add(map, "Push All General-Purpose Registers",
            "Pushes AX, CX, DX, BX, original SP, BP, SI, DI in that order (PUSHA: 16-bit, PUSHAD: 32-bit).",
            "Reads all GPRs; writes (E)SP and stack memory. Flags unaffected.",
            "Quick prolog used by interrupt handlers / context switches to save the entire register file.",
            "PUSHA", "PUSHAD");
        Add(map, "Pop All General-Purpose Registers",
            "Pops in reverse order: DI, SI, BP, (skips SP), BX, DX, CX, AX (POPA: 16-bit, POPAD: 32-bit).",
            "Reads stack memory and (E)SP; writes all GPRs except SP. Flags unaffected.",
            "Counterpart to PUSHA - restore the saved register file at the end of an interrupt handler / context switch.",
            "POPA", "POPAD");
        Add(map, "Push Flags Register",
            "Pushes the FLAGS (16-bit) or EFLAGS (32-bit) register.",
            "Reads FLAGS/EFLAGS; writes (E)SP and stack memory.",
            "Save the current flag state across a call or before a CLI/STI sequence.",
            "PUSHF", "PUSHFD");
        Add(map, "Pop Flags Register",
            "Pops a value off the stack into FLAGS / EFLAGS.",
            "Reads stack memory and (E)SP; writes FLAGS/EFLAGS (some bits - IOPL, VM - are protected by privilege).",
            "Restore the saved flag state, typically at the end of an interrupt service routine.",
            "POPF", "POPFD");
        Add(map, "Make Stack Frame for Procedure Parameters",
            "Pushes BP, copies SP into BP, allocates the requested number of bytes of locals, and threads display links for nested-procedure languages.",
            "Reads/writes (E)SP, (E)BP and stack memory. Flags unaffected.",
            "High-level-language prolog (Pascal-style nested procedures). Modern compilers usually emit PUSH BP / MOV BP,SP / SUB SP,n instead.",
            "ENTER");
        Add(map, "High-Level Procedure Exit",
            "Copies (E)BP into (E)SP and pops the saved (E)BP off the stack - the inverse of the standard prolog.",
            "Reads (E)BP and stack memory; writes (E)SP and (E)BP. Flags unaffected.",
            "Function epilog: discards the entire stack frame in one instruction before RET.",
            "LEAVE");

        // ---------- Arithmetic ----------
        Add(map, "Add",
            "Adds the source operand to the destination operand and stores the result in the destination.",
            "Reads both operands; writes destination; sets OF/SF/ZF/AF/PF/CF.",
            "Plain integer addition; also used to combine pointer+offset.",
            "ADD");
        Add(map, "Add with Carry",
            "Adds source + destination + CF and stores the result in the destination.",
            "Reads both operands and CF; writes destination; sets OF/SF/ZF/AF/PF/CF.",
            "Multi-precision addition: chain ADD on the low limb with ADC on each higher limb.",
            "ADC");
        Add(map, "Subtract",
            "Subtracts the source operand from the destination operand and stores the result in the destination.",
            "Reads both operands; writes destination; sets OF/SF/ZF/AF/PF/CF.",
            "Plain subtraction; also used to allocate stack space (SUB SP, n).",
            "SUB");
        Add(map, "Subtract with Borrow",
            "Computes destination - (source + CF) and stores the result in the destination.",
            "Reads both operands and CF; writes destination; sets OF/SF/ZF/AF/PF/CF.",
            "Multi-precision subtraction (counterpart of ADC).",
            "SBB");
        Add(map, "Increment by 1",
            "Adds 1 to the operand.",
            "Reads/writes operand; sets OF/SF/ZF/AF/PF - does NOT touch CF (key difference from ADD).",
            "Cheap counter bump in loops without disturbing CF.",
            "INC");
        Add(map, "Decrement by 1",
            "Subtracts 1 from the operand.",
            "Reads/writes operand; sets OF/SF/ZF/AF/PF - does NOT touch CF.",
            "Cheap counter decrement (often paired with JNZ for loop-until-zero).",
            "DEC");
        Add(map, "Two's-Complement Negate",
            "Replaces the operand with 0 - operand.",
            "Reads/writes operand; sets OF/SF/ZF/AF/PF/CF (CF=0 if operand was 0, else 1).",
            "Compute the additive inverse of a signed integer.",
            "NEG");
        Add(map, "Unsigned Multiply",
            "Multiplies AL/AX/EAX by the source operand; the double-width result goes into AX, DX:AX, or EDX:EAX.",
            "Reads source and AL/AX/EAX; writes AX or DX:AX or EDX:EAX; sets CF/OF (others undefined).",
            "Unsigned integer multiplication; the implicit destination doubles the precision so no overflow can be lost.",
            "MUL");
        Add(map, "Signed Multiply",
            "Signed multiply. One-operand form mirrors MUL; two/three-operand forms (386+) write a same-width result into a chosen register.",
            "Reads operands (and AL/AX/EAX in the one-operand form); writes destination; sets CF/OF (others undefined).",
            "Signed multiplication; the 386 two/three-operand forms are how compilers emit '*' on signed ints without using DX/EDX.",
            "IMUL");
        Add(map, "Unsigned Divide",
            "Divides AX/DX:AX/EDX:EAX by the source; quotient goes to AL/AX/EAX, remainder to AH/DX/EDX.",
            "Reads source and the implicit dividend register pair; writes both quotient and remainder. Flags undefined. Raises #DE on divide-by-zero or overflow.",
            "Unsigned integer division; compilers emit it for unsigned '/' and '%'.",
            "DIV");
        Add(map, "Signed Divide",
            "Same as DIV but treats the operands as signed.",
            "Reads source and dividend register pair; writes quotient/remainder. Flags undefined; raises #DE.",
            "Signed integer division; compilers emit it for signed '/' and '%' (often paired with CDQ/CWD to extend the dividend).",
            "IDIV");
        Add(map, "Compare",
            "Computes destination - source like SUB but discards the result, only updating flags.",
            "Reads both operands; writes only OF/SF/ZF/AF/PF/CF.",
            "The standard preamble before a conditional jump (Jcc) - sets up the flags encoding the relation.",
            "CMP");
        Add(map, "Convert Byte to Word",
            "Sign-extends AL into AX.",
            "Reads AL; writes AH (= sign of AL). Flags unaffected.",
            "Prepare AX as a signed dividend (or operand) before IDIV / signed arithmetic on a byte.",
            "CBW");
        Add(map, "Convert Word to Doubleword",
            "Sign-extends AX into DX:AX (DX = sign of AX).",
            "Reads AX; writes DX. Flags unaffected.",
            "Set up DX:AX as a signed 32-bit dividend before IDIV.",
            "CWD");
        Add(map, "Convert Word to Doubleword Extended",
            "Sign-extends AX into EAX.",
            "Reads AX; writes EAX. Flags unaffected.",
            "386: promote a signed 16-bit value in AX to 32 bits in EAX.",
            "CWDE");
        Add(map, "Convert Doubleword to Quadword",
            "Sign-extends EAX into EDX:EAX (EDX = sign of EAX).",
            "Reads EAX; writes EDX. Flags unaffected.",
            "386: set up EDX:EAX as a signed 64-bit dividend before IDIV.",
            "CDQ");

        // ---------- Logic ----------
        Add(map, "Bitwise AND",
            "Bitwise AND of source and destination.",
            "Reads both operands; writes destination; sets SF/ZF/PF, clears OF/CF, AF undefined.",
            "Mask off bits, test bit groups (often with a discarded result via TEST), implement logical AND on bool flags.",
            "AND");
        Add(map, "Bitwise OR",
            "Bitwise OR of source and destination.",
            "Reads both operands; writes destination; sets SF/ZF/PF, clears OF/CF, AF undefined.",
            "Set selected bits (flag merging), combine bitfields.",
            "OR");
        Add(map, "Bitwise Exclusive-OR",
            "Bitwise XOR of source and destination.",
            "Reads both operands; writes destination; sets SF/ZF/PF, clears OF/CF, AF undefined.",
            "Toggle selected bits; the idiom 'XOR reg, reg' is the canonical 2-byte 'set register to zero' (also clears the data dependency).",
            "XOR");
        Add(map, "Bitwise NOT",
            "Bitwise complement (one's-complement) of the operand.",
            "Reads/writes operand. Flags unaffected.",
            "Invert all bits; build masks; counterpart of NEG that does not affect flags.",
            "NOT");
        Add(map, "Logical Compare",
            "Computes (destination AND source) like AND but discards the result, only updating flags.",
            "Reads both operands; writes SF/ZF/PF, clears OF/CF, AF undefined.",
            "Check whether selected bits are set (TEST AL, mask) without clobbering AL - the standard preamble before a Jcc on bit tests.",
            "TEST");

        // ---------- Shifts and rotates ----------
        Add(map, "Shift Logical/Arithmetic Left",
            "Shifts the destination left by the count, filling the low bits with 0; the last bit shifted out goes to CF.",
            "Reads destination and count; writes destination; sets CF, SF, ZF, PF, OF (count=1).",
            "Multiply by powers of two; pack bitfields.",
            "SHL", "SAL");
        Add(map, "Shift Logical Right",
            "Shifts the destination right by the count, filling the high bits with 0; the last bit shifted out goes to CF.",
            "Reads destination and count; writes destination; sets CF, SF, ZF, PF, OF (count=1).",
            "Unsigned divide by powers of two; extract bitfields.",
            "SHR");
        Add(map, "Shift Arithmetic Right",
            "Shifts the destination right by the count, replicating the sign bit into the high bits.",
            "Reads destination and count; writes destination; sets CF, SF, ZF, PF, OF (count=1).",
            "Signed divide by powers of two without losing the sign.",
            "SAR");
        Add(map, "Rotate Left",
            "Rotates the destination left; bits shifted out of the high end re-enter at the low end and also land in CF.",
            "Reads destination and count; writes destination; sets CF and OF (count=1).",
            "Realign bitfields, hash construction, manual bit rotation.",
            "ROL");
        Add(map, "Rotate Right",
            "Rotates the destination right; bits shifted out of the low end re-enter at the high end and also land in CF.",
            "Reads destination and count; writes destination; sets CF and OF (count=1).",
            "Realign bitfields, hash construction, manual bit rotation.",
            "ROR");
        Add(map, "Rotate through Carry Left",
            "Rotates the destination + CF left as a single (operand_size+1)-bit value.",
            "Reads destination, count and CF; writes destination and CF.",
            "Multi-precision shifts (chain across multiple registers via the carry).",
            "RCL");
        Add(map, "Rotate through Carry Right",
            "Rotates the destination + CF right as a single (operand_size+1)-bit value.",
            "Reads destination, count and CF; writes destination and CF.",
            "Multi-precision shifts (chain across multiple registers via the carry).",
            "RCR");
        Add(map, "Double-Precision Shift Left",
            "386: shifts destination left by N bits, feeding in the high N bits of the source from the right.",
            "Reads destination, source and count; writes destination; flag effects mirror SHL.",
            "Combine two 16/32-bit values into a wider shift in one instruction (multi-precision shifts, fast bit-stream packing).",
            "SHLD");
        Add(map, "Double-Precision Shift Right",
            "386: shifts destination right by N bits, feeding in the low N bits of the source from the left.",
            "Reads destination, source and count; writes destination; flag effects mirror SHR.",
            "Combine two 16/32-bit values into a wider shift (multi-precision shifts, fast bit-stream extraction).",
            "SHRD");

        // ---------- Bit manipulation (386) ----------
        Add(map, "Bit Test",
            "Copies the selected bit from the destination into CF.",
            "Reads destination and bit index; writes CF.",
            "Test individual bits in a register/memory bitmap (the canonical 386 'is bit N set?').",
            "BT");
        Add(map, "Bit Test and Set",
            "Copies the selected bit into CF, then sets it in the destination.",
            "Reads/writes destination; writes CF.",
            "Atomic-style bit set on a bitmap (with LOCK prefix it becomes truly atomic).",
            "BTS");
        Add(map, "Bit Test and Reset",
            "Copies the selected bit into CF, then clears it in the destination.",
            "Reads/writes destination; writes CF.",
            "Bit clear on a bitmap (LOCK-prefixable).",
            "BTR");
        Add(map, "Bit Test and Complement",
            "Copies the selected bit into CF, then flips it in the destination.",
            "Reads/writes destination; writes CF.",
            "Toggle a bit in a bitmap.",
            "BTC");
        Add(map, "Bit Scan Forward",
            "Searches the source for the lowest-numbered set bit and stores its index in the destination.",
            "Reads source; writes destination; sets ZF (=1 if source was 0; destination then undefined).",
            "Find the index of the first set bit (count trailing zeros); used in scheduler bitmaps and 'find first free' routines.",
            "BSF");
        Add(map, "Bit Scan Reverse",
            "Searches the source for the highest-numbered set bit and stores its index in the destination.",
            "Reads source; writes destination; sets ZF (=1 if source was 0).",
            "Find the index of the most significant set bit; used to compute integer log2 / floor(log2(n)).",
            "BSR");

        // ---------- Control flow ----------
        Add(map, "Unconditional Jump",
            "Transfers control to the target. Near forms only change (E)IP; far forms also reload CS.",
            "Reads target operand; writes (E)IP (and CS for far jumps). Flags unaffected.",
            "Branch unconditionally: end of an else-clause, switch dispatcher, tail call, far jumps to BIOS/DOS.",
            "JMP");
        Add(map, "Call Procedure",
            "Pushes the return address (and CS for far calls) onto the stack, then transfers control to the target.",
            "Reads target; writes (E)SP, stack memory, (E)IP (and CS for far calls). Flags unaffected.",
            "Invoke a function - the caller side of the calling convention.",
            "CALL");
        Add(map, "Return from Near Procedure",
            "Pops (E)IP off the stack and resumes execution there; the optional immediate releases that many additional bytes of arguments.",
            "Reads stack memory and (E)SP; writes (E)IP and (E)SP. Flags unaffected.",
            "Function epilog (near). The optional argument count implements callee-cleans-up calling conventions like Pascal/__stdcall.",
            "RET", "RETN");
        Add(map, "Return from Far Procedure",
            "Pops (E)IP and CS off the stack; optional immediate releases additional argument bytes.",
            "Reads stack memory and (E)SP; writes (E)IP, CS and (E)SP. Flags unaffected.",
            "Counterpart of CALL FAR - return across a segment boundary (DOS / BIOS / DLL-style segmented code).",
            "RETF");
        Add(map, "Interrupt Return",
            "Pops (E)IP, CS and FLAGS/EFLAGS off the stack - restoring the entire pre-interrupt context.",
            "Reads stack memory and (E)SP; writes (E)IP, CS, FLAGS/EFLAGS and (E)SP.",
            "End-of-handler instruction for hardware and software interrupts; required to leave an INT handler.",
            "IRET", "IRETD");
        Add(map, "Loop According to (E)CX",
            "Decrements (E)CX; if the result is non-zero, jumps to the short target.",
            "Reads/writes (E)CX; reads target; writes (E)IP. Flags unaffected.",
            "Compact 'count down (E)CX times' construct; modern compilers prefer DEC + JNZ for speed but LOOP is common in DOS code.",
            "LOOP");
        Add(map, "Loop While Equal / Zero",
            "Decrements (E)CX; jumps if (E)CX != 0 AND ZF = 1.",
            "Reads/writes (E)CX; reads ZF and target; writes (E)IP.",
            "Search loops that exit either when the count runs out or when a comparison stops being equal.",
            "LOOPE", "LOOPZ");
        Add(map, "Loop While Not Equal / Not Zero",
            "Decrements (E)CX; jumps if (E)CX != 0 AND ZF = 0.",
            "Reads/writes (E)CX; reads ZF and target; writes (E)IP.",
            "Scan-until-found loops (often paired with REPNE-style logic).",
            "LOOPNE", "LOOPNZ");
        Add(map, "Jump if (E)CX is Zero",
            "Jumps to the short target when (E)CX is 0 (does NOT decrement).",
            "Reads (E)CX and target; writes (E)IP. Flags unaffected.",
            "Pre-loop guard: skip an entire string-op / LOOP body when the count is already zero.",
            "JCXZ", "JECXZ");

        // Conditional jumps
        AddJcc(map, "Jump if Equal / Zero", "ZF = 1", "JE", "JZ");
        AddJcc(map, "Jump if Not Equal / Not Zero", "ZF = 0", "JNE", "JNZ");
        AddJcc(map, "Jump if Above (unsigned >)", "CF = 0 AND ZF = 0", "JA", "JNBE");
        AddJcc(map, "Jump if Above-or-Equal (unsigned >=)", "CF = 0", "JAE", "JNB", "JNC");
        AddJcc(map, "Jump if Below (unsigned <)", "CF = 1", "JB", "JNAE", "JC");
        AddJcc(map, "Jump if Below-or-Equal (unsigned <=)", "CF = 1 OR ZF = 1", "JBE", "JNA");
        AddJcc(map, "Jump if Greater (signed >)", "ZF = 0 AND SF = OF", "JG", "JNLE");
        AddJcc(map, "Jump if Greater-or-Equal (signed >=)", "SF = OF", "JGE", "JNL");
        AddJcc(map, "Jump if Less (signed <)", "SF != OF", "JL", "JNGE");
        AddJcc(map, "Jump if Less-or-Equal (signed <=)", "ZF = 1 OR SF != OF", "JLE", "JNG");
        AddJcc(map, "Jump if Overflow", "OF = 1", "JO");
        AddJcc(map, "Jump if Not Overflow", "OF = 0", "JNO");
        AddJcc(map, "Jump if Sign (negative)", "SF = 1", "JS");
        AddJcc(map, "Jump if Not Sign (non-negative)", "SF = 0", "JNS");
        AddJcc(map, "Jump if Parity Even", "PF = 1", "JP", "JPE");
        AddJcc(map, "Jump if Parity Odd", "PF = 0", "JNP", "JPO");

        // SETcc family (386)
        Add(map, "Set Byte if Equal / Zero",
            "Writes 1 to the byte destination if ZF = 1, else 0.",
            "Reads ZF; writes destination byte. Flags unaffected.",
            "Materialize a Boolean from a comparison without a branch (compilers emit this for C '==' assigning to a 'bool').",
            "SETE", "SETZ");
        Add(map, "Set Byte if Not Equal",
            "Writes 1 if ZF = 0, else 0.",
            "Reads ZF; writes destination byte.",
            "Branchless materialization of '!='.",
            "SETNE", "SETNZ");

        // ---------- Flags ----------
        Add(map, "Clear Carry Flag",
            "Sets CF = 0.",
            "Writes CF.",
            "Prepare CF=0 before a multi-precision ADC chain, or signal 'success' to a DOS/BIOS caller (CF=0 = success convention).",
            "CLC");
        Add(map, "Set Carry Flag",
            "Sets CF = 1.",
            "Writes CF.",
            "Signal 'error' on return from a DOS/BIOS handler (CF=1 means failure under the DOS calling convention).",
            "STC");
        Add(map, "Complement Carry Flag",
            "Inverts CF.",
            "Reads/writes CF.",
            "Toggle the carry result without recomputing it.",
            "CMC");
        Add(map, "Clear Direction Flag",
            "Sets DF = 0 (string ops auto-increment SI/DI).",
            "Writes DF.",
            "Standard prelude before string ops (MOVS/STOS/CMPS/SCAS/LODS) so they advance forward; the System V/DOS ABI requires DF=0 on calls.",
            "CLD");
        Add(map, "Set Direction Flag",
            "Sets DF = 1 (string ops auto-decrement SI/DI).",
            "Writes DF.",
            "Make string ops walk backward - used for overlapping-buffer copies that must go right-to-left.",
            "STD");
        Add(map, "Clear Interrupt Flag",
            "Sets IF = 0 - masks maskable hardware interrupts.",
            "Writes IF.",
            "Enter a critical section (e.g. while reprogramming the PIC, IVT, or modifying shared data with an ISR).",
            "CLI");
        Add(map, "Set Interrupt Flag",
            "Sets IF = 1 - re-enables maskable hardware interrupts (with one-instruction delay).",
            "Writes IF.",
            "Leave a critical section / re-enable interrupts at the start of a long ISR.",
            "STI");
        Add(map, "Load Status Flags into AH",
            "Loads SF, ZF, AF, PF and CF (low byte of FLAGS) into AH.",
            "Reads FLAGS; writes AH.",
            "Snapshot the arithmetic flags into a register without going through memory.",
            "LAHF");
        Add(map, "Store AH into Flags",
            "Stores AH into SF, ZF, AF, PF, CF.",
            "Reads AH; writes those flag bits.",
            "Restore flags previously saved by LAHF; also used historically to feed x87 compare results to integer Jcc.",
            "SAHF");

        // ---------- String operations ----------
        Add(map, "Move String",
            "Copies the byte/word/dword at DS:[(E)SI] to ES:[(E)DI] and advances both pointers per DF.",
            "Reads DS:[SI], ES:[DI], DF; writes ES:[DI], (E)SI, (E)DI.",
            "memcpy-style copy. With a REP prefix and (E)CX = length, this is the canonical fast block move.",
            "MOVS", "MOVSB", "MOVSW", "MOVSD");
        Add(map, "Store String",
            "Stores AL/AX/EAX to ES:[(E)DI] and advances DI per DF.",
            "Reads AL/AX/EAX, DF; writes ES:[DI], (E)DI.",
            "memset-style fill. With REP and (E)CX, fast block fill.",
            "STOS", "STOSB", "STOSW", "STOSD");
        Add(map, "Load String",
            "Loads the byte/word/dword at DS:[(E)SI] into AL/AX/EAX and advances SI per DF.",
            "Reads DS:[SI], DF; writes AL/AX/EAX and (E)SI.",
            "Single-step pull from a source buffer in unrolled string loops.",
            "LODS", "LODSB", "LODSW", "LODSD");
        Add(map, "Compare String",
            "Compares DS:[(E)SI] with ES:[(E)DI] (sets flags as if SUB), advances both pointers.",
            "Reads DS:[SI], ES:[DI], DF; writes flags, (E)SI, (E)DI.",
            "memcmp-style compare. With REPE/REPNE and (E)CX, scan two buffers for first mismatch / first match.",
            "CMPS", "CMPSB", "CMPSW", "CMPSD");
        Add(map, "Scan String",
            "Compares AL/AX/EAX against ES:[(E)DI] (sets flags as if SUB), advances DI per DF.",
            "Reads AL/AX/EAX, ES:[DI], DF; writes flags and (E)DI.",
            "memchr / strchr style search. With REPNE and (E)CX = max length, walk a buffer for a sentinel byte.",
            "SCAS", "SCASB", "SCASW", "SCASD");
        Add(map, "Input from Port to String",
            "Reads a byte/word/dword from port DX and stores it at ES:[(E)DI], advancing DI per DF.",
            "Reads DX (port) and DF; writes ES:[DI] and (E)DI.",
            "Block transfer from an I/O port into memory (with REP prefix, fast PIO from a device).",
            "INS", "INSB", "INSW", "INSD");
        Add(map, "Output String to Port",
            "Reads a byte/word/dword from DS:[(E)SI] and writes it to port DX, advancing SI per DF.",
            "Reads DS:[SI], DX (port), DF; writes the I/O port and (E)SI.",
            "Block transfer from memory to an I/O port (fast PIO out).",
            "OUTS", "OUTSB", "OUTSW", "OUTSD");
        Add(map, "Repeat String Operation",
            "Prefix that repeats the following string instruction (E)CX times, decrementing (E)CX each iteration. REPE/REPZ also requires ZF=1 to continue; REPNE/REPNZ requires ZF=0.",
            "Reads/writes (E)CX; reads/writes ZF (REPE/REPNE forms).",
            "Turn a single MOVS/STOS/CMPS/SCAS into a hardware-accelerated block operation.",
            "REP", "REPE", "REPZ", "REPNE", "REPNZ");

        // ---------- Segment / system / I/O ----------
        Add(map, "Load Far Pointer using DS",
            "Loads a 32-bit (real-mode) far pointer from memory into DS:reg.",
            "Reads memory; writes DS and the destination register.",
            "Dereference a far pointer in DOS code (e.g. char far*) - loads segment:offset together.",
            "LDS");
        Add(map, "Load Far Pointer using ES",
            "Loads a far pointer from memory into ES:reg.",
            "Reads memory; writes ES and the destination register.",
            "Address a destination buffer that lives in another segment (typical before string ops / video memory writes).",
            "LES");
        Add(map, "Load Far Pointer using FS (386)",
            "Loads a far pointer into FS:reg.",
            "Reads memory; writes FS and the destination register.",
            "Use the 386 extra segment register FS as a side channel (e.g. thread-local pointers in protected mode).",
            "LFS");
        Add(map, "Load Far Pointer using GS (386)",
            "Loads a far pointer into GS:reg.",
            "Reads memory; writes GS and the destination register.",
            "Use the 386 extra segment register GS as a side channel.",
            "LGS");
        Add(map, "Load Far Pointer using SS",
            "Loads a far pointer into SS:reg (typically SS:SP) atomically with respect to interrupts.",
            "Reads memory; writes SS and the destination register; masks interrupts for one instruction.",
            "Switch stack atomically: load SS:SP from memory without exposing a transient bad SS to a possibly-firing interrupt.",
            "LSS");
        Add(map, "Software Interrupt",
            "Pushes FLAGS, CS, IP, clears IF/TF, and dispatches through IVT entry n (real mode) or IDT entry n (protected mode).",
            "Reads IVT/IDT, vector n; writes (E)SP, stack memory, CS, (E)IP, IF, TF.",
            "Invoke DOS / BIOS / device-driver services via the standard interrupt API (e.g. INT 21h DOS, INT 10h video).",
            "INT");
        Add(map, "Interrupt on Overflow",
            "If OF = 1, raises INT 4; otherwise a no-op.",
            "Reads OF; conditionally behaves like INT 4.",
            "Trap signed overflow without an explicit Jcc + INT pair (rarely used outside historic compilers).",
            "INTO");
        Add(map, "Input from I/O Port",
            "Reads a byte/word/dword from the I/O port specified by an immediate or DX into AL/AX/EAX.",
            "Reads the I/O port and DX (DX-form); writes AL/AX/EAX. Flags unaffected.",
            "Talk to hardware: VGA registers, PIT, PIC, sound cards, GUS, MPU-401, etc.",
            "IN");
        Add(map, "Output to I/O Port",
            "Writes AL/AX/EAX to the I/O port specified by an immediate or DX.",
            "Reads AL/AX/EAX and DX; writes the I/O port. Flags unaffected.",
            "Configure or drive hardware (mode set, palette program, DMA controller setup, sound register writes, ...).",
            "OUT");
        Add(map, "Halt",
            "Halts execution until the next external interrupt.",
            "Reads IF; stops fetching until an interrupt arrives.",
            "Idle the CPU when there is no work to do (e.g. inside the BIOS POST timer wait or a custom idle loop).",
            "HLT");
        Add(map, "No Operation",
            "Does nothing (encoded as XCHG (E)AX,(E)AX).",
            "No reads/writes. Flags unaffected.",
            "Padding for alignment, hot-patching slots, branch-delay timing, or removed code.",
            "NOP");
        Add(map, "Wait for Coprocessor",
            "Synchronizes the CPU with the x87 FPU; checks for pending unmasked FPU exceptions.",
            "Reads x87 status.",
            "Insert before a CPU-side load of FPU memory results to ensure the FPU has finished writing them.",
            "WAIT", "FWAIT");
        Add(map, "Table Lookup Translation",
            "Replaces AL with the byte at DS:[(E)BX + AL].",
            "Reads DS:[(E)BX + AL]; writes AL. Flags unaffected.",
            "Single-instruction byte lookup table - used for character translation, code-page conversion, encoding tables.",
            "XLAT", "XLATB");

        // ---------- BCD / decimal adjust (8086) ----------
        Add(map, "ASCII Adjust After Addition",
            "Adjusts AL after an unpacked-BCD addition: if the low nibble > 9 or AF=1, adds 6, propagates a carry into AH and sets AF/CF.",
            "Reads/writes AL, AH, AF, CF. OF/SF/ZF/PF undefined.",
            "Implement unpacked-BCD addition (one decimal digit per byte) - used by old COBOL/BASIC arithmetic on the 8086.",
            "AAA");
        Add(map, "ASCII Adjust After Subtraction",
            "Adjusts AL after an unpacked-BCD subtraction: if the low nibble > 9 or AF=1, subtracts 6 and propagates a borrow into AH.",
            "Reads/writes AL, AH, AF, CF. OF/SF/ZF/PF undefined.",
            "Implement unpacked-BCD subtraction.",
            "AAS");
        Add(map, "ASCII Adjust After Multiplication",
            "After MUL of two unpacked-BCD digits, divides AL by an immediate base (default 10): AH = AL / base, AL = AL % base.",
            "Reads AL and the immediate base; writes AL, AH, SF, ZF, PF. AF/CF/OF undefined.",
            "Convert a binary product into two unpacked-BCD digits - typical after MUL of two single-digit values.",
            "AAM");
        Add(map, "ASCII Adjust Before Division",
            "Combines AH:AL into a single binary value for an upcoming DIV: AL = AH*base + AL, AH = 0 (default base 10).",
            "Reads AH/AL and the immediate base; writes AL, AH, SF, ZF, PF. AF/CF/OF undefined.",
            "Pre-process a two-digit unpacked-BCD value (one digit per byte) so DIV can produce a digit and a remainder.",
            "AAD");
        Add(map, "Decimal Adjust After Addition",
            "Adjusts AL after a packed-BCD addition: corrects each nibble that overflowed past 9, updating AF and CF.",
            "Reads/writes AL, AF, CF, SF, ZF, PF. OF undefined.",
            "Implement packed-BCD addition (two decimal digits per byte) - widely used by mainframe-era code.",
            "DAA");
        Add(map, "Decimal Adjust After Subtraction",
            "Adjusts AL after a packed-BCD subtraction by undoing nibble underflows, updating AF and CF.",
            "Reads/writes AL, AF, CF, SF, ZF, PF. OF undefined.",
            "Implement packed-BCD subtraction.",
            "DAS");

        // ---------- 80186 array bounds ----------
        Add(map, "Check Array Index Against Bounds",
            "Compares the signed register operand against a [lower, upper] pair in memory; raises INT 5 if out of range.",
            "Reads register and the bounds pair from memory; on failure pushes flags/CS/IP and dispatches INT 5.",
            "Cheap range check - emitted by Pascal/Modula range-check code and 80186-targeted compilers.",
            "BOUND");

        // ---------- 80286 / 80386 protected-mode system ----------
        Add(map, "Adjust Requested Privilege Level",
            "Forces the RPL field of the destination selector to be at least as large as the RPL of the source selector; sets ZF if the destination changed.",
            "Reads source and destination selector RPL fields; writes destination RPL and ZF.",
            "OS kernel sanitizing a user-supplied selector before using it on the user's behalf, so a low-privilege caller cannot spoof a high-privilege selector.",
            "ARPL");
        Add(map, "Clear Task-Switched Flag",
            "Clears CR0.TS so subsequent x87/SSE instructions do not raise #NM.",
            "Reads/writes CR0 (TS bit). CPL=0 instruction.",
            "Run inside an FPU context-switch handler (#NM) once the kernel has restored or determined it does not need to restore the FPU state.",
            "CLTS");
        Add(map, "Load Access Rights Byte",
            "Loads the access-rights field of the descriptor referenced by the source selector into the destination register, masked appropriately; sets ZF on success.",
            "Reads GDT/LDT through the source selector; writes destination register and ZF. CPL- and DPL-checked.",
            "OS code or a debugger validating a selector and inspecting its descriptor's type/DPL/granularity bits.",
            "LAR");
        Add(map, "Load Segment Limit",
            "Loads the (scaled) segment limit of the descriptor referenced by the source selector into the destination register; sets ZF on success.",
            "Reads GDT/LDT through the source selector; writes destination register and ZF. CPL- and DPL-checked.",
            "Bounds-check a pointer against the actual segment limit at runtime without trusting the selector's RPL alone.",
            "LSL");
        Add(map, "Verify Segment for Reading",
            "Sets ZF=1 if the segment referenced by the source selector is readable from the current CPL, else ZF=0.",
            "Reads GDT/LDT and CPL; writes ZF.",
            "OS / loader validates a user-supplied selector before issuing a read through it.",
            "VERR");
        Add(map, "Verify Segment for Writing",
            "Sets ZF=1 if the segment referenced by the source selector is writable from the current CPL, else ZF=0.",
            "Reads GDT/LDT and CPL; writes ZF.",
            "OS / loader validates a user-supplied selector before issuing a write through it.",
            "VERW");
        Add(map, "Load Global Descriptor Table Register",
            "Loads the 6-byte GDT pseudo-descriptor (limit + base) from memory into GDTR.",
            "Reads memory; writes GDTR. CPL=0 instruction.",
            "Set up the GDT during boot or a kernel mode switch (real <-> protected).",
            "LGDT");
        Add(map, "Store Global Descriptor Table Register",
            "Stores the contents of GDTR into a 6-byte memory operand.",
            "Reads GDTR; writes memory.",
            "Snapshot the GDTR for later restore (rarely needed - mostly a hypervisor / debugger probe).",
            "SGDT");
        Add(map, "Load Interrupt Descriptor Table Register",
            "Loads the 6-byte IDT pseudo-descriptor (limit + base) from memory into IDTR.",
            "Reads memory; writes IDTR. CPL=0 instruction.",
            "Install or replace the protected-mode IDT (e.g. when entering protected mode from a DOS-extender).",
            "LIDT");
        Add(map, "Store Interrupt Descriptor Table Register",
            "Stores the contents of IDTR into a 6-byte memory operand.",
            "Reads IDTR; writes memory.",
            "Snapshot the IDTR for later restore - or for hypervisor/debugger detection (Red Pill technique).",
            "SIDT");
        Add(map, "Load Local Descriptor Table Register",
            "Loads LDTR with the selector for the current task's LDT (or the null selector to disable the LDT).",
            "Reads source selector and the GDT entry it points to; writes LDTR. CPL=0 instruction.",
            "Switch the per-task LDT during a task switch in segmented protected-mode kernels.",
            "LLDT");
        Add(map, "Store Local Descriptor Table Register",
            "Stores the LDTR selector value into the destination operand.",
            "Reads LDTR; writes destination.",
            "Save the current LDT selector before switching it (kernel task switch / debugger).",
            "SLDT");
        Add(map, "Load Task Register",
            "Loads TR with the selector of a TSS descriptor and marks that descriptor busy.",
            "Reads source selector and the TSS descriptor in the GDT; writes TR and the descriptor's busy bit. CPL=0 instruction.",
            "Establish the current task's TSS - required before the CPU can do hardware task switches or use the IO permission bitmap.",
            "LTR");
        Add(map, "Store Task Register",
            "Stores the TR selector value into the destination operand.",
            "Reads TR; writes destination.",
            "Save the current TSS selector (kernel task switch / debugger probe).",
            "STR");
        Add(map, "Load Machine Status Word",
            "Loads the lower 16 bits of CR0 (PE, MP, EM, TS, ET) from a register or memory.",
            "Reads source; writes CR0[15:0]. CPL=0 instruction.",
            "Legacy 286 way to enter protected mode (set PE=1) without touching the high bits of CR0; preserved for backward compatibility on the 386.",
            "LMSW");
        Add(map, "Store Machine Status Word",
            "Stores the lower 16 bits of CR0 into the destination operand.",
            "Reads CR0[15:0]; writes destination.",
            "Read PE/MP/EM/TS without needing CPL=0 (SMSW is unprivileged) - older code uses this to detect protected mode.",
            "SMSW");

        // ---------- 80387 FPU - data transfer ----------
        Add(map, "Load Floating-Point Value",
            "Pushes the source (memory float / register ST(i)) onto the x87 stack as ST(0).",
            "Reads source; writes ST(0); decrements TOP. May set C1.",
            "Bring an operand onto the FPU stack before any arithmetic - the FPU is stack-based, so almost every FP routine starts with FLD.",
            "FLD");
        Add(map, "Store Floating-Point Value",
            "Stores ST(0) to the destination (memory float or ST(i)) without popping the stack.",
            "Reads ST(0); writes destination.",
            "Save an intermediate FP result while keeping it available on the FPU stack for further use.",
            "FST");
        Add(map, "Store Floating-Point and Pop",
            "Stores ST(0) to the destination, then pops the FPU stack.",
            "Reads ST(0); writes destination; increments TOP.",
            "Final write of an FP result back to memory - the typical end of an FP expression.",
            "FSTP");
        Add(map, "Load Integer",
            "Converts a signed integer in memory to extended-precision float and pushes it onto the FPU stack.",
            "Reads memory; writes ST(0); decrements TOP.",
            "Bring an integer (16/32/64-bit) onto the FPU stack to mix with floating-point math (e.g. C 'double f(int x)').",
            "FILD");
        Add(map, "Store Integer",
            "Rounds ST(0) to an integer (per current rounding mode) and stores it to memory; ST(0) is preserved.",
            "Reads ST(0); writes memory.",
            "Convert a floating-point intermediate to integer without consuming the value on the FPU stack.",
            "FIST");
        Add(map, "Store Integer and Pop",
            "Rounds ST(0) to an integer and stores it to memory; pops the FPU stack.",
            "Reads ST(0); writes memory; increments TOP.",
            "Final FP-to-int conversion at the end of an expression - typical for C '(int)x'.",
            "FISTP");
        Add(map, "Load BCD",
            "Loads a 10-byte packed-BCD integer from memory and pushes it onto the FPU stack.",
            "Reads memory; writes ST(0); decrements TOP.",
            "Read a packed-BCD constant (e.g. for old database/financial code) into the FPU.",
            "FBLD");
        Add(map, "Store BCD and Pop",
            "Stores ST(0) to memory as a 10-byte packed-BCD integer; pops the FPU stack.",
            "Reads ST(0); writes memory; increments TOP.",
            "Output a BCD-formatted result for legacy printing or financial code.",
            "FBSTP");
        Add(map, "Exchange Floating-Point Registers",
            "Exchanges ST(0) with ST(i) (default ST(1)).",
            "Reads/writes ST(0) and ST(i).",
            "Reorder operands on the FPU stack so the next instruction operates on the right value (the stack-based FPU forces lots of FXCH).",
            "FXCH");
        Add(map, "Free Floating-Point Register",
            "Marks ST(i) as empty in the FPU tag word without changing TOP.",
            "Writes the tag-word entry for ST(i).",
            "Discard a stack slot the compiler knows is dead, avoiding a stack-overflow exception on the next push.",
            "FFREE");
        Add(map, "Decrement Stack-Top Pointer",
            "Decrements TOP without reading or writing any data.",
            "Writes TOP and C1.",
            "Manipulate the FPU stack pointer manually - rare, used by some hand-written FP libraries.",
            "FDECSTP");
        Add(map, "Increment Stack-Top Pointer",
            "Increments TOP without freeing the popped slot.",
            "Writes TOP and C1.",
            "Manipulate the FPU stack pointer manually - rare, used by some hand-written FP libraries.",
            "FINCSTP");

        // ---------- 80387 FPU - constants ----------
        Add(map, "Load +0.0", "Pushes +0.0 onto the FPU stack.", "Writes ST(0); decrements TOP.",
            "Initialize an FP accumulator without a memory load.", "FLDZ");
        Add(map, "Load +1.0", "Pushes +1.0 onto the FPU stack.", "Writes ST(0); decrements TOP.",
            "Initialize an FP product accumulator or supply 1.0 for transcendental sequences.", "FLD1");
        Add(map, "Load Pi", "Pushes the constant pi onto the FPU stack.", "Writes ST(0); decrements TOP.",
            "Built-in pi for trig argument reduction or angle conversion.", "FLDPI");
        Add(map, "Load log2(e)", "Pushes log2(e) onto the FPU stack.", "Writes ST(0); decrements TOP.",
            "Helper constant for computing exp(x) via 2^(x*log2(e)) with F2XM1.", "FLDL2E");
        Add(map, "Load log2(10)", "Pushes log2(10) onto the FPU stack.", "Writes ST(0); decrements TOP.",
            "Helper constant for computing 10^x via 2^(x*log2(10)).", "FLDL2T");
        Add(map, "Load ln(2)", "Pushes ln(2) onto the FPU stack.", "Writes ST(0); decrements TOP.",
            "Helper constant for natural-logarithm scaling alongside FYL2X.", "FLDLN2");
        Add(map, "Load log10(2)", "Pushes log10(2) onto the FPU stack.", "Writes ST(0); decrements TOP.",
            "Helper constant for base-10 logarithm scaling alongside FYL2X.", "FLDLG2");

        // ---------- 80387 FPU - arithmetic ----------
        Add(map, "Floating-Point Add",
            "Adds the source to the destination FPU register (default ST(0) += source).",
            "Reads two FPU values; writes the destination FPU register.",
            "Floating-point addition - the basic FP arithmetic primitive.",
            "FADD", "FIADD");
        Add(map, "Floating-Point Add and Pop",
            "Adds ST(0) to ST(i), stores the result in ST(i), then pops the stack.",
            "Reads ST(0) and ST(i); writes ST(i); increments TOP.",
            "End an FP add expression by collapsing two stack slots into one.",
            "FADDP");
        Add(map, "Floating-Point Subtract",
            "Subtracts the source from the destination FPU register.",
            "Reads two FPU values; writes destination.",
            "Floating-point subtraction.",
            "FSUB", "FISUB");
        Add(map, "Floating-Point Reverse Subtract",
            "Subtracts the destination from the source: dest = source - dest.",
            "Reads two FPU values; writes destination.",
            "Compute (memory - ST(0)) without an extra FXCH - emitted often by C compilers.",
            "FSUBR", "FISUBR");
        Add(map, "Floating-Point Subtract and Pop",
            "Computes ST(i) - ST(0), stores into ST(i), pops the stack.",
            "Reads ST(0)/ST(i); writes ST(i); increments TOP.",
            "End an FP subtract expression.",
            "FSUBP");
        Add(map, "Floating-Point Reverse Subtract and Pop",
            "Computes ST(0) - ST(i), stores into ST(i), pops the stack.",
            "Reads ST(0)/ST(i); writes ST(i); increments TOP.",
            "End a reversed FP subtract expression.",
            "FSUBRP");
        Add(map, "Floating-Point Multiply",
            "Multiplies the destination FPU register by the source.",
            "Reads two FPU values; writes destination.",
            "Floating-point multiplication.",
            "FMUL", "FIMUL");
        Add(map, "Floating-Point Multiply and Pop",
            "Multiplies ST(i) by ST(0), stores into ST(i), pops the stack.",
            "Reads ST(0)/ST(i); writes ST(i); increments TOP.",
            "End an FP multiply expression.",
            "FMULP");
        Add(map, "Floating-Point Divide",
            "Divides the destination FPU register by the source.",
            "Reads two FPU values; writes destination. May raise #Z on divide-by-zero.",
            "Floating-point division.",
            "FDIV", "FIDIV");
        Add(map, "Floating-Point Reverse Divide",
            "Computes source / destination and stores it in destination.",
            "Reads two FPU values; writes destination. May raise #Z.",
            "Compute (memory / ST(0)) without an extra FXCH.",
            "FDIVR", "FIDIVR");
        Add(map, "Floating-Point Divide and Pop",
            "Divides ST(i) by ST(0), stores into ST(i), pops the stack.",
            "Reads ST(0)/ST(i); writes ST(i); increments TOP.",
            "End an FP divide expression.",
            "FDIVP");
        Add(map, "Floating-Point Reverse Divide and Pop",
            "Computes ST(0) / ST(i), stores into ST(i), pops the stack.",
            "Reads ST(0)/ST(i); writes ST(i); increments TOP.",
            "End a reversed FP divide expression.",
            "FDIVRP");
        Add(map, "Floating-Point Change Sign",
            "Negates ST(0) by flipping its sign bit.",
            "Writes ST(0) sign and C1.",
            "Implement unary minus on a floating-point value.",
            "FCHS");
        Add(map, "Floating-Point Absolute Value",
            "Clears the sign bit of ST(0).",
            "Writes ST(0) sign and C1.",
            "Implement fabs() in one instruction.",
            "FABS");
        Add(map, "Floating-Point Square Root",
            "Replaces ST(0) with sqrt(ST(0)).",
            "Reads/writes ST(0); may raise invalid-operation for negative input.",
            "Hardware sqrt - faster and more accurate than a software approximation.",
            "FSQRT");
        Add(map, "Floating-Point Round to Integer",
            "Rounds ST(0) to an integer value (still in FP format) according to the current rounding mode.",
            "Reads/writes ST(0); reads RC field of control word.",
            "Implement floor/ceil/trunc/round depending on the rounding mode setup.",
            "FRNDINT");
        Add(map, "Floating-Point Scale",
            "Multiplies ST(0) by 2^trunc(ST(1)) (binary scale).",
            "Reads ST(0)/ST(1); writes ST(0).",
            "Adjust the exponent of an FP value by an integer power of two - used inside transcendental implementations.",
            "FSCALE");
        Add(map, "Floating-Point Extract Exponent and Significand",
            "Splits ST(0) into its (unbiased) exponent in ST(1) and significand in ST(0).",
            "Reads ST(0); writes ST(0)/ST(1); decrements TOP.",
            "Implementation block for log/exp/frexp library routines.",
            "FXTRACT");
        Add(map, "Floating-Point Partial Remainder (8087)",
            "Computes the IEEE-style partial remainder ST(0) mod ST(1) - may need to be repeated until C2=0.",
            "Reads ST(0)/ST(1); writes ST(0), C0/C1/C2/C3.",
            "Argument reduction (mod 2*pi) inside trig - used for the older 8087-style remainder.",
            "FPREM");
        Add(map, "Floating-Point Partial Remainder (IEEE)",
            "IEEE-754 conforming partial remainder of ST(0) and ST(1).",
            "Reads ST(0)/ST(1); writes ST(0), C0/C1/C2/C3.",
            "IEEE-conforming argument reduction - preferred over FPREM on the 387.",
            "FPREM1");

        // ---------- 80387 FPU - compare ----------
        Add(map, "Floating-Point Compare",
            "Compares ST(0) to the source FPU value, setting C0/C1/C2/C3 in the FPU status word.",
            "Reads ST(0) and source; writes status-word condition codes.",
            "FP comparison - usually followed by FNSTSW AX + SAHF + Jcc to branch on the result.",
            "FCOM", "FICOM");
        Add(map, "Floating-Point Compare and Pop",
            "Compares ST(0) to source, then pops the stack.",
            "Reads ST(0)/source; writes status-word condition codes; increments TOP.",
            "Compare and discard the top operand in one go.",
            "FCOMP", "FICOMP");
        Add(map, "Floating-Point Compare and Pop Twice",
            "Compares ST(0) to ST(1), then pops the stack twice.",
            "Reads ST(0)/ST(1); writes condition codes; increments TOP twice.",
            "Compare two values that are both already on the FPU stack and discard them.",
            "FCOMPP");
        Add(map, "Floating-Point Test Against Zero",
            "Compares ST(0) to +0.0, setting condition codes.",
            "Reads ST(0); writes condition codes.",
            "Sign / zero test on an FP value before a branch.",
            "FTST");
        Add(map, "Floating-Point Examine",
            "Classifies ST(0) (zero, normal, denormal, NaN, infinity, empty) into C0/C2/C3.",
            "Reads ST(0); writes C0/C1/C2/C3.",
            "Implement isnan / isinf / fpclassify without taking exceptions.",
            "FXAM");

        // ---------- 80387 FPU - transcendental ----------
        Add(map, "2^x - 1",
            "Computes 2^ST(0) - 1, with ST(0) in [-1.0, +1.0]; result replaces ST(0).",
            "Reads/writes ST(0); writes C1.",
            "Building block for exp() and pow() implementations.",
            "F2XM1");
        Add(map, "ST(1) * log2(ST(0))",
            "Replaces ST(1) with ST(1) * log2(ST(0)) and pops the stack.",
            "Reads ST(0)/ST(1); writes ST(1); pops; writes C1.",
            "Building block for log/log2/log10 - paired with FLDLN2 / FLDLG2 to scale to other bases.",
            "FYL2X");
        Add(map, "ST(1) * log2(ST(0)+1)",
            "Replaces ST(1) with ST(1) * log2(ST(0)+1), with ST(0) close to 0; pops the stack.",
            "Reads ST(0)/ST(1); writes ST(1); pops; writes C1.",
            "More accurate near 1 than FYL2X for log1p().",
            "FYL2XP1");
        Add(map, "Floating-Point Sine",
            "Replaces ST(0) with sin(ST(0)) (radians).",
            "Reads/writes ST(0); writes C2 if argument out of range.",
            "Hardware sine - faster and more accurate than a CORDIC software implementation.",
            "FSIN");
        Add(map, "Floating-Point Cosine",
            "Replaces ST(0) with cos(ST(0)) (radians).",
            "Reads/writes ST(0); writes C2 if argument out of range.",
            "Hardware cosine.",
            "FCOS");
        Add(map, "Sine and Cosine",
            "Computes sin(ST(0)) into ST(0) and pushes cos of the original onto the FPU stack.",
            "Reads/writes ST(0); pushes ST; writes C2.",
            "Compute both sin and cos in one shot - typical inside 2D/3D rotation matrices.",
            "FSINCOS");
        Add(map, "Partial Tangent",
            "Computes tan(ST(0)) into ST(0) and pushes 1.0 (so a divide isn't immediately needed).",
            "Reads/writes ST(0); pushes ST; writes C2.",
            "Hardware tangent - the trailing 1.0 is a quirk of the 8087 ISA.",
            "FPTAN");
        Add(map, "Partial Arctangent",
            "Replaces ST(1) with atan2(ST(1), ST(0)) and pops the stack.",
            "Reads ST(0)/ST(1); writes ST(1); pops.",
            "Two-argument arctangent for converting (x,y) to angle - the standard atan2() primitive.",
            "FPATAN");

        // ---------- 80387 FPU - control ----------
        Add(map, "Initialize FPU",
            "Resets the x87 FPU to its power-on state (control word 037Fh, all registers empty).",
            "Writes the entire FPU state.",
            "Reset the FPU at program startup or after an FP error - the 'wait' form (FINIT) checks for pending exceptions first.",
            "FINIT", "FNINIT");
        Add(map, "Clear FPU Exceptions",
            "Clears the FPU exception flags in the status word.",
            "Writes status-word exception bits.",
            "Acknowledge handled FP exceptions before continuing - paired with FNSTSW for software exception handling.",
            "FCLEX", "FNCLEX");
        Add(map, "Store Control Word",
            "Stores the FPU control word to a 16-bit memory operand.",
            "Reads the FPU control word; writes memory.",
            "Save the rounding/precision/exception-mask configuration so a callee can change it and restore it.",
            "FSTCW", "FNSTCW");
        Add(map, "Load Control Word",
            "Loads a 16-bit value from memory into the FPU control word.",
            "Reads memory; writes the FPU control word.",
            "Switch FPU rounding mode (e.g. truncate vs round-to-nearest) for a (int) cast or for a numeric library.",
            "FLDCW");
        Add(map, "Store Status Word",
            "Stores the FPU status word to a 16-bit memory or to AX.",
            "Reads the FPU status word; writes memory or AX.",
            "Inspect FP condition codes after FCOM - typical pattern: FNSTSW AX, SAHF, Jcc.",
            "FSTSW", "FNSTSW");
        Add(map, "Store Environment",
            "Stores the FPU environment (control/status/tag words and pointers) to memory.",
            "Reads FPU environment; writes a 14- or 28-byte block.",
            "Save FPU state on a context switch or signal entry without saving the data registers.",
            "FSTENV", "FNSTENV");
        Add(map, "Load Environment",
            "Restores the FPU environment from memory (control/status/tag words and pointers).",
            "Reads memory; writes FPU environment.",
            "Restore the FPU control state previously saved with FSTENV.",
            "FLDENV");
        Add(map, "Save FPU State",
            "Saves the full FPU state (environment + 8 data registers) to a 94/108-byte memory block; reinitializes the FPU.",
            "Reads FPU state; writes memory; resets FPU.",
            "Save FPU state on a heavy context switch.",
            "FSAVE", "FNSAVE");
        Add(map, "Restore FPU State",
            "Restores the full FPU state (environment + 8 data registers) from memory.",
            "Reads memory; writes FPU state.",
            "Restore FPU state previously saved with FSAVE on a context switch.",
            "FRSTOR");
        Add(map, "FPU No Operation",
            "FPU-level no-op.",
            "No reads/writes.",
            "Padding inside FPU code.",
            "FNOP");

        return map;
    }

    private static void Add(Dictionary<string, InstructionInfo> map,
        string name, string summary, string uses, string purpose,
        params string[] mnemonicAliases) {
        string canonical = mnemonicAliases[0];
        InstructionInfo info = new(canonical, name, summary, uses, purpose);
        foreach (string alias in mnemonicAliases) {
            map[alias] = info;
        }
    }

    private static void AddJcc(Dictionary<string, InstructionInfo> map,
        string name, string flagCondition, params string[] mnemonicAliases) {
        Add(map,
            name,
            $"Jumps to the target when the flag condition holds: {flagCondition}.",
            $"Reads {flagCondition}; writes (E)IP if the branch is taken.",
            "Encodes a high-level if / while / for branch - emitted right after CMP, TEST, SUB or any flag-setting instruction.",
            mnemonicAliases);
    }
}
