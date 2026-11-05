# Review of feature/codegen-ir-pipeline against master

## Current status

**All four findings are resolved.** Handler input analysis conservatively retains partial-definition and phi reads, global offsets are evaluated without truncation, and ranges crossing the A20 boundary conservatively return `MayAlias`. The guarded jump-table rewrite was removed because its signature checks and duplicated dispatch did not improve the generated code. Its ASM regression remains in the catalogue.

Final validation passed: the solution built with no warnings or errors, the core suite excluding `SingleStepTest` passed **8246 tests**, and the second serial golden regeneration passed **705 tests** without changing any golden. All three golden writers are disabled.

The interrupt regression detects missing AH, BH and CH reads in the summary derived from a real ASM trace. Its execution oracle also passed before the fix; no incorrect final execution result was reproduced for that finding.

## Earlier follow-up verification

Before removal of the guarded jump-table rewrite, focused verification rebuilt the checkout and passed **437 tests, 0 failed, 0 skipped**:

```text
dotnet test tests/Spice86.Tests -- --filter-method '*CatalogueCaseRunsAsGeneratedCode*' --filter-method '*HardwareInterruptSummaryKeepsPartialRegisterInputs*' --filter-method '*SpeculativeA20WrapSelfModificationGuardFires*'
```

This includes the generated-code catalogue with all four new ASM regressions and the two focused handler-summary and speculative-guard assertions. The full core suite was not repeated during this follow-up. The test run changed no source or golden files.

The original review and its evidence are retained below as history.

## Original review summary

**Original verdict: changes required.** Four introduced correctness issues were confirmed with targeted reproductions: two high-priority issues (P1), and two medium-priority issues (P2). All four were functional findings requiring fixes before merge. Severity: four major findings; no separate critical, minor, or suggestion findings.

- Base: local `master`, `e1193c294367a33aa1d07c214f783c3891cbea33`.
- Head: `9ce9ff6a9f148e2b91cdd840830d020d2cc5d66a`.
- Scope: `master...HEAD`; master is also the merge base.
- Size: 961 files, 154,068 inserted lines, 42,105 deleted lines.
- Reviewed source changes and relevant test code. Regenerated listings, CFG data, generated-source goldens, and binaries were excluded from bulk inspection.
- This was a risk-focused review of a very large change, not an exhaustive inspection of every changed line. No remote PR metadata or existing review comments were consulted.

## Original findings (now resolved)

### FN1 - P1 / Major: preserve handler inputs through partial register definitions

**Location:** `src/Spice86.Core/Emulator/ReverseEngineer/CfgCodeGeneration/HardwareInterruptEffectBuilder.cs:485-490`

**Category:** Functional. **Blocking:** Yes. **Pre-existing:** No.

`EntryReads` counts a register read only when its SSA version exactly equals the entry version. SSA tracks EAX as one value, so writing AL creates a new version even though AH still contains the interrupted program's value.

For this hardware handler:

```asm
mov al,20h
mov [0400h],ah
iret
```

the computed hardware effect omits AH from `Reads`. The handler does read the incoming AH. The same direct-version check also misses entry inputs carried through phi nodes.

`CheckExternalEvents` uses this summary as its read effect. A caller's register assignment can therefore appear dead when its only observer is the handler, allowing optimization to change what the handler stores.

**Evidence:** `ReviewHardwareHandlerReadsAhAfterWritingAl` lifted a program with this handler registered as the INT 8 execution-context entry. The assertion that `HardwareInterruptEffect.Reads` contains `CpuValues.Ah` failed: it was false.

**Recommendation:** follow the input-byte dependencies through partial definitions and phi operands. Until that proof is available, retain the established conservative read effect for cases the analysis cannot resolve. Apply the same correction to the terminator and argument-read variants.

### FN2 - P1 / Major: do not truncate 32-bit offsets when recognizing globals

**Location:** `src/Spice86.Core/Emulator/ReverseEngineer/CfgCodeGeneration/Ir/IrGlobalAccesses.cs:162-165`

**Category:** Functional. **Blocking:** Yes. **Pre-existing:** No.

`TryConstantOffset` casts every resolved constant to `ushort` without checking whether the original offset fits. A real address-size-prefixed instruction, `mov ax,[dword 10000h]` (bytes `67 A1 00 00 01 00`), consequently generates:

```csharp
AX = DsGlobals.Word0000;
```

The generated property accesses DS:0000. The original instruction accesses DS:10000 and must raise a segment-limit fault under `RealModeMmu386`. The rewrite silently changes the address and removes that fault.

**Evidence:** `ReviewGlobalAccessKeeps32BitOffset` parsed those instruction bytes and ran the generator. Its assertion against `DsGlobals.Word0000` failed. The captured source contains the original DS:[0x00010000] instruction comment followed by the access to `Word0000`.

