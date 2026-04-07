# Code Review: AST for CfgCpu Instruction Execution

**Commit**: `0eed10ff` – *feat: AST for CfgCpu instruction execution*  
**Reviewer**: GitHub Copilot  
**Date**: 2026-04-07  
**Scope**: Full commit excluding cross-instruction factorization and mixin removal (planned future work).

---

## 1. Summary

This commit replaces the imperative `Execute(InstructionExecutionHelper)` method on every `ICfgNode` with a compiled expression tree pipeline:

1. Each node builds an execution AST via `BuildExecutionAst(AstBuilder)`.
2. `CfgNodeExecutionCompiler` compiles the AST to a `CfgNodeExecutionAction<T>` delegate.
3. An interpreted delegate is assigned immediately; a background thread pool swaps in a `FastExpressionCompiler`-optimized delegate later.
4. The hot path calls `toExecute.CompiledExecution(_instructionExecutionHelper)` instead of the old virtual `Execute()`.

The change touches ~170 files (+4637/-1099 lines), but the bulk is mechanical: each instruction's `Execute()` body became a `BuildExecutionAst()` body that returns AST nodes instead of performing side effects directly.

---

## 2. Architectural Observations

### 2.1 Good

- **Clean separation of concerns**: AST generation, expression compilation, and runtime execution are distinct phases with clear boundaries.
- **Two-phase compilation** (interpreted → optimized) is a pragmatic choice that avoids first-execution latency without blocking the CPU loop.
- **AST caching** with `InvalidateExecutionAstCache()` is correctly hooked into `SignatureReducer` for self-modifying code.
- **AstBuilder decomposition** into sub-builders (Register, Stack, ControlFlow, StringOperation, Io, etc.) keeps the builder manageable.
- **`CfgNodeExecutionCompilerMonitor`** provides good observability with windowed metrics.
- **Test coverage** is solid: new tests for AST caching, compiler lifecycle, monitor counters, and comprehensive `AstExpressionBuilder` tests covering arithmetic, comparisons, shifts, variables, if/else, while loops, and type conversions.
- **`DataType` getting `IEquatable<DataType>` and proper `GetHashCode()`** is a welcome correctness improvement for collections/caching.

### 2.2 Questionable / Needs Attention

#### BUG: `DataType.BOOL` equals `DataType.UINT32`

`DataType.BOOL` is defined as `new(BitWidth.DWORD_32, false)`, identical to `UINT32`. Before this commit, `DataType` did not implement `Equals`, so `==` used reference equality and the two instances were correctly distinct. This commit adds `IEquatable<DataType>` based on `(BitWidth, Signed)`, which means:

```csharp
DataType.BOOL.Equals(DataType.UINT32) // true — probably wrong
```

This could cause subtle bugs in any code using `DataType` in dictionaries, sets, or equality checks where BOOL and UINT32 need to be distinguished.

**Recommendation**: Either give BOOL a distinct BitWidth (e.g. `BitWidth.BOOL_1`) or add a `Kind` discriminator field to `DataType`.

Additionally, `==` / `!=` operators are not overridden, so `dataType == DataType.BOOL` still uses reference equality. Consider either:
- Overriding `==`/`!=` for consistency, or
- Making `DataType` a `record class` so equality is unified.

#### Commented-out code in `CfgCpu.cs`

```csharp
toExecute.CompiledExecution(_instructionExecutionHelper);
//toExecute.Execute(_instructionExecutionHelper);
```

The old `Execute()` call is commented out rather than removed. Since `Execute()` has been removed from `ICfgNode`, this dead line should be deleted.

#### `Console.WriteLine` in production code

`CfgNodeExecutionCompiler.RunBackgroundCompiler()` line 80:
```csharp
Console.WriteLine($"[CfgNodeExecutionCompiler] compile error: {ex.Message}");
```

Should use `ILoggerService` instead. The compiler already has a `_monitor` reference, which has a logger. Either pass the logger to the compiler or route errors through the monitor.

#### Unnecessary `Lazy<Task>` wrapper in `CfgNodeExecutionCompiler.Compile()`

```csharp
Lazy<Task<...>> lazyTask = new Lazy<Task<...>>(() => tcs.Task, ...);
Debug.Assert(lazyTask.Value is not null, ...);
lazyTask.Value.ContinueWith(task => { ... });
```

