# Test Plan: Context-Aware Completions v1.3.0

## Overview
This document provides a comprehensive test plan for verifying all new completion features work correctly.

---

## Test Environment Setup

### Prerequisites
1. VS Code with IC10 extension v1.3.0 installed
2. LSP server v0.8.0 running
3. Test files in `dev/testing/` directory
4. Extension host running (F5 or Debug extension)

### Files to Use
- `test_device_completions.ic10` - Device completions in HASH()
- `test_context_aware_completions.ic10` - Parameter type filtering

---

## Test Suite 1: Device Completions in HASH()

### Test 1.1: Empty HASH() Shows All Devices
**Steps:**
1. Open `test_device_completions.ic10`
2. Type: `define test HASH("`
3. Place cursor inside quotes: `HASH("|")` (| = cursor)
4. Trigger completion (Ctrl+Space if needed)

**Expected Result:**
- ✅ Completion dropdown appears
- ✅ Shows all 1709 device names
- ✅ Includes: StructureVolumePump, ItemKit, StructureBattery, etc.
- ✅ Each item has detail showing: DisplayName → HashValue

### Test 1.2: Fuzzy Filtering - "Struct"
**Steps:**
1. Type: `define test HASH("Struct"`
2. Place cursor after "Struct"

**Expected Result:**
- ✅ Dropdown filters to only devices matching "Struct"
- ✅ Shows: StructureVolumePump, StructureBatterySmall, StructureConsole, etc.
- ✅ Does not show: ItemKit, CircuitboardAtmosAnalyser, etc.

### Test 1.3: Fuzzy Filtering - "Gas"
**Steps:**
1. Type: `define test HASH("Gas"`
2. Place cursor after "Gas"

**Expected Result:**
- ✅ Shows: GasSensor, GasTankStorageAtmospherics, etc.
- ✅ Fuzzy matching works: devices containing "Gas" anywhere in name

### Test 1.4: Case Insensitive Matching
**Steps:**
1. Type: `define test HASH("struct"`
2. Note lowercase "struct"

**Expected Result:**
- ✅ Shows same results as "Struct" (case-insensitive)
- ✅ StructureVolumePump, StructureBatterySmall, etc. appear

### Test 1.5: Exact Device Name
**Steps:**
1. Type: `define test HASH("StructureBattery"`
2. Select completion

**Expected Result:**
- ✅ Completion shows "StructureBattery" options
- ✅ Selecting inserts exact device name
- ✅ Quotes remain intact: `HASH("StructureBattery")`

### Test 1.6: Inlay Hint Shows Hash Value
**Steps:**
1. Complete line: `define pump HASH("StructureVolumePump")`
2. Look above the HASH() call

**Expected Result:**
- ✅ Inlay hint appears: `StructureVolumePump: -404336834`
- ✅ Gray shadow text above HASH()

---

## Test Suite 2: HASH() Number Diagnostic

### Test 2.1: Error on Numeric String
**Steps:**
1. Type: `define bad HASH("123")`
2. Wait for diagnostic to appear

**Expected Result:**
- ✅ Red squiggle under entire `HASH("123")`
- ✅ Error message: "Content inside HASH() argument cannot be a number. Use the hash value directly: 123"
- ✅ Severity: ERROR

### Test 2.2: Error on Negative Number
**Steps:**
1. Type: `define bad HASH("-456")`

**Expected Result:**
- ✅ Red squiggle appears
- ✅ Error message shows "-456"

### Test 2.3: Error on Zero
**Steps:**
1. Type: `define bad HASH("0")`

**Expected Result:**
- ✅ Red squiggle appears
- ✅ Error message shows "0"

### Test 2.4: No Error on Device Name
**Steps:**
1. Type: `define good HASH("StructureBattery")`

**Expected Result:**
- ✅ No error diagnostic
- ✅ No squiggle

### Test 2.5: Valid Direct Hash Value
**Steps:**
1. Type: `define valid -404336834`

**Expected Result:**
- ✅ No error diagnostic
- ✅ This is the correct way to use hash values

---

## Test Suite 3: Context-Aware Parameter Completions

### Test 3.1: LogicType Parameter (l instruction)
**Steps:**
1. Type: `l r0 d0 `
2. Place cursor after space
3. Trigger completion

**Expected Result:**
- ✅ Shows only LogicType completions: Temperature, Pressure, Power, etc.
- ❌ Does NOT show: r0, r1, r2 (registers)
- ❌ Does NOT show: d0, d1, d2 (devices)
- ❌ Does NOT show: numeric defines

### Test 3.2: Register Parameter (add instruction)
**Steps:**
1. Type: `add r0 `
2. Place cursor after space
3. Trigger completion

**Expected Result:**
- ✅ Shows register completions: r1-r15, ra, sp
- ✅ Shows register aliases if defined
- ❌ Does NOT show: Temperature, Pressure (logic types)
- ❌ Does NOT show: d0, d1 (devices)

