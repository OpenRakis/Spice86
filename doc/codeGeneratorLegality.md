# Legality catalogue - IR data-flow rewrites

Every condition a rewrite checks before it applies, what goes wrong without it, and what proves
it is load-bearing. "Trapped" means a mutation that drops the condition fails an oracle (or
fails to compile the generated code, or throws). Rows that cannot be proven by an oracle carry
one of:

- `redundant (covered by <id>)` - another condition refuses the same rewrites first;
- `unreachable` - no input in the fixtures reaches the code path;
- `readability` - an output-size or shape gate that does not change behavior;
- `not trappable` - dropping the condition is observably safe;
- `unit test only` - covered by a unit test because no fixture can produce the shape.

A row id is a short name for one condition (a prefix for the rewrite family and a number); the
fixture section comments use the same ids. The `code` column names the IR pass member that
implements the row now. Rows whose old code was deleted and whose condition is now implied by
the IR model say so.

`dataflow_legality_traps` is the fixture added for the conditions no earlier fixture covered;
its section comments name the row ids. Mutations ran serially; each was reverted after its
run.

## IrConstants - substitution (CR)

Substitutes reads of general registers whose value is known on every path by the constant, and
folds all-constant expressions.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| CR-1 | A statement that may fault is not rewritten | `IrConstants.Substitute` (a `MayFault` statement keeps its origin's statements) | constants would be printed inside a faultable statement | unreachable | the fault edge re-runs the interpreter with the real registers, and on the normal edge the substituted constant equals the incoming value |
| CR-2 | A register's constant dies at the first statement that may write any of its bytes | implied by `IrSsa` versioned reads (a read after the write sees a new version with its own lattice entry) | a read after an overwriting statement of the same instruction would print the stale incoming constant | fixture `dataflow_legality_traps` §CR-2 | mutation (kill accumulation removed): oracle `[01A] expected 0xBB actual 0x00` - the `loopne` condition read the pre-instruction CX, skipped the store |
| CR-3 | Loop bodies and conditions of an inside-instruction loop are not rewritten | `IrConstants.Substitute` (`LoopBlocks`) | a loop-carried register read would get the pre-loop constant | redundant (RC-2) | a register written in a loop body gets a Varying phi, so no constant reaches the read; loop-invariant constants are correct to substitute |
| CR-4 | Substitution reaches only `AluOperationNode` / `AluStatementNode` arguments and the port argument of `PortReadCallNode` / `PortWriteCallNode` | `IrConstants.RewriteCallArguments`, `IsSubstitutableCall`, `IsIoPortCall` | other helper calls would get constant arguments and change overload resolution | readability | mutation (all arguments rewritten) produced golden diffs only; the printed constants evaluate identically |
| CR-5 | A folded condition keeps at least one register / flag / memory read | `IrConstants.RewriteCondition`, `FoldCondition` (`ContainsStateRead`) | an all-constant condition would drop the unreachable arm | unreachable | a condition can only fold to a constant when its operands are constant on every path, so the dropped arm is already dead; `IrBranchSimplification` removes constant arms anyway; golden-only |
| CR-6 | A register read on the left of `ASSIGN` is never substituted | `IrConstants.RewriteBinary` | `AX = ...` would print `0x0040 = ...` | compile gate | mutation (left side rewritten too): CS0131 in every filtered fixture (`dataflow_legality_traps`, `dataflow_constant_folding`, `dataflow_partial_registers`, `dataflow_idioms`) |
| CR-7 | A declaration whose variable is unreferenced and whose initializer is call-free is removed; a call-initialized declaration stays | `IrConstants.Substitute` (dead-declaration removal) | an unreferenced local remains (CS0219 under `ReportDiagnostic.Error`), or a call side effect is dropped | fixture oracles | the `xadd`/`mul`/`lxs` temporaries keep register reads in the goldens |
| CR-8 | Segment registers and ESP are never substituted | `IrConstants.Tracked` (only the eight general registers hold a lattice) | `ES = AX` would print `ES = 0x2000` | redundant (RC-5) | untracked values never have a constant; a general register copied into a segment register is still substituted (`ES = 0x2300`) |

## IrConstants - lattice (RC)

Per-byte lattice of general-register values known on every path, over the IR SSA.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| RC-1 | A phi operand byte that is Undetermined contributes nothing to the meet | `IrConstants.Meet` | an undetermined operand would be read as a value byte and poison or fake the join constant | fixture `dataflow_constant_folding` + 6 more | mutation (undetermined operands kept in the meet): 7 oracle failures across the catalogue |
| RC-2 | A phi byte is known only when every determined operand agrees on it | `IrConstants.Meet` | a join constant would take one path's byte for a byte that differs across paths | fixture `dataflow_legality_traps` §RC-phi | mutation (first determined operand's byte wins): oracle `[0x56] expected 0x99 actual 0x11` |
| RC-3 | A partial definition inherits the known bytes of the previous version for the bytes it does not write | `IrConstants.FillDefinition` (`unwritten` mask) | the unwritten bytes would lose their known value | fixture `dataflow_expression_propagation`, `dataflow_partial_registers` | mutation (inherit removed): oracle failures in both fixtures |
| RC-4 | A version is known only when exactly one ASSIGN writes its covered bytes | `IrConstants.WriterOf` | a multi-writer statement would get a constant from one of its writers | fixture `dataflow_legality_traps` §RC-4 | mutation (first writer accepted): oracle `[027] expected 0x33 actual 0x00` - the `xchg al,ah` after `mov ax,0x4433` got AX = 0x0044 instead of 0x3344 |
| RC-5 | A constant is served only while the register version is clean: no unknown-like effect on any path since its definition | `IrConstants.ComputeClean`, `IrConstantFacts.TryGetBefore`; the interrupt-delivery reset was deleted - the event-check effect may-writes the handler-visible registers, so the lattice loses their constant through the new version | a callback (timer, OPL delay) could have written the register; the printed constant would be stale | implied by the event-check effect (SSA-7); fixtures `externalint.pit`, `stipending.pit`, `opl_read_delay.pit.a20.ivt.opl3`, `datatrnf`, `jump2`, `bitwise`, `flashback_video.ivt`, `test386.failport` | mutation (the delivery resets removed from `OutState`, rules 2-4 still deleted) on the regenerated goldens: 0 failures - the may-write re-versions the registers on every path, so no pre-check constant survives. Carry proof (see SSA-7): with the tokens-only check effect the `externalint.pit` / `stipending.pit` oracles fail and `opl_read_delay.pit.a20.ivt.opl3` prints `In8(0x0388)` across a loop-head check |
| RC-6 | Only a writer whose right side evaluates to a constant seeds a known value | `IrConstants.FillDefinition` (`ConstantEvaluation.TryEvaluate`) | a non-constant writer would seed garbage | not trappable | the evaluation is the definition of "known"; a mutation would have to fabricate a value, which is not a dropped condition |