The `Lazy<Task>` is immediately `.Value`-accessed on the next line, so it provides no deferred initialization benefit. This can be simplified to:

```csharp
Task<...> task = tcs.Task;
task.ContinueWith(t => { ... }, TaskContinuationOptions.ExecuteSynchronously);
```

#### Background compiler thread lifecycle

The background compiler threads run `while (true)` with no shutdown mechanism. In tests or scenarios where `CfgNodeExecutionCompiler` is created and abandoned (e.g., in `DosTestFixture`, `SingleStepTestMinimalMachine`), these threads become orphans. Consider:
- Accepting a `CancellationToken` to signal shutdown.
- Completing the `Channel` writer on dispose so the reader loop exits cleanly.

#### `CfgNodeExecutionCompilerMonitor.Dispose()` only disposes the timer

The `Dispose()` method disposes the timer but doesn't signal the compiler to stop. If the monitor outlives the compiler or vice-versa, this could leave dangling state.

#### `sync-over-async` in background compiler

```csharp
reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult();
```

While the comment explains this is a dedicated thread, a cleaner approach for a dedicated thread loop would be to use `reader.ReadAllAsync()` with a cancellation token, or `BlockingCollection` instead of `Channel` if a synchronous consumer is the design.

#### `ICfgNode.CompiledExecution` is a public setter

```csharp
CfgNodeExecutionAction<InstructionExecutionHelper> CompiledExecution { get; set; }
```

This allows any consumer to overwrite the compiled delegate. Consider restricting the setter (e.g., `internal set`) to prevent accidental mutation outside the compiler.

#### `CfgInstruction.InstructionId` duplication

`CfgInstructionNode` captures `InstructionId = instruction.Id` at construction time. But it also stores `Instruction` (the full `CfgInstruction` reference). This means the `CfgInstruction` is captured in the AST and later baked into `Expression.Constant(node.Instruction, ...)` in the compiled delegate. This pins the `CfgInstruction` object in memory for the lifetime of the delegate. If the intent of `InstructionId` is to avoid this capture, the full `Instruction` reference should be removed from the AST node (or at least from the compiled expression).

#### `_maxNodesToDisplay` change from 200 to 20

In `CfgCpuViewModel.cs`, `_maxNodesToDisplay` was reduced from 200 to 20. This looks like a debugging artifact that may have been accidentally committed.

---

## 3. `SegmentedAddressValueNode` does not extend `ValueNode`

`SegmentedAddressValueNode` implements only `IVisitableAstNode`, not `ValueNode`. This means it cannot be used in contexts expecting a `ValueNode` (e.g., as an operand in `BinaryOperationNode`). Currently this is fine because it's used in typed properties (`CallFarNode.TargetAddress`, `JumpFarNode.TargetAddress`) and `InstructionFieldAstBuilder.ToNode()`. But this is a design inconsistency - every other value-producing node extends `ValueNode`. If future AST transformations need to treat it generically as a value, this will break.

**Recommendation**: Document this design choice explicitly (it doesn't have a single DataType since it's a composite) or consider making it extend `ValueNode` with a synthetic data type like `DataType.SEGMENTED_ADDRESS`.

---

## 4. `AstExpressionBuilder` Review

### 4.1 Method resolution improvements

The new `ResolveMethodCall` with overload scoring is a significant improvement over the previous direct parameter-type matching. The `TryConvertArguments` / `CanConvertExpression` logic handles numeric type promotions well.

### 4.2 Property-as-zero-arg-method fallback

```csharp
if (argumentExpressions.Length == 0) {
    PropertyInfo? targetProperty = targetType.GetProperty(node.MethodName, ...);
    if (targetProperty != null) {
        return Expression.Property(target, targetProperty);
    }
}
```

This conflates method calls and property access by name. If a property and a zero-arg method have the same name, the property always wins. This is fragile — consider making this explicit in the AST (e.g., a `PropertyAccessNode` vs `MethodCallNode`).

### 4.3 Sub-int type arithmetic handling

The `IsSubIntType` / `IsArithmeticOperation` guard correctly addresses .NET's restriction that `+`, `-`, `*`, etc. are not defined for `byte`, `sbyte`, `short`, `ushort` in `Expression` APIs. The narrowing conversion back to the expected type via `FromDataType(resultDataType)` is well-placed.