### Test 3.3: Device Parameter (s instruction)
**Steps:**
1. Type: `s d0 Setting `
2. Place cursor after "Setting "
3. Trigger completion

**Expected Result:**
- ✅ Shows completions valid for Setting parameter (0/1)
- ✅ May show defines, numbers
- ✅ Context-appropriate for the parameter type

### Test 3.4: Branch Instruction Prioritizes Labels
**Steps:**
1. Create a label: `main:`
2. Type: `j `
3. Place cursor after space
4. Trigger completion

**Expected Result:**
- ✅ "main" label appears at top of list
- ✅ Labels prioritized over defines/aliases
- ✅ Other completions appear after labels

### Test 3.5: SlotLogicType Parameter (ls instruction)
**Steps:**
1. Type: `ls r0 d0 0 `
2. Place cursor after "0 "
3. Trigger completion

**Expected Result:**
- ✅ Shows SlotLogicType completions (OccupantHash, Quantity, etc.)
- ❌ Does NOT show: regular LogicType that don't apply to slots

### Test 3.6: BatchMode Parameter (lb instruction)
**Steps:**
1. Type: `lb r0 HASH("") `
2. Place cursor after space (after device hash)
3. Trigger completion

**Expected Result:**
- ✅ Shows BatchMode completions (Average, Sum, Minimum, Maximum)
- ❌ Does NOT show: regular LogicType

---

## Test Suite 4: STR() Completion Suppression

### Test 4.1: No Completions Inside STR()
**Steps:**
1. Type: `alias msg STR("`
2. Place cursor inside quotes: `STR("|")`
3. Trigger completion

**Expected Result:**
- ✅ No completion dropdown appears
- ✅ OR: Empty dropdown with no suggestions
- ❌ Does NOT show: LogicType, registers, devices

### Test 4.2: STR() Allows Freeform Text
**Steps:**
1. Type: `alias msg STR("Hello World")`

**Expected Result:**
- ✅ No errors
- ✅ No unwanted completions
- ✅ Text is freeform and unrestricted

### Test 4.3: Inlay Hint Shows STR() Value
**Steps:**
1. Type: `alias msg STR("Test Message")`

**Expected Result:**
- ✅ Inlay hint shows: `Test Message`
- ✅ Shadow text above STR()

---

## Test Suite 5: Syntax Highlighting

### Test 5.1: HASH() Content Color
**Steps:**
1. Type: `define pump HASH("StructureVolumePump")`
2. Observe text color inside quotes

**Expected Result:**
- ✅ "StructureVolumePump" displays in white
- ❌ NOT in green (old behavior)
- ✅ Matches color of device references (d0, d1)

### Test 5.2: STR() Content Color
**Steps:**
1. Type: `alias msg STR("Hello")`
2. Observe text color inside quotes

**Expected Result:**
- ✅ "Hello" displays in white
- ❌ NOT in green (old behavior)

### Test 5.3: Regular String Color (for comparison)
**Steps:**
1. Look at syntax in other contexts
2. Compare with HASH()/STR()

**Expected Result:**
- ✅ HASH()/STR() content uses variable.other.ic10 scope
- ✅ Distinct from regular string literals if any

---

## Test Suite 6: Regression Testing

### Test 6.1: Existing Features Still Work
**Steps:**
1. Test instruction completions: type `l` and see all load variants
2. Test register completions: type `r` and see r0-r15
3. Test device completions: type `d` and see d0-d5, db
4. Test logic type completions: type `Temp` and see Temperature

**Expected Result:**
- ✅ All existing completions still work
- ✅ No regressions in basic functionality

### Test 6.2: Inlay Hints Still Work
**Steps:**
1. Hover over HASH() with device name
2. Hover over STR() with string
3. Look for shadow text

**Expected Result:**
- ✅ HASH() shows device name and hash value
- ✅ STR() shows string content
- ✅ All inlay hints functional

### Test 6.3: Diagnostics Still Work
**Steps:**
1. Use undefined register: `l rx d0 Temperature`
2. Use undefined device: `l r0 dx Temperature`
3. Use wrong number of parameters: `l r0`

**Expected Result:**
- ✅ Errors for undefined registers
- ✅ Errors for undefined devices
- ✅ Errors for incorrect parameters
- ✅ All existing diagnostics functional

### Test 6.4: Hover Documentation Still Works
**Steps:**
1. Hover over instruction: `add`
2. Hover over logic type: `Temperature`
3. Hover over register: `r0`

**Expected Result:**
- ✅ Documentation tooltip appears
- ✅ Shows parameter signatures
- ✅ Shows descriptions

---

## Test Suite 7: Edge Cases

### Test 7.1: HASH() with Empty String
**Steps:**
1. Type: `define test HASH("")`
2. Place cursor inside quotes