## IrPlainTemporaryPropagation (PT)

Inlines a plain temporary declaration into its same-block reads and drops the declaration.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| PT-1 | The initializer is plain: no memory read, no call | `IrPlainTemporaryPropagation.IsPlain` | a memory read or call would move to a later program point | unreachable | mutation (`IsPlain` -> true) propagated `oldRm` to `CX = UInt16[DS, 0x0F40]` in the §PT-1 `xadd` - golden diff only, oracle identical. A mem-reading temp is always consumed before any write to its address (`xadd`, `lxs`, `xchg`); the blocking-write shape would be covered by PT-3 only for register reads anyway |
| PT-2 | The declaration's variable is not assigned or re-referenced inside another statement's initializer | `IrPlainTemporaryPropagation.HasNestedReference`, `AssignsVariable` | a second use site would substitute a stale value | redundant (Substitute) | `Substitute` already refuses shapes it cannot rewrite |
| PT-3 | No statement between the declaration and a read may write a register the initializer reads | implied by `IrSsa.SameVersions` on the register values | the moved initializer would read the post-write value | fixture `dataflow_partial_registers`, `dataflow_idioms`, `instruction_coverage_386` | mutation (the write-detection check forced false): oracle failures `dataflow_partial_registers` [009], `dataflow_idioms` [002], `instruction_coverage_386` [00E] |

