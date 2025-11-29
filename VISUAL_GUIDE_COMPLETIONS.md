# Visual Guide: New Completion Features in v1.3.0

## 1. Device Completions in HASH()

### Before v1.3.0:
```ic10
define pump HASH("_")  # No completions, had to manually type device name
```

### After v1.3.0:
```ic10
define pump HASH("Struct_")  # 🎉 Dropdown shows:
# - StructureVolumePump (StructureVolumePump → -404336834)
# - StructureBatterySmall (Small Battery → 1958427767)
# - StructureConsole (Console → -928148858)
# - StructureSensor (Sensor → 1279118686)
# ... and more! (fuzzy filtered from 1709 devices)
```

**How it works:**
- Type opening quote: `HASH("`
- Start typing device name: `Struct`
- See filtered device completions
- Select device → automatically inserts device name
- Hover over HASH() to see computed hash value in inlay hint

---

## 2. Context-Aware Parameter Completions

### LogicType Parameters
```ic10
l r0 d0 _  # Only shows: Temperature, Pressure, Power, etc.
           # ❌ No registers (r0, r1) - not valid for this parameter
           # ❌ No devices (d0, d1) - not valid for this parameter
```

### Register Parameters
```ic10
add r0 _   # Only shows: r1-r15, ra, sp, and register aliases
           # ❌ No logic types (Temperature, Pressure) - not valid
           # ❌ No devices (d0, d1) - not valid
```

### Device Parameters
```ic10
s d0 Setting _  # Shows: device references and device aliases
                # Also shows LogicType for the value (0/1 for Setting)
```

### Branch Instructions (Special Priority)
```ic10
j _        # Prioritizes labels first (main, loop, end)
           # Then shows defines, aliases, enums
           # ✅ Labels are most relevant for jumps
```

---

## 3. HASH() Number Validation

### Error Diagnostic
```ic10
define pump HASH("123")  # ❌ ERROR: "Content inside HASH() argument cannot be a number. Use the hash value directly: 123"
                         # Red squiggle under entire HASH("123")
```

### Correct Usage
```ic10
define pump -404336834   # ✅ Correct: Use hash value directly
define pump HASH("StructureVolumePump")  # ✅ Also correct: Use device name
```

---

## 4. STR() Completion Suppression

### No Completions in STR()
```ic10
alias msg STR("_")  # No completions appear
                    # Strings are freeform text
                    # Prevents clutter from logic types/registers
```

---

## 5. Syntax Highlighting Fix

### Before v1.3.0:
```ic10
HASH("Device")  # "Device" displayed in green (like a string)
```

### After v1.3.0:
```ic10
HASH("Device")  # "Device" displays in white (like a variable)
                # Matches the visual style of device references
```

---

## Feature Comparison Table

| Feature | Before v1.3.0 | After v1.3.0 |
|---------|---------------|--------------|
| HASH() completions | ❌ No completions | ✅ 1709 device names with fuzzy filter |
| Parameter filtering | ❌ All types shown everywhere | ✅ Only relevant types for each parameter |
| HASH() number detection | ❌ No error | ✅ ERROR diagnostic with guidance |
| STR() completions | ❌ Logic types appear | ✅ No completions (cleaner) |
| HASH()/STR() color | ❌ Green (like strings) | ✅ White (like variables) |

---

## Usage Examples

### Example 1: Setting up devices with completions
```ic10
# Old way: Manual lookup of device hash
define pump -404336834

# New way: Type device name with autocomplete
define pump HASH("Struct")  # Select StructureVolumePump from dropdown
```

### Example 2: Reading temperature
```ic10
# Context-aware completion guides you
l r0 d0   # Type space, see only LogicType completions
          # Select "Temperature" from dropdown
          # Result: l r0 d0 Temperature
```

### Example 3: Complex logic with multiple parameters
```ic10
# Each parameter shows only relevant completions
s d0 Setting 1        # "Setting" from LogicType, "1" from numbers
add r0 r1 10          # r0, r1 from registers, 10 from numbers
j loop                # "loop" prioritized from labels
```

---

## Technical Benefits

1. **Faster Development**: Less scrolling through irrelevant completions
2. **Error Prevention**: Diagnostic catches HASH("123") mistake immediately
3. **Better Learning**: Context-aware hints teach parameter requirements
4. **Cleaner Code**: Visual consistency with white text in HASH()/STR()
5. **Intuitive UX**: Device names appear when you need them

---

## How to Test

1. Open any `.ic10` file in VS Code
2. Try typing: `define sensor HASH("")`
3. Place cursor inside quotes and start typing: `HASH("Struct")`
4. See device completions appear! 🎉
5. Try: `l r0 d0 ` and verify only LogicType completions appear
6. Try: `HASH("123")` and verify error squiggle appears

---

## Screenshots

### Device Completions
![Device completions in HASH()](../images/device-hash-demo.gif)
*Placeholder - Add screenshot showing dropdown inside HASH("Struct")*

### Context-Aware Completions
![Context-aware parameter filtering](../images/context-aware-completions.gif)
*Placeholder - Add screenshot showing different completions for different parameter types*

### Number Diagnostic
![Error for numbers in HASH()](../images/hash-number-error.png)
*Placeholder - Add screenshot showing red squiggle on HASH("123")*
