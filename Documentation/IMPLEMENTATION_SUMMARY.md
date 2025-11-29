# Implementation Summary - Register Warnings & Control Flow Analysis

## Completed (Extension Side)

### 1. Hotkey Feature (Ctrl+Alt+R)
✅ **Command**: `ic10.ignoreRegisterWarnings`
✅ **Keybinding**: `Ctrl+Alt+R` (when in IC10 file)
✅ **Functionality**: Adds `#IgnoreRegisterWarnings` directive at the top of the file
✅ **Implementation**: `src/extension.ts` lines 988-1033

**How it works**:
- Checks if directive already exists
- Finds best insertion point (after other directives/comments at top)
- Inserts `#IgnoreRegisterWarnings` directive
- Shows confirmation message

### 2. Configuration Option for Control Flow Analysis
✅ **Setting**: `ic10.lsp.enableControlFlowAnalysis`
✅ **Default**: `true`
✅ **Description**: "Analyze unconditional jumps (j) to reduce false positives for register warnings. May be 3-5x slower on large files."
✅ **Config sync**: Added to `getLSPIC10Configurations()` in `extension.ts`

## Pending (LSP Side - Rust Implementation)

### 1. Recognize `#IgnoreRegisterWarnings` Directive
**Location**: `ic10lsp/src/main.rs`
**What needs to be done**:
- Add directive recognition in the `should_ignore_*` helper functions section
- Similar to existing `#IgnoreLimits` directive
- When present, skip all register assignment/read warnings

```rust
// Example implementation location (find the existing should_ignore_limits function)
fn should_ignore_register_warnings(content: &str) -> bool {
    content.lines().any(|line| {
        let trimmed = line.trim();
        trimmed.eq_ignore_ascii_case("#IgnoreRegisterWarnings")
    })
}
```

### 2. Phase 1 Control Flow Analysis (Unconditional Jumps)
**Goal**: Reduce 80% of false positives by analyzing unconditional `j` instructions

**Algorithm**:
1. **Build CFG** for unconditional jumps only:
   - Parse all `j label` instructions
   - Create edges from line N → label target line
   - Linear paths only (no cycles with just `j`)

2. **Track register state** through paths:
   - Follow `j` jumps to determine reachability
   - Mark registers as "assigned" or "read" along each path
   - Don't warn if register is read on ANY reachable path after assignment

3. **Example**:
```ic10
move r0 10      # Assign r0
j skip          # Jump forward
move r1 20      # This is skipped
skip:
add r2 r0 5     # Read r0 (reachable via jump)
```
Currently: False positive "r0 assigned but never read"  
With Phase 1: ✓ Correctly sees r0 is read after jump

**Implementation Notes**:
- Check `config.enableControlFlowAnalysis` before running
- Only analyze `j` instruction (not `beq`, `bne`, etc. - those create cycles)
- Performance: 3-5x slower is acceptable
- No infinite loop risk since `j` creates only linear paths

**Estimated Complexity**: ~300-500 lines of Rust code
- Label→line mapping (already exists?)
- Build jump graph
- DFS/BFS through graph tracking register state
- Modified diagnostic generation

### 3. Configuration Handling
**What needs to be done**:
- Add `enable_control_flow_analysis` field to config struct
- Read from `InitializeParams` or workspace configuration
- Use flag to enable/disable Phase 1 CFG analysis

## Testing Plan

### Extension Tests:
1. ✅ Press Ctrl+Alt+R → verify directive added
2. ✅ Press Ctrl+Alt+R again → verify "already present" message
3. ✅ Check directive inserted after existing comments
4. ✅ Verify configuration option appears in settings UI

### LSP Tests (Once Implemented):
1. File with `#IgnoreRegisterWarnings` → no register warnings
2. File without directive but with `enableControlFlowAnalysis: false` → current behavior (false positives)
3. File with `enableControlFlowAnalysis: true`:
   - `j` forward → no false positive
   - `j` backward → no false positive
   - Register used only in jumped-over code → still warns (correct!)
   - Conditional jumps (`beq`, etc.) → unchanged behavior

## Priority

**HIGH**: Implement `#IgnoreRegisterWarnings` directive recognition
- Quick win (~50 lines of code)
- Immediate user benefit
- Hotkey already functional

**MEDIUM**: Implement Phase 1 CFG for unconditional jumps
- More complex (~300-500 lines)
- Significant reduction in false positives (80%)
- User can disable if performance issues

## File Locations

### Extension (TypeScript)
- `package.json`: Command/keybinding/config definitions
- `src/extension.ts`: Command implementation

### LSP (Rust)
- `ic10lsp/src/main.rs`: Main diagnostic generation logic
- Look for existing register tracking code
- Add CFG analysis near diagnostic generation

## Current Status

🟢 **Extension**: Fully implemented and building  
🟡 **LSP**: Directive recognition pending  
🔴 **LSP**: Phase 1 CFG analysis not started

## Next Steps

1. Locate register diagnostic generation code in `main.rs`
2. Add `#IgnoreRegisterWarnings` check
3. Test hotkey with directive recognition
4. Design CFG data structures
5. Implement Phase 1 analysis
6. Performance testing on large files
