# Breakpoint Performance Optimization

## Overview

This document describes the performance optimization implemented to restore the performance that was lost in PR #1547.

## Problem

PR #1547 fixed bugs related to step_into functionality and "element not found" exceptions by reverting to an older version of `BreakPointHolder` and `EmulatorBreakpointsManager`. However, this change removed the `HasActiveBreakpoints` optimization, causing a performance regression.

### Before PR #1547
- The `BreakPointHolder` tracked active (enabled) breakpoints with a counter
- The `EmulationLoop` only called `CheckExecutionBreakPoints()` when `HasActiveBreakpoints` was true
- This avoided expensive dictionary/list lookups when no breakpoints were active (the common case)

### After PR #1547
- The tracking was replaced with a simple `IsEmpty` check
- `CheckExecutionBreakPoints()` was called on every loop iteration if any breakpoints existed
- Even disabled breakpoints caused the expensive check to run
- Performance regression occurred because the hot path always performed unnecessary work

## Solution

Reintroduce the `HasActiveBreakpoints` property while maintaining the bug fixes from PR #1547.

### Implementation Details

#### 1. BreakPointHolder.cs

Added the following fields and properties:
```csharp
private readonly HashSet<BreakPoint> _registeredBreakPoints = [];
private int _activeBreakpoints;

public bool HasActiveBreakpoints => _activeBreakpoints > 0;
```

Added registration/unregistration logic:
- `RegisterBreakPoint()`: Subscribes to `IsEnabledChanged` event and increments counter if enabled
- `UnregisterBreakPoint()`: Unsubscribes from event and decrements counter if enabled
- `OnBreakPointIsEnabledChanged()`: Increments/decrements counter when breakpoint is toggled

Prevented duplicate registrations:
- Check if breakpoint already exists before adding to collections
- Use `HashSet` to track registered breakpoints to avoid duplicate event subscriptions

#### 2. EmulatorBreakpointsManager.cs

Added property that checks both execution and cycle breakpoints:
```csharp
public bool HasActiveBreakpoints =>
    _executionBreakPoints.HasActiveBreakpoints || _cycleBreakPoints.HasActiveBreakpoints;
```

#### 3. EmulationLoop.cs

Modified hot path to skip expensive check when no breakpoints are active:
```csharp
while (_cpuState.IsRunning) {
    if (_emulatorBreakpointsManager.HasActiveBreakpoints) {
        _emulatorBreakpointsManager.CheckExecutionBreakPoints();
    }
    // ... rest of loop
}
```

## Testing

### Unit Tests

Added 4 new tests in `BreakPointHolderTests.cs`:
1. `HasActiveBreakpointsTracksEnabledState`: Verifies counter tracks enable/disable correctly
2. `RemovalOnTriggerUpdatesActiveBreakpoints`: Verifies removal on trigger updates counter
3. `DoubleToggleUnconditionalBreakPointDoesNotLeakActiveCount`: Prevents counter leaks
4. `AddressBreakPointIsOnlyRegisteredOnce`: Prevents duplicate registrations

Added performance demonstration test in `BreakpointPerformanceBenchmark.cs`:
- Shows that HasActiveBreakpoints avoids unnecessary checks with 1M iterations
- Demonstrates difference between active and disabled breakpoints

### Integration Tests

All existing tests pass (943 tests):
- CPU tests
- GDB conditional breakpoint tests (5 tests)
- Emulation tests
- Memory tests

### BenchmarkDotNet Benchmark

Created `BreakpointCheckBenchmark.cs` for detailed performance analysis:
- Compares with/without optimization for empty holder
- Compares with/without optimization for disabled breakpoint
- Compares with/without optimization for enabled breakpoint
- Can be run in Release mode for accurate measurements

## Performance Impact

### Without Optimization (PR #1547 state)
- `CheckExecutionBreakPoints()` called every loop iteration if any breakpoints exist
- Even disabled breakpoints cause the check to run
- Dictionary lookups and list iterations on every cycle

### With Optimization (This PR)
- `HasActiveBreakpoints` is a simple integer comparison (`_activeBreakpoints > 0`)
- No dictionary lookups when no breakpoints are active
- No list iterations when no breakpoints are active
- Disabled breakpoints don't trigger the expensive check

### Typical Scenarios

1. **No breakpoints** (common during normal emulation):
   - Before: Called `CheckExecutionBreakPoints()` → checked `IsEmpty` → returned false
   - After: Checked `HasActiveBreakpoints` (integer comparison) → returned false
   - **Benefit**: Avoided method call and collection checks

2. **Breakpoints exist but are disabled** (common during debugging sessions):
   - Before: Called `CheckExecutionBreakPoints()` → checked dictionaries and lists
   - After: Checked `HasActiveBreakpoints` (integer comparison) → returned false
   - **Benefit**: Avoided all dictionary and list operations

3. **Active breakpoints** (less common):
   - Before: Called `CheckExecutionBreakPoints()` → performed checks
   - After: Same behavior
   - **No change**: Both versions perform the same work

## Code Quality

### Bug Prevention

The implementation includes safeguards to prevent the bugs that PR #1547 fixed:

1. **Duplicate registration prevention**:
   - Check if breakpoint already exists before adding
   - Use `HashSet<BreakPoint>` to track registered breakpoints
   - Return early if already registered

2. **Element not found prevention**:
   - Check if breakpoint exists before removing
   - Only unregister if remove was successful
   - Use `Contains()` checks before operations

3. **Counter leak prevention**:
   - Only increment/decrement when HashSet operation succeeds
   - Track enabled state when registering
   - Check `_activeBreakpoints > 0` before decrementing

### Code Style

The implementation follows project conventions:
- No `var` keyword (explicit types)
- Proper XML documentation comments
- Java-style brace placement
- File-scoped namespaces

## Conclusion

This optimization restores the performance lost in PR #1547 while maintaining all bug fixes. The implementation is thoroughly tested and follows project code quality standards.

The key insight is that `HasActiveBreakpoints` (tracking enabled breakpoints) is more efficient than `IsEmpty` (tracking any breakpoints) because:
1. It's a simple integer comparison vs. collection count checks
2. It distinguishes between disabled and enabled breakpoints
3. It avoids expensive operations in the EmulationLoop hot path
