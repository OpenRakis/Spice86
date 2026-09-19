# Task 20 — Typed-access comments in generated code

Spice86 side, R3. Final consumer task.

Structured lowering (rewriting the access to a typed accessor call) is
deferred until SSA lands. Pre-SSA there is no value identity across
statements, so a rewritten access would either allocate a wrapper object
per access in the emulator's hot path or call static plumbing that reads
worse than the raw access and is a dead end for later refactoring. This
task therefore proves the type fact and surfaces it as a comment; the
emitted statement is always the faithful raw access. The post-SSA rewrite
will emit R2's instance accessor classes (`troop.Count`) with wrapper
construction hoisted to the base value's definition.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §R3 (Functional
  requirements, Acceptance criteria, Non-goals)
- [README.md](../requirement/README.md): §Shared context (faithful
  generation guarantee)

## Goal

Annotate a memory access with a typed-access comment only when it is
provable; the emitted statement is always the faithful raw access.
Comment emission is always on and never changes observed behavior.

## Deliverables

- A register-based memory operand is commented only when all conditions
  hold:
  - The offset expression is exactly one register plus at most one
    constant term.
  - The type for that register is known at the use site (task 19 scan).
  - The instruction's execution AST does not assign that register
    anywhere.
  - The displacement lands exactly on a field boundary of the known
    layout, at matching width.
- An operand with no register term (`[0x25c]`) is commented on its own:
  the space must resolve through the task 07 query and the offset must
  land exactly on a typed global or field boundary.
- The comment names the struct and field (e.g. `Troop.Count`) above the
  instruction's disassembly line; the statement itself stays the faithful
  raw access.
- No configuration switch: the only variation is per access.
- Every declined comment feeds the NFR5 report with its reason.

## Acceptance

- `[si+4]` with typed SI is commented; the same access inside an
  instruction whose body writes SI declines and gets no comment.
- `mov ax, [0x25c]` is commented with the typed global when a `ds`
  segment default covers the instruction and the global exists; it gets
  no comment otherwise.
- Generated output is the pure-faithful translation plus comment lines;
  it compiles and behaves identically for observed paths
  (GeneratedCodeMachineTest coverage).

## Deferred to SSA

- Rewriting the access to `troop.Count` with hoisted wrapper
  construction. The provability conditions above gate the future rewrite
  unchanged; only the emitted output changes from a comment to an
  accessor call.

## Dependencies

- Tasks 07, 16, 17, 19.
