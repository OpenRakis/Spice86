# HasActiveBreakpoints Performance Benchmark Results

## Methodology

We're comparing two implementations of `HasActiveBreakpoints`:

### Current Implementation (Dynamic Iteration)
- Iterates through `_registeredBreakPoints` HashSet
- Checks `IsEnabled` property on each breakpoint
- Returns true on first enabled breakpoint (early return)
- **Complexity**: O(n) where n is the number of breakpoints

### Proposed Implementation (Event-Based Counter)
- Maintains an `_activeBreakpoints` integer counter
- Subscribes to `IsEnabledChanged` event on each breakpoint
- Increments/decrements counter when breakpoints are enabled/disabled
- Returns `_activeBreakpoints > 0`
- **Complexity**: O(1) constant time

## Test Scenarios

The benchmark tests six scenarios:
1. **Empty holder**: No breakpoints registered
2. **One disabled breakpoint**: Single breakpoint, disabled
3. **Five disabled breakpoints**: Multiple disabled breakpoints
4. **Ten disabled breakpoints**: More disabled breakpoints
5. **One enabled breakpoint**: Single enabled breakpoint (best case for iteration)
6. **Mixed breakpoints (enabled last)**: 9 disabled + 1 enabled at end (worst case for iteration)

## Expected Results

- **Event-based approach should be faster** in all scenarios because:
  - O(1) integer comparison vs O(n) iteration
  - No memory allocations or method calls
  - Direct field access

- **Dynamic iteration penalty** should increase with number of breakpoints:
  - Worst case: All disabled breakpoints require full iteration
  - Best case: First breakpoint is enabled (early return)

## Running the Benchmark

```bash
cd src/Spice86.MicroBenchmarkTemplate/bin/Release/net8.0
./Spice86.MicroBenchmarkTemplate
```

## Results

### Current Implementation (Dynamic Iteration)

Run date: (To be filled after benchmark runs)

| Scenario | Mean Time | Allocated Memory |
|----------|-----------|------------------|
| Empty | - | - |
| 1 Disabled | - | - |
| 5 Disabled | - | - |
| 10 Disabled | - | - |
| 1 Enabled | - | - |
| Mixed (9 disabled + 1 enabled) | - | - |

### Event-Based Implementation (After Reintroduction)

Run date: (To be filled after benchmark runs)

| Scenario | Mean Time | Allocated Memory |
|----------|-----------|------------------|
| Empty | - | - |
| 1 Disabled | - | - |
| 5 Disabled | - | - |
| 10 Disabled | - | - |
| 1 Enabled | - | - |
| Mixed (9 disabled + 1 enabled) | - | - |

## Conclusion

(To be filled after benchmarks and comparison)
