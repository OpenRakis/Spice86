# Spice86 Disassembly View Todo List

## Current Priorities

1. **Step Over Enhancement**
   - Modify step over functionality to properly handle loop instructions
   - Ensure the debugger doesn't step into loops when using step over
   - Add detection for common loop patterns (LOOP, LOOPE, LOOPNE, etc.)
   - Also ignore interrupts so we don't end up in the interrupt handler

2. **UI enhancements**
   - Add tooltip to flags
   - Add context menu to flags (Set to 0/1)

3. **Comments**
   - Add tooltip to "function" part of comment
   - Allow CRUD comments

4. **Branch Visualization**
   - Add indicators for branch instructions (JMP, Jcc, CALL, etc.)
   - Show whether branches will be taken based on current CPU state
   - Indicate jump direction (forward/backward) with visual cues
   - Consider adding arrow indicators or color coding

5. **Project files**
   - Define what data should be part of the state
   - Add saving current state to file
   - Add loading previous state

7. **Breakpoint Management** 
   - Implement keyboard shortcuts for common breakpoint operations (e.g., F2 to toggle breakpoint)

8. **Segment Visualization**
   - Add visual distinction between different segments in the disassembly view
   - Use background colors, borders, or other visual cues to indicate segment boundaries
   - Provide clear indication when execution crosses segment boundaries

9. **Self-Modifying Code Detection**
   - Detect when code modifies itself or other code regions
   - Provide visual indicators when instructions have been modified since last execution
   - Automatically refresh disassembly view when code modifications are detected
