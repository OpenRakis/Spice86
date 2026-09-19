# Plan: Task 01 — Project file model classes (new assembly)

## TL;DR
Implement `plan/tasks/01-format-model.md`: the in-memory model of the Spice86
project file as plain C# classes with no parsing logic, in a **new assembly
`Spice86.ProjectFile`** (net10.0) under `src/`, consumed later by Spice86 and
every converter. TDD: tests first, then model classes.

## Decisions (confirmed with user)
- New assembly `Spice86.ProjectFile` in `src/`, added to `Spice86.sln`.
  Inherits `src/Directory.Build.props` (net10.0, nullable, XML docs,
  WarningsAsErrors=nullable). No package references — pure model.
- Address reference: **two types** — `NamedAddress` (space name + offset) and
  `RawAddress` (segment:offset numeric form, representable until task 07
  resolves it). Both implement a small `IAddress` interface so model
  properties accept either.
- Closed vocabularies are **C# enums**: `When { Pre, Post }`,
  `ParamDir { In, Out, InOut }`, `RepeatUnit { Bytes, Elements }`,
  `SegmentRegister { Cs, Ds, Es, Ss }`. The YAML serializer (task 04) maps
  to/from strings.
- `AddressSpace` is one class with nullable `EntryDelta` (int, paragraphs,
  signed), `Absolute` (ushort), `Dynamic` (bool). "Exactly one" is a
  validation concern (task 05), not a type-system concern.
- Repo conventions apply: no `var`, one top-level type per file, file-scoped
  namespaces, Java braces, no optional parameters, no generic catch, no
  null-forgiving, XML docs on public types/members, no `#region`.

## Steps

### Phase A — scaffolding
1. Create `src/Spice86.ProjectFile/Spice86.ProjectFile.csproj`
   (`<Project Sdk="Microsoft.NET.Sdk">`, no package references; optionally
   `PackageId` property like `Spice86.Shared.csproj`).
2. Add the project to `src/Spice86.sln` (copy the `Project(...)` +
   `ProjectConfigurationPlatforms` block pattern of `Spice86.Shared`).
3. Add a test folder `tests/Spice86.Tests/ProjectFile/` (existing test
   project, xunit.v3 + FluentAssertions already referenced).

### Phase B — tests first (red)
4. Write unit tests in `tests/Spice86.Tests/ProjectFile/ProjectFileModelTest.cs`
   (split into a few files if it grows):
   - Construct a full `ProjectFile` with every section populated (mirroring
     the §Top-level shape YAML example in
     `plan/requirement/project-file-format.md`) and assert every field reads
     back.
   - Construct an empty `ProjectFile` (all sections null/empty) — partial by
     design, must be representable.
   - `AddressSpace` in each of its three forms (entryDelta incl. negative,
     absolute, dynamic).
   - `Binding` key (exe hash, programEntryPointSegment, initializeDos,
     exeArgs) + map space name → segment number.
   - `StructDefinition` with optional `size`, a field with `repeat`
     (count field reference + unit), and a struct without `size`
     (non-stridable).
   - `FunctionEntry` with signature: params (name, type, location, dir) and
     returns.
   - `Assertion` with `when` pre and post at the same address/location.
   - `OffsetPin`, `SegmentDefault` (whole-space and ranged forms),
     `BranchTarget` (flat `to` list), `Global` (named and unnamed),
     `Label`, `Comment`.
   - `NamedAddress` and `RawAddress` both usable where an address is expected.
   - Enums round-trip through the model (e.g. `ParamDir.InOut`).

### Phase C — model classes (green)
5. Create the model classes in `src/Spice86.ProjectFile/` (one file per
   top-level type, namespace `Spice86.ProjectFile`):
   - `IAddress`, `NamedAddress` (Space, Offset), `RawAddress` (Segment, Offset)
   - Enums: `When`, `ParamDir`, `RepeatUnit`, `SegmentRegister`
   - `ProjectFile` — root: `Project` (string), `Launch`, `Files`
     (List<FileEntry>), `AddressSpaces` (List<AddressSpace>), `Bindings`
     (List<Binding>), `Graph` (string?), `Structs` (List<StructDefinition>),
     `Functions` (List<FunctionEntry>), `Assertions` (List<Assertion>),
     `OffsetPins` (List<OffsetPin>), `SegmentDefaults`
     (List<SegmentDefault>), `Targets` (List<BranchTarget>), `Globals`
     (List<Global>), `Labels` (List<Label>), `Comments` (List<Comment>).
     Every section optional (nullable reference or null list).
   - `Launch` (Exe, ExeArgs, CDrive, ProgramEntryPointSegment,
     ProvidedAsmHandlersSegment, InitializeDos)
   - `FileEntry` (Path, Hash) — no `role` field
   - `AddressSpace` (Name, EntryDelta?, Absolute?, Dynamic)
   - `BindingKey` (Exe, ProgramEntryPointSegment, InitializeDos, ExeArgs),
     `Binding` (Key, Map: Dictionary<string, ushort>)
   - `StructDefinition` (Name, Size?, Comment, Fields), `StructField`
     (Name, Type, Offset, Comment?, Repeat?), `Repeat` (Count, Unit)
   - `FunctionEntry` (Address, Name, Comment?, Signature?), `Signature`
     (Convention, Params, Returns), `SignatureParam` (Name, Type, Location,
     Dir)
   - `Assertion` (Address, Location, When, Type)
   - `OffsetPin` (Address, Type)
   - `SegmentDefault` (Space, Reg, Resolves, Start?, End?)
   - `BranchTarget` (Address, To: List<IAddress>, Comment?)
   - `Global` (Address, Type, Name?), `Label` (Address, Name, Comment?),
     `Comment` (Address, Text)
   - Type strings stay as `string` in the model (grammar is task 02).
6. Run tests until green.

## Relevant files
- `plan/tasks/01-format-model.md` — the task spec (deliverables + acceptance)
- `plan/requirement/project-file-format.md` — §Top-level shape is the field
  reference (YAML example lists every property and its meaning)
- `src/Spice86.Shared/Spice86.Shared.csproj` — csproj pattern to copy
- `src/Spice86.sln` — add project entry
- `tests/Spice86.Tests/` — test project (xunit.v3, FluentAssertions)
- `AGENTS.md` — code style rules

## Verification
1. `dotnet build src/Spice86.sln` — clean, no warnings-as-errors.
2. `dotnet test tests/Spice86.Tests -- --filter-class '*ProjectFileModelTest*'`
   — all new tests pass.
3. `dotnet test tests/Spice86.Tests -- --filter-not-class '*SingleStepTest*'`
   — full suite green.
4. Acceptance from task 01: model compiles and is consumable without
   referencing YAML or any producer format (verify csproj has zero package
   references).

## Scope boundaries
- In: model classes + enums + address types + unit tests.
- Out: YAML serialization (task 04), type grammar (task 02), validation
  (task 05), address resolution/minting (task 07). No parsing logic, no
  producer vocabulary, no `role` field, no `source`/provenance fields.
