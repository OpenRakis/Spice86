namespace Spice86.DebuggerKnowledgeBase.Instructions;

using System;
using System.Collections.Generic;

/// <summary>
/// Static knowledge base of high-level <see cref="InstructionInfo"/> for common
/// x86 / 386 instructions, keyed by canonical mnemonic name (case-insensitive).
/// </summary>
/// <remarks>
/// Coverage is intentionally focused on the instructions that appear most often in
/// real-mode DOS programs (data movement, arithmetic, logic, control flow, string
/// ops, stack, flags, segment loads, system / I/O). The full 386 ISA can be added
/// incrementally; lookups for unknown mnemonics return <c>false</c> from
/// <see cref="TryGet"/> so callers can degrade gracefully.
///
/// Mnemonic names follow the Intel/MASM convention without a leading dot. Multiple
/// aliases (e.g. JE/JZ, JA/JNBE) are registered for the same instruction so callers
/// can pass whichever form their disassembler emits.
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