**Recommendation:** evaluate the offset with its conversion semantics intact. Refuse the global rewrite when the effective offset exceeds `ushort.MaxValue`, keeping the existing raw segmented-indexer path and its MMU checks.

### FN3 - P2 / Major: make jump-table signature names unique across the program

**Location:** `src/Spice86.Core/Emulator/ReverseEngineer/CfgCodeGeneration/Ir/JumpTables.cs:154-159`

**Category:** Functional. **Blocking:** Yes. **Pre-existing:** No.

The new signature-field name uses only the instruction offset:

```csharp
string fieldName = $"Signature_{irSwitch.Origin.Address.Offset:X4}_jumptable";
```

Two qualifying dispatches at E000:0005 and F000:0005 both declare `Signature_0005_jumptable`. All signature fields are emitted into the same `CfgGeneratedOverrides` class, including when methods are split across files. The resulting generated program does not compile.

**Evidence:** `ReviewJumpTablesAtSameOffsetInDifferentSegmentsCompile` combined two bounded jump-table partitions at those segments and compiled the generator output. It failed with:

```text
error CS0102: The type 'CfgGeneratedOverrides' already contains a definition for 'Signature_0005_jumptable'
```

**Recommendation:** use the existing program-wide name-disambiguation approach for these fields, including distinct instructions at the same segmented address. Update each guard reference to the assigned name.

### FN4 - P2 / Major: split address ranges that cross the A20 wrap boundary

**Location:** `src/Spice86.Core/Emulator/ReverseEngineer/CfgCodeGeneration/Ir/Memory/IrMemoryAliasAnalysis.cs:372-376`

**Category:** Functional. **Blocking:** Yes. **Pre-existing:** No.

`Wrapped` masks the two endpoints independently. A range crossing 1 MB becomes an inverted interval, which `RangesIntersect` treats as disjoint from its wrapped bytes.

For example, a 16-byte code range starting at `0xFFFF4` ends at `0x100003`. With A20 disabled it includes physical bytes `0x00000` through `0x00003`. The analysis nevertheless reports a one-byte write at `0x00002` as `Disjoint`.

This result is used by `IrSpeculativeGuardMerge.MemoryWriteProvedDisjoint` at lines 145-151 to decide whether a store can modify later code. A speculative run crossing the boundary can therefore have its guards moved before a store that changes a later instruction.

**Evidence:** `ReviewA20WrappedCodeRangeMayAliasLowMemory` checked exactly these two ranges. It failed because `Relation` returned `Disjoint`. This reproduction confirms the alias-analysis error; it does not constitute an end-to-end self-modifying-program execution test.

**Recommendation:** represent a wrapped interval as its two non-wrapping pieces and compare both, or conservatively return `MayAlias` when an interval crosses the boundary.

## Validation

`fastbox` was unavailable. Verification ran locally.

The unmodified branch passed:

```text
dotnet test tests/Spice86.Tests -- --filter-not-class '*SingleStepTest*'
Passed: 8249, Failed: 0, Skipped: 0
Exit code: 0
```

The four temporary reproductions ran together:

```text
dotnet test tests/Spice86.Tests -- --filter-method '*ReviewHardwareHandlerReadsAhAfterWritingAl*' --filter-method '*ReviewJumpTablesAtSameOffsetInDifferentSegmentsCompile*' --filter-method '*ReviewA20WrappedCodeRangeMayAliasLowMemory*' --filter-method '*ReviewGlobalAccessKeeps32BitOffset*'
Failed: 4, Passed: 0
Exit code: 1
```

Each failed for the specific reason reported above. Failure output is saved in `tmp/review-repro-failures.log`.

The temporary tests were removed after verification. No production code or goldens were changed. Before saving this report, `git diff` was empty and the only untracked path was the pre-existing `tasks/`.

## Risk and testing guidance

**Impact scope: global.** The branch changes generation-wide optimization assumptions and always-used interrupt dispatch and memory tracking. The existing suite provides substantial coverage, but it does not catch these four cases.

After fixes, add behavioral regressions for the handler input and 32-bit memory fault, a generated-program compile check for duplicate offsets across segments, and an alias/guard regression for the A20 boundary. Run the full suite excluding `SingleStepTest` on the final state. A real DOS program generation-and-execution smoke check would also exercise the combined paths beyond the small fixtures; none was run during this review.

- **Automated coverage sufficient:** Partial.
- **Manual testing required for the reported defects:** No; all four can be covered automatically.
- **Non-regression scope:** Full, excluding `SingleStepTest` unless instruction decoding, execution, or flag logic changes during the fixes.

## Other observations

No separate non-blocking or pre-existing findings are included.

The branch has useful safeguards: generated-program compilation and execution tests, explicit effect models, conservative unknown contracts, runtime checks for typed interrupt vectors, and a guarded fallback for jump-table rewrites. The findings concern gaps in those proofs and naming rules.