## IrExpressionPropagation (EP)

Merges a register assignment read exactly once into its reader, so one statement covers several
instructions.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| EP-1 | Every register the moved expression reads has the same SSA version before the reader as after the writer | `IrSsa.SameVersions` inside `IrExpressionPropagation.TryMerge` | the moved expression would read a register redefined between write and read | fixture `dataflow_legality_traps` §EP-1 | mutation (version check removed): oracle `[0x02] expected 0xEF actual 0x22` |
| EP-2 | The writer is a statement whose right side is mergeable, writing a general register (not segment, not SP) | `IrExpressionPropagation.TryMerge`, `Mergeable` | SP / segment merges would print illegal or unverifiable code | unreachable | entry blocks' definitions are always consumed by more than one reader |
| EP-3 | Among registers the writer defines only the merged register; every flag it defines is dead | `IrExpressionPropagation.TryMerge` (`LiveUses` checks) | a live flag write would be dropped with the merged statement | unreachable | a statement whose flags are live is never the single `R = e` assign the merge requires; the flag check never gets to refuse |
| EP-4 | The merged register's version has exactly one live non-phi use, in a later statement of the same block | `IrExpressionPropagation.TryMerge` (single-use, `ReaderIndex`) | the expression would be duplicated into several readers or moved across blocks | unreachable | a second live use keeps the version alive through its own web and the merge into one reader leaves the other printing the same register |
| EP-5 | A moved memory read crosses no statement that writes the `Memory` or `Io` token | implied by `IrSsa.SameVersions` on the tokens (`TryMerge`) | the read would see the moved-past store's value (or the wrong port state) | fixture `dataflow_legality_traps` §EP-5; `unit test only` for the port half | mutation (token `SameVersions` check removed): oracle `[0x04] expected 0x04 actual 0x09`; the port-IO half needs a port write changing a memory read (VGA planar), which no partition fixture machine provides - `IrExpressionPropagationTest` covers it |
| EP-6 | The merge crosses no CPU-fault statement, no speculative guard and no event check the moved read would jump over | implied by `IrSsa` versions and the stored `MayFault` effect (`TryMerge`); the `DeliversInterrupts` refusal was deleted - the check's may-write re-versions the read registers, so EP-1's version rule refuses | a moved read would run before an event check or guard that must precede it | redundant (EP-1, EP-4) | a check or fault re-versions the read registers (EP-1 refuses) or ends the block (EP-4's same-block check refuses). Mutation (interrupt refusal removed): no oracle failure; the effect itself carries the condition (carry proof, see SSA-7) |
| EP-7 | The reader reads the merged register exactly once and through nothing overlapping it; the assignment target's address registers do not overlap it | `IrExpressionPropagation.CollectRegisters`, `CollectAddressRegisters`, `Overlaps`, `MergeableTarget` | the substituted expression would write through an address computed from the new value instead of the old | fixture `dataflow_legality_traps` §EP-7b | mutation (address-overlap check removed): oracle `[0x12] expected 0xEF actual 0x76`; the single-read half is a shape gate |
| EP-8 | The merged expression stays under `MaxMergedNodes` nodes and the chain under `MaxChainLength` | `IrExpressionPropagation.CountNodes` | generated expressions grow without bound | readability | output-size limit only |

## IrFlagPropagation (FP)

Replaces a conditional branch's flag read by the relation of the single instruction setting
those flags, and substitutes carry reads before the next carry write.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| FP-1 | Every flag version the rewrite consumes is dead within the fused region: nothing between setter and jump, and nothing after, still reads it | `IrFlagPropagation` (`LiveUses` checks; a setter is rewritten only when its flag versions are dead) | a live flag read (for example `pushf`) would see a flag whose setter was dropped | fixture `dataflow_legality_traps` §FP-1 | mutation (the live-use check forced true): oracle `[0x12] expected 0xEF actual 0x76` |
| FP-2 | The registers the setter's operands read have the same version at the jump as after the setter | `IrSsa.SameVersions` inside `IrFlagPropagation` | the printed relation would read a redefined register | fixtures `alu_dead_flags`, `dataflow_double_precision`, `dataflow_expression_propagation`, `dataflow_flag_definitions` | mutation (the `SameVersions` check forced true): oracle failures in all four |
| FP-3 | A memory operand of the setter is still available at the jump (no blocking write between); otherwise it goes through a `flagOperand_` temporary | `IrFlagPropagation.MemoryOperandAvailable`; the event-check refusal inside it was deleted - the check's may-write re-versions the registers and the memory-write refusal still guards stores | the printed relation would read post-store memory | fixture `dataflow_flag_definitions` | mutation (`MemoryOperandAvailable` -> true): oracle failure; mutation (only the interrupt refusal removed): no oracle failure |
| FP-4 | The flags the jump reads have a single setter version: a phi of two different setters must not resolve to either | `IrFlagPropagation.SetterSite` (a phi-defined version or two sites gives no setter) | the relation of one arm's setter would be printed for a jump reachable from both | fixture `dataflow_legality_traps` §FP-4 | mutation (phi resolved to first operand's setter): oracle `[062] expected 0x01 actual 0x00` - the `test`-arm path got the `cmp`-arm's relation |
| FP-5 | The setter matches a known ALU/compare call shape | `IrFlagPropagation.MatchSetter` | an unrecognized call would be rewritten with a wrong relation | unreachable | the only flag setters a `Definition` version can point at are the emitted Alu/compare calls or plain assigns; both shapes are covered |
| FP-6 | The setter is before the jump it feeds: not an entry, not after the jump, in the fused region | `IrFlagPropagation.LegalSite` and redundant (SSA versions) | a relation from a later instruction would be used for an earlier jump | redundant (SSA versions) | a setter after the read defines a different version than the one the read uses |
| FP-7 | A carry-reader rewrite (`sbb`, `adc`, `rcr` ...) only fires on the recognized call shape with the fused setter's carry | `IrFlagPropagation.MatchCarryReader` | a carry read of a different provenance would get the relation | unreachable | mutation failed nothing over the catalogue |
| FP-8 | Carry substitution stops at the first statement that may write Carry | `FlagReadSubstitution` (`WritesCarry`) | a carry read after a new carry write would keep the old relation | unreachable | a carry writer ends the ALU-assignment block the substitution runs on |