### 4.4 Reflection caching

`AstExpressionBuilder` calls `typeof(State).GetProperty(...)`, `typeof(Memory).GetProperty(...)`, etc. on every visitor invocation. Since expression building happens on the init path (not the hot path), this is acceptable for now. But if the AST is regenerated frequently (e.g., during self-modification), consider caching `PropertyInfo`/`MethodInfo` lookups in static fields.

---

## 5. Instructions/ Folder Review

### 5.1 Patterns

All instructions consistently follow one of these patterns:
- **Simple**: `return builder.WithIpAdvancement(this, assignmentNode)`
- **Control flow**: Return `ControlFlowAstBuilder` nodes (conditional jumps, interrupts, calls)
- **String ops**: Delegate to `builder.StringOperation.GenerateExecutionAst(this, builder)`
- **ALU ops**: Call `builder.AluCall(...)` or `MethodCallValueNode`/`MethodCallNode`

### 5.2 Within-file factorization opportunities

#### PUSHA/POPA register loop
Both `Pusha.mixin` and `Popa.mixin` have 8 nearly identical register push/pop statements. A helper like `PushRegisters(statements, dataType, registerNames...)` / `PopToRegisters(...)` on `StackAstBuilder` would reduce boilerplate.

#### Enter.mixin variable declaration pattern
`Enter.mixin` repeatedly uses:
```csharp
VariableDeclarationNode fooDecl = builder.DeclareVariable(type, "foo", expr);
VariableReferenceNode fooRef = fooDecl.Reference;
```
A `DeclareAndReference` helper returning a tuple would clean this up.

#### Pusha uses string-based variable reference
```csharp
statements.Add(builder.DeclareVariable(addressType, "savedSp", originalSp));
ValueNode savedSp = builder.VariableReference(addressType, "savedSp");
```

This is fragile — the name `"savedSp"` must match exactly between the declaration and reference. The idiomatic pattern (used in Enter.mixin) is `decl.Reference` which is type-safe. Pusha should follow the same pattern.

### 5.3 `var` usage in mixin files

Six mixin files use `var` for tuple deconstruction:
- `MovRegRm.mixin`, `ShxdCl.mixin`, `OpRmReg.mixin`, `ShxdImm8.mixin`, `MovRmReg.mixin`, `OpRegRm.mixin`

Example:
```csharp
var (rNode, rmNode) = builder.ModRmOperands(builder.UType({{Size}}), ModRmContext);
```

This violates the `.editorconfig` rule prohibiting `var`. Should be:
```csharp
(ValueNode rNode, ValueNode rmNode) = builder.ModRmOperands(builder.UType({{Size}}), ModRmContext);
```

### 5.4 No other consistency or correctness issues found

The instruction implementations are clean and consistent. The transition from `Execute()` to `BuildExecutionAst()` is mechanical and correct across all instruction types reviewed.

---

## 6. Minor Issues

| Issue | File(s) | Severity |
|-------|---------|----------|
| `BOOL == UINT32` in `DataType.Equals` | `DataType.cs` | **High** — semantic bug |
| `var` usage in 6 mixin files | `MovRegRm.mixin` et al. | **Medium** — editorconfig violation |
| Commented-out `Execute()` call | `CfgCpu.cs` | **Low** — dead code |
| `Console.WriteLine` instead of logger | `CfgNodeExecutionCompiler.cs` | **Low** — logging hygiene |
| Unnecessary `Lazy<Task>` | `CfgNodeExecutionCompiler.cs` | **Low** — unnecessary complexity |
| Background threads have no shutdown | `CfgNodeExecutionCompiler.cs` | **Low** — resource leak in tests |
| `_maxNodesToDisplay` 200→20 | `CfgCpuViewModel.cs` | **Low** — possible debug artifact |
| String-based variable reference in Pusha | `Pusha.mixin` | **Low** — fragile pattern |
| `CompiledExecution` has public setter | `ICfgNode.cs` | **Low** — encapsulation |

---

## 7. Verdict

The architecture is solid. The AST-based execution model with two-phase compilation is well-designed and the implementation is thorough. The main actionable item is the `DataType.BOOL == DataType.UINT32` equality bug, which should be fixed before this enters production. The `var` violations and commented-out code are straightforward cleanups.
