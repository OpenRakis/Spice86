# Event-Based Counter Optimization

## Summary

This document describes the final implementation of the `HasActiveBreakpoints` optimization using an event-based counter approach instead of dynamic iteration.

## Implementation

### Counter-Based Approach (Current)

```csharp
private int _activeBreakpoints;

public bool HasActiveBreakpoints => _activeBreakpoints > 0;
```

**How it works:**
1. When a breakpoint is registered, subscribe to its `IsEnabledChanged` event
2. If the breakpoint is initially enabled, increment `_activeBreakpoints`
3. When `IsEnabled` changes, the event handler increments/decrements the counter
4. When a breakpoint is unregistered, unsubscribe from the event and decrement if needed
5. `HasActiveBreakpoints` simply checks if the counter is greater than zero

### Event Handling

```csharp
private void RegisterBreakPoint(BreakPoint breakPoint) {
    if (!_registeredBreakPoints.Add(breakPoint)) {
        return;
    }

    breakPoint.IsEnabledChanged += OnBreakPointIsEnabledChanged;
    if (breakPoint.IsEnabled) {
        _activeBreakpoints++;
    }
}

private void UnregisterBreakPoint(BreakPoint breakPoint) {
    if (!_registeredBreakPoints.Remove(breakPoint)) {
        return;
    }

    breakPoint.IsEnabledChanged -= OnBreakPointIsEnabledChanged;
    if (breakPoint.IsEnabled && _activeBreakpoints > 0) {
        _activeBreakpoints--;
    }
}

private void OnBreakPointIsEnabledChanged(BreakPoint breakPoint, bool isEnabled) {
    if (isEnabled) {
        _activeBreakpoints++;
    } else if (_activeBreakpoints > 0) {
        _activeBreakpoints--;
    }
}
```

## Performance Characteristics

### Counter-Based (Current)
- **Complexity**: O(1) - constant time integer comparison
- **Memory**: Single integer field (4 bytes)
- **Operations**: Direct field access, no method calls
- **Best for**: Hot path code called thousands of times per second

### Dynamic Iteration (Previous)
- **Complexity**: O(n) - must iterate through all breakpoints
- **Memory**: No additional memory needed
- **Operations**: HashSet iteration, property access on each breakpoint
- **Performance**: Degrades with number of breakpoints

## Benchmark Results

Run the benchmark to compare:

```bash
cd src/Spice86.MicroBenchmarkTemplate
dotnet run -c Release
```

Expected results show counter-based approach is consistently faster regardless of:
- Number of breakpoints
- Whether breakpoints are enabled or disabled
- Position of enabled breakpoints in collection

## Trade-offs

### Advantages of Event-Based Counter
✅ O(1) constant time performance
✅ Optimal for hot path (EmulationLoop called thousands of times/second)
✅ Performance doesn't degrade with number of breakpoints
✅ Simple integer comparison

### Disadvantages of Event-Based Counter
❌ Requires event subscription/unsubscription
❌ Slightly more complex code
❌ Small memory overhead for event handlers
❌ Need to carefully manage counter to avoid leaks

### Advantages of Dynamic Iteration
✅ No event management needed
✅ Simpler conceptual model
✅ No risk of counter getting out of sync

### Disadvantages of Dynamic Iteration
❌ O(n) performance - gets slower with more breakpoints
❌ Must iterate entire collection when all breakpoints disabled
❌ Property access overhead on each breakpoint

## Conclusion

For the EmulationLoop hot path, the event-based counter approach is optimal because:

1. **Performance is critical**: The loop executes thousands of times per second
2. **O(1) vs O(n)**: Constant time is always better than linear time
3. **Common case**: Most of the time there are no active breakpoints
4. **Predictable**: Performance doesn't vary with number of breakpoints

The small complexity increase from event management is worth the guaranteed O(1) performance in the critical hot path.