## IrDeadRegisterWrites (DW)

Drops register writes whose SSA versions are dead.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| DW-1 | Only plain values are dropped (no memory read, no call, no variable) | `IrDeadRegisterWrites.IsPlainValue` | dropping the statement would drop a side effect or strand a variable | unreachable | a single-assign statement with a non-plain right side is a stack call (its SP write is always live, so the live-use check refuses first), a port call (boundary), or a variable |
| DW-2 | Blocks containing a `FailAsUntested` region or entered from outside keep their writes | `IrDeadRegisterWrites.ObservingBlocks`; the `DeliversInterrupts` refusal in `Drops` was deleted - the check's register reads keep a handler-visible write live through SSA boundary uses | a write the interpreter or an event check relies on would vanish | redundant (SSA boundary uses) | exit transfers mark every version read, and the check effect reads the handler-visible registers, so the dead check never fires on these writes. Mutation (refusal removed): no oracle failure; the effect itself carries the condition (carry proof, see SSA-7) |
| DW-3 | A use that is only a later partial definition's previous-version dependency does not keep the write alive when that later statement does not read the register and must write every byte the dropped statement may write | `IrDeadRegisterWrites.InheritsAcrossAnOverwrite` | a write still feeding a later read through untouched bytes would be dropped | fixture `dataflow_legality_traps` §DW-3; existing fixtures | mutation (byte-coverage check removed): oracle "every byte of the result region at 0x20000 is the expected one ... [032] expected 0xBB actual 0x34"; mutation (-> true): oracle failures across the catalogue. A former requirement that the later write cover the register's whole low word was removed: no oracle failure, and 41 constant byte writes overwritten before any read left 8 generated-code goldens. §DW-3 also holds a loop whose phi joins both versions into one web, where the write is dropped |
| DW-4 | SP writes are never dropped | `IrDeadRegisterWrites.Drops` | stack movement would disappear | unreachable | every SP assignment has live uses (the next push/pop/call reads it) |
| DW-5 | Every register version the statement defines must be dead before its write is dropped | `IrDeadRegisterWrites.DefinedVersionsAreUsed` | one live version among several would be lost | existing fixtures | mutation (check removed): oracle failures |

