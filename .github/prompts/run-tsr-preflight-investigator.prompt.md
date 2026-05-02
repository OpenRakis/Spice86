---
description: "Run a full pre-execution TSR investigation package before code changes, with deep DOS interrupts, batch, XMS, and EMS analysis."
name: "Run TSR Preflight Investigator"
agent: "tsr-preflight-investigator"
argument-hint: "Describe the failing scenario and target binaries/workflow."
---
Create a full pre-execution investigation package for the provided TSR issue scenario.

Requirements:
1. Scan BIOS, DOS interrupts, DOS batch engine, memory, XMS, EMS, CFGCPU, debugger, and MCP layers.
2. Produce code-proven hard facts with file and symbol citations.
3. Produce a failure-surface matrix focused on TSR then next-EXEC failures.
4. Produce interrupt and memory-model contracts for DOS interrupts, XMS, EMS, and batch context transitions.
5. Produce a deterministic A/B plus divergence capture protocol.
6. Rank hypotheses and identify the smallest evidence needed to prove or disprove each.
7. End with an execution handoff contract for the fix phase.

Use this output structure:
1. Hard Facts
2. Failure Surfaces
3. Interrupt and Memory-Model Contracts
4. Observability Plan
5. Ranked Hypotheses
6. Handoff Contract

Strict rules:
- No behavior-change suggestions unless causal chain is proven.
- Keep unknowns explicit.
- Prefer CFG and MCP runtime evidence over assumptions.
- Do not skip DOS interrupts, batch, XMS, or EMS even if initial evidence appears weak.
