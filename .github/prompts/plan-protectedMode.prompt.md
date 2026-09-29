## Plan: Complete Protected Mode Conformance

The hang is a finite but impractical `test386` workload, not an unexplained CFG loop: after POST `0xEE`, the ROM iterates its arithmetic table, formats extensive output, and finally reaches POST `0xFF`. The existing `test386_pmode.bin` avoids this by compiling `SKIP_UNVERIFIED_TESTS=1`; it still validates every in-ROM protected-mode assertion, but not the upstream reference-output oracle.

**Steps**
1. Build an unskipped `test386_pmode_full.bin` with `TEST_PMODE=1` and `SKIP_UNVERIFIED_TESTS=0`; measure Release runtime/cycles and use the completed cycle count plus margin as the bounded guard.
2. Make generation of both PMODE variants reproducible without committing changed default NASM configuration flags.
3. Refactor [`Test386PostPortHandler.cs`](tests/Spice86.Tests/Test386PostPortHandler.cs) to capture raw bytes efficiently, register both intended port `0x998` and NASM-truncated `0x98`, and expose POST checkpoints independently.
4. Keep [`Test386ProtectedMode`](tests/Spice86.Tests/MachineTest.cs#L1219) as the fast checkpoint regression, then add a long interpreter conformance test that reaches `0xFF` and byte-compares its output to the vendored `test386-EE-reference.txt`.
5. Close confirmed generated-code parity holes:
   - Implement 32-bit call-gate handling in [`CSharpOverrideHelper.FarCall32`](src/Spice86.Core/Emulator/ReverseEngineer/CSharpOverrideHelper.cs).
   - Make static far-jump lowering in [`CSharpAstEmitter.VisitJumpFarNode`](src/Spice86.Core/Emulator/ReverseEngineer/CfgCodeGeneration/CSharpAstEmitter.cs) call the shared gate-aware `FarJump` helper instead of directly validating/loading CS.
   - Add focused generated-code fixtures for 32-bit call-gate/RETF and static JMP-through-call-gate behavior.
6. Add full generated-code `test386_pmode_full` conformance: discovery, generated C# compilation, override run, complete POST sequence, and exact output comparison. Keep capture state separate for discovery and generated executions.
7. Profile Release execution around [`EmulationLoop.RunLoop`](src/Spice86.Core/Emulator/VM/EmulationLoop.cs) and [`CfgCpu.ExecuteNext`/`ExecuteOneNode`](src/Spice86.Core/Emulator/CPU/CfgCpu/CfgCpu.cs). Optimize only the measured hot path while retaining per-instruction cycles, exceptions, breakpoints, CFG recording, and external-interrupt boundaries.
8. Add a dedicated Ubuntu PR CI job in [`pr.yml`](.github/workflows/pr.yml), capped at ten minutes, which runs only the full interpreter and generated-code conformance tests. Keep the fast tests in the normal cross-platform matrix.
9. Update [`docs/index.html`](docs/index.html) so it no longer claims protected mode and paging are unimplemented; document the tested scope and the two `test386` variants without overstating full 80386 emulation.
10. Finish with focused tests, both full conformance paths, a build, and the normal suite excluding `SingleStepTest`.

**Important findings**

- The ROM's deliberate `error:` self-loop means a truly stuck run can also indicate an earlier failed assertion; it is not itself a CPU-loop defect.
- The post-`0xEE` path is finite: `testOps` advances through a table and ends at `testDone -> postFF`.
- The full output test must capture bytes, not normalized text, because the ROM controls newline order itself.
- `Test386PostPortHandler` currently misses port `0x98`, which the ROM reaches through `OUT imm8, AL` truncation.
- DOSBox Staging is useful as a performance and behavior reference, but its dynamic CPU core should not be ported into Spice86's CFG executor.
- User-selected acceptance: both interpreter and generated-code full output comparisons must complete inside a dedicated ten-minute CI job.