## IrAluLowering (AA)

Decides by flag liveness whether an ALU call is removed, expanded into plain C# operators or
kept.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| AA-1 | The cleaned expansion stays within its statement budget | `AluAssignmentRewrite` (the budget check it keeps) | generated code grows | readability | output-size limit only |
| AA-2 | The statement matches the single-assignment or wrapped-call shape with the ALU result reused only through the destination | `AluAssignmentRewrite.TryRewriteStatement` | a use through a second path would print a free variable | compile gate | an expansion that names a variable never declared fails generated-code compilation |
| AA-3 | A wrapped ALU call whose result is unused is removed only when the flags it writes are dead | `IrAluLowering.Apply` (`IrFlagLiveness.LiveAfter`) | a later flag read would see no setter | fixtures `dataflow_flag_definitions`, `instruction_coverage_386` | mutation (live-flag check removed): oracle failures in both |
| AA-4 | A masked shift (`shl/shr/sar` with the count masked) is rewritten only when no flag it would have written is live | `IrAluLowering.Apply` (`IrFlagLiveness.LiveAfter`) | a live flag would lose its setter because the expanded form writes no flags | fixture `dataflow_legality_traps` §AA-4 | mutation (live-flag check removed): oracle `[0x4C] expected 0x01 actual 0x00` |

## IrDeadFlagWrites (DF)

Removes plain flag writes whose flags are dead.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| DF-1 | A flag write is removed only when the flag version it defines has no live use | `IrDeadFlagWrites.IsDeadFlagWrite`, `DefinedStatusFlagsDead` (`IrSsa.LiveUses`) | a live `clc`/`stc`/`cmc`/`sahf` would disappear | fixtures `alu_dead_flags`, `bcdcnv`, `datatrnf`, `dataflow_double_precision`, `dataflow_flag_definitions`, `instruction_coverage_386`, `realloc_test_pad*` | mutation (`live` forced to None) over ALL generated-code classes: 32 failures, including `alu_dead_flags` oracle `[016] expected 0x01 actual 0x00` |
| DF-2 | The written value is plain: no memory read, no call, no variable | `IrDeadFlagWrites.IsDeadFlagWrite` (`IsPlain`) | a flag write carrying a memory/call side effect would be dropped | unreachable | the only non-plain flag write emitted is `popf`/`popfd`; popf can enable interrupts, so its `DeliversInterrupts` effect keeps the statement |

## IrInstructionIdioms (ID)

Folds statement pairs into `xchg` and `lds`/`les` idioms.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| ID-1 | A tuple swap prints `xchg` only when the two assignments really exchange the registers | `IrInstructionIdioms.TryTupleSwap` | a same-register "swap" would print `xchg AX,AX` for a plain copy | readability | a same-register xchg is a no-op either way |
| ID-2 | `lds`/`les` prints only when the segment read's address does not read the register the value part writes | `IrInstructionIdioms.TryLoadFarPointer` | the segment half would read the freshly written register instead of memory | fixture `dataflow_legality_traps` §ID-2 (with EP-7b site) | mutation (order/overlap check removed): oracle `[0x12] expected 0xEF actual 0x00` |

## ConditionCombiner (CC)