**Expected Result:**
- ✅ Shows all 1709 devices
- ✅ No crash or error

### Test 7.2: HASH() with Special Characters
**Steps:**
1. Type: `define test HASH("@#$")`

**Expected Result:**
- ✅ No completions match (expected)
- ✅ No crash or error

### Test 7.3: Nested Quotes (Invalid Syntax)
**Steps:**
1. Type: `define test HASH("Test"More")`

**Expected Result:**
- ✅ Parser handles gracefully
- ✅ May show syntax error (expected)

### Test 7.4: Very Long Device Name
**Steps:**
1. Type full device name: `HASH("ModularDeviceConsoleLabelPlateSmall")`

**Expected Result:**
- ✅ Completion works
- ✅ Inlay hint displays correctly
- ✅ No truncation issues

### Test 7.5: Multiple HASH() on Same Line
**Steps:**
1. Type: `define a HASH("") define b HASH("")`
2. Test completions in both

**Expected Result:**
- ✅ Both HASH() instances show device completions
- ✅ Independent completion contexts

---

## Test Suite 8: Performance

### Test 8.1: Completion Response Time
**Steps:**
1. Type: `HASH("`
2. Measure time until dropdown appears

**Expected Result:**
- ✅ Dropdown appears within 200ms
- ✅ No lag or freezing

### Test 8.2: Filtering Performance
**Steps:**
1. Type: `HASH("S"`
2. Observe filter speed as you type more characters

**Expected Result:**
- ✅ Filtering updates immediately
- ✅ No visible lag when typing

### Test 8.3: Large File Performance
**Steps:**
1. Open file with 500+ lines of IC10 code
2. Test completions in HASH()
3. Test context-aware completions

**Expected Result:**
- ✅ Completions still responsive
- ✅ No significant slowdown

---

## Test Suite 9: Integration

### Test 9.1: Completion + Inlay Hint
**Steps:**
1. Complete: `define pump HASH("StructureVolumePump")`
2. Check inlay hint appears above

**Expected Result:**
- ✅ Completion works
- ✅ Inlay hint appears with correct hash value
- ✅ Both features work together

### Test 9.2: Completion + Diagnostic
**Steps:**
1. Complete: `define bad HASH("123")`
2. Check for both completion and diagnostic

**Expected Result:**
- ✅ (May have completed "123" if it's typed)
- ✅ Diagnostic appears showing error
- ✅ Both features coexist

### Test 9.3: Completion + Hover Documentation
**Steps:**
1. Type: `l r0 d0 Temp`
2. Complete to "Temperature"
3. Hover over "Temperature"

**Expected Result:**
- ✅ Completion works
- ✅ Hover shows "Current Temperature" documentation

---

## Pass/Fail Criteria

### Critical (Must Pass)
- ✅ Device completions appear inside HASH()
- ✅ Context-aware filtering shows only relevant types
- ✅ HASH() number diagnostic catches "123"
- ✅ STR() shows no completions
- ✅ No regressions in existing features

### Important (Should Pass)
- ✅ Fuzzy filtering works correctly
- ✅ Case-insensitive matching
- ✅ Syntax highlighting white (not green)
- ✅ Branch instructions prioritize labels
- ✅ Performance is acceptable (<200ms)

### Nice to Have
- ✅ Edge cases handled gracefully
- ✅ Large files still performant
- ✅ All integration scenarios work

---

## Bug Report Template

If any test fails, use this template:

```
**Test ID**: [e.g., Test 1.1]
**Test Name**: [e.g., Empty HASH() Shows All Devices]
**Status**: FAIL
**Expected**: [What should happen]
**Actual**: [What actually happened]
**Steps to Reproduce**:
1. [Step 1]
2. [Step 2]
3. [Step 3]

**Environment**:
- Extension Version: 1.3.0
- LSP Version: 0.8.0
- VS Code Version: [version]
- OS: Windows [version]

**Additional Notes**: [Any other relevant information]
```

---

## Success Checklist

After running all tests:
- [ ] All Test Suite 1 tests pass (Device Completions)
- [ ] All Test Suite 2 tests pass (Number Diagnostic)
- [ ] All Test Suite 3 tests pass (Context-Aware Completions)
- [ ] All Test Suite 4 tests pass (STR Suppression)
- [ ] All Test Suite 5 tests pass (Syntax Highlighting)
- [ ] All Test Suite 6 tests pass (Regression Testing)
- [ ] All Test Suite 7 tests pass (Edge Cases)
- [ ] All Test Suite 8 tests pass (Performance)
- [ ] All Test Suite 9 tests pass (Integration)

---

## Next Steps After Testing

1. Document any bugs found
2. Fix critical issues before release
3. Update documentation with any unexpected behaviors
4. Consider user feedback on completion UX
5. Plan future improvements based on test results