Joins nested and sequential `if` statements on the same exit into `&&` / `||` conditions, after
structuring.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| CC-1 | The two conditions guard the same exit (the else-arm of the outer is empty and the inner exits the same way) | `ConditionCombiner.SameExit`, `FirstIfOf`, `SecondIfOf` | `if a { if b { body } }` and `if a && b { body }` would run different bodies | fixture `dataflow_rewrite_traps` §§3-4 | mutations of these checks fail the `dataflow_rewrite_traps` oracle |
| CC-2 | A movable if is not taken out of a boundary a goto targets | `ConditionCombiner.IsJumpTargeted`, `MovableIfInBoundary`, `CombineNestedAt` | the combined condition would evaluate the moved test also on the goto path, where it must not run | unreachable | the only nested-&& shape the §CC-2 section reaches is handled by the sequential combiner, which has its own targeted check; a fixture section exists for the day the nested path becomes reachable |
| CC-3 | A sequential combination does not cross an instruction boundary a goto targets or a method entry | `ConditionCombiner.CombineSequential`, `IsJumpTargeted`, `IsEmptyBoundary` | the second test would be skipped on the entry path | fixture `dataflow_rewrite_traps` §§3-4 | sections 3-4 of `dataflow_rewrite_traps` reach this combination; their oracle covers it |

## Direction steps (DS)

Prints `SI += n` / `DI += n` when the direction flag is provably clear or set at the step; the
direction flag is a one-bit lane of the `IrConstants` lattice and a `DirectionFlag ? -n : n` select
folds to its known arm.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| DS-1 | The arm is chosen only when the lattice gives a known direction at the step: it must agree with the actual DF on every path | `IrConstants.FillDefinition` (`TryEvaluateDirectionWriter`), `IrConstantFacts.TryGetDirection` | an `std` region would print forward steps | fixture `dataflow_interrupt_boundaries.pit` | mutation (a `DirectionFlag` read answers the unknown lattice): oracle `[014] expected 0x1E actual 0x22` |
| DS-2 | A phi of DF is known only when the determined operands agree (same meet as RC-2) | `IrConstants.Meet` | a join would get one path's direction | unreachable | no fixture joins two differing known DF values into one string instruction |
| DS-3 | The step is a pure select: a conditional arm may only be a constant, register, flag, variable or operators on them | `IrVerifier` (`conditional arms` rule) | a memory read or a call in an arm would be evaluated where the instruction has none | unit test `IrVerifierTest` | verifier rule; no generator path builds an impure arm |
| DS-4 | An event check does not dirty DF: a hardware interrupt pushes FLAGS and its `iret` restores them, so the direction before the check still holds after it (the general registers stay dirtied) | `IrConstants` (the direction flag is outside `ComputeClean`'s register mask; only a real DF write varies the version) | DF folded to clear after a check between `std` and the step would print `+1` where the machine steps `-1` | `dataflow_legality_traps` (DS-4 section: `std`, a timer handler that sets DF itself, then `movsb`, the oracle stores `DI` and the copied byte) | mutation (the check writes DF) is conservative and stays correct; mutation (the step's condition forced clear, printing `+n`) fails the oracle on `DI` |

## SSA and boundary rules (IrSsa, IrEffects)

What every pass inherits: which values a statement uses and which versions survive a boundary.

| id | condition | code | if dropped | trapped by | evidence |
|----|-----------|------|------------|------------|----------|
| SSA-1 | A call, return, interrupt or halt transfer re-versions everything it may write | `IrEffects` of the external-code helper calls (`ProcedureCallHelperNode`, `InterruptCallHelperNode`, `CallbackHelperNode`, `CallbackMovedCsIpHelperNode`) / `IrExit` (stored `MayWrite`) | a flag or register version would flow through a call unchanged | redundant (`IrEffects.Unknown` / `ExitUse`) | the stored effects already report the full may-write set for such nodes |
| SSA-2 | A transfer out of the method, or a statement with a CPU-fault edge, uses every value | `IrEffects.ExitUse`, `IrExit.Effects` | a value read by the interpreter on the outside edge would be dropped | unreachable | the versions stay live for the in-method edge anyway, and the oracle cannot see a read that happens on the interpreter side |
| SSA-3 | A transfer out of the method or a may-fault statement is a boundary | `IrEffects.ExitUse`, `MayFault` | versions would flow past an edge that can re-execute elsewhere | fixture `dataflow_interrupt_boundaries.pit` | mutation (condition removed from `boundary`): oracle failure |
| SSA-4 | A return/interrupt exit uses every value except the status flags and DF where the transfer restores them | `IrExit.Effects`, `IrEffectsVisitor.VisitInterruptReturnNode` | flags would be kept live across returns (or, in the other direction, needed values dropped) | `dataflow_legality_traps` (SSA-8: the handler's `add` prints `CX++`, not `CX = Alu16.Add(CX, 1)`), plus every fixture ending in `iret` | the flag half shows in the generated text; the register half is covered by SSA-8 |
| SSA-5 | A mid-block entry block defines fresh entry versions of every value | `IrSsa.CreateEntryVersions` (`IrMethod.Entries`) | a value would flow from before the entry into the joined body | unreachable | entries always start blocks in the catalogue partitions |
| SSA-6 | A partial definition reads the previous version of the register (the bytes it does not write stay live) | `IrSsaVersionInfo.Previous`, `IrSsa` rename | `mov al, 1` would sever `eax`'s version, losing AH/EAX high bytes | fixture `dataflow_partial_registers`, `dataflow_legality_traps` | exercised by the RC-3 and EP-1 mutations, which fail precisely through this path; not mutated on its own |
| SSA-7 | An event check reads and may-writes every general and segment register a handler can touch: all of them except SP/ESP and SS (its `iret` restores the stack pointer; CS is kept in the set although `iret` also restores it, conservatively), and no status flag or DF | `IrEffectsVisitor.VisitCheckExternalEventsNode` (`CheckEvents`) | a value an event handler could write would keep its pre-check version | fixtures `externalint.pit`, `stipending.pit`; `dataflow_legality_traps` (SSA-9) | carry proof (check effect reverted to tokens-only, all per-pass interrupt refusals deleted): oracle `[00] expected=0x01 actual=0x00` in `externalint.pit.spec` and `stipending.pit.spec` (and their `GeneratedCodeMachineTest` cases: the timer handler's DX write is lost to a substituted constant), oracle `[02E] expected 0x34 actual 0x05` in `dataflow_legality_traps`; `opl_read_delay.pit.a20.ivt.opl3` keeps its oracle but drops `DX = 0x0388` and prints `In8(0x0388)` inside a loop whose head checks events |
| SSA-8 | An interrupt-return exit still uses the registers and tokens the handler writes (only the flags are restored) | `IrEffectsVisitor.VisitInterruptReturnNode` (`InterruptReturnUse`) | a register write right before `iret` would be dropped | `dataflow_legality_traps` (SSA-8 section: `add cx, 1` then `iret`, the caller stores CX) | mutation (`InterruptReturnUse` uses nothing): oracle failure, the stored CX keeps the unincremented value |
| SSA-9 | A register written before an event check stays when a handler can read it: the check's effect reads the handler-visible registers | `IrEffectsVisitor.VisitCheckExternalEventsNode` (`CheckEvents`) | the write would be dropped as dead and the handler would read the overwritten value | `dataflow_legality_traps` (SSA-9 section: `mov ebp, 0x1234`, a wait loop whose head checks events, then `mov ebp, 0`; the timer handler stores BP) | failing-first oracle `[02E] expected 0x34 actual 0x05` with the tokens-only check effect |

The flag half of `CheckEvents` rests on an assumption: a hardware interrupt handler does not
inspect the interrupted FLAGS image it finds on its stack, so status flags and DF stay outside
the check effect and keep their pre-check versions (rows DS-4 and SSA-4 rely on this). The
register half assumes only that the handler's `iret` restores SP/ESP and SS; CS is treated as
handler-visible anyway, conservatively.
