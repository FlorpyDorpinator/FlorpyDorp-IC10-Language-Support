# Auto-Generation System

The IC10 LSP automatically generates type definitions and completions from Stationeers game source files. This ensures the extension stays up-to-date with game updates without manual maintenance.

## Overview

**Problem Solved**: Previously, logic types and other game constants were manually maintained in `instructions.rs`. This led to:
- Missing types (93 logic types were missing, including `VolumeOfLiquid`)
- Outdated definitions as game updates added new types
- Manual maintenance burden

**Solution**: Auto-generate all type definitions from authoritative game source files during build time.

## Architecture

### Build-Time Generation

The auto-generation happens in `build.rs` (Cargo build script) which runs before compilation:

```
Game Source Files (data/game-sources/)
    ↓
build.rs (parses & extracts)
    ↓
instructions_generated.rs (generated code)
    ↓
instructions.rs (includes generated code)
    ↓
Compiled into ic10lsp.exe
```

### Source Files

Three game source files drive the auto-generation:

1. **Enums.json** - Contains all game enums with values and descriptions
   - LogicType (257 entries)
   - SlotLogicType (8 entries)
   - BatchMode (4 entries)
   - ReagentMode (3 entries)

2. **Stationpedia.json** - Contains game documentation
   - Device information
   - Instruction descriptions
   - Logic type descriptions

3. **ProgrammableChip.cs** - Decompiled game code
   - GetCommandExample() method contains instruction parameter signatures
   - Used to generate instruction type hints

## What Gets Generated

The build script generates `instructions_generated.rs` containing:

```rust
// Auto-generated constants (pub visibility)
pub const LOGIC_TYPES: phf::Set<&'static str> = { ... };           // 257 entries
pub const LOGIC_TYPE_DOCS: phf::Map<&'static str, &'static str> = { ... };
pub const SLOT_LOGIC_TYPES: phf::Set<&'static str> = { ... };      // 8 entries
pub const SLOT_TYPE_DOCS: phf::Map<&'static str, &'static str> = { ... };
pub const BATCH_MODES: phf::Set<&'static str> = { ... };           // 4 entries
pub const BATCH_MODE_DOCS: phf::Map<&'static str, &'static str> = { ... };
pub const REAGENT_MODES: phf::Set<&'static str> = { ... };         // 3 entries
pub const REAGENT_MODE_DOCS: phf::Map<&'static str, &'static str> = { ... };
pub const INSTRUCTION_SIGNATURES: &[(&str, &[&[&str]])] = [ ... ]; // Instruction params
```

These constants are included directly into `instructions.rs` and override the old manual definitions.

## Updating for New Game Versions

When Stationeers releases an update with new logic types or instructions:

### Step 1: Extract Game Source Files

**Option A: From Game Installation**
1. Locate Stationeers installation (usually `Steam/steamapps/common/Stationeers`)
2. Copy `rocketstation_Data/StreamingAssets/Data/Enums.json`
3. Copy `rocketstation_Data/StreamingAssets/Language/english/Stationpedia.json`

**Option B: Decompile Assembly-CSharp.dll**
1. Use a tool like [ILSpy](https://github.com/icsharpcode/ILSpy) or [dnSpy](https://github.com/dnSpy/dnSpy)
2. Open `rocketstation_Data/Managed/Assembly-CSharp.dll`
3. Navigate to `Assets.Scripts.Objects.Electrical.ProgrammableChip`
4. Export `ProgrammableChip.cs` class to file

### Step 2: Replace Source Files

Copy the new files to the LSP data folder:
```bash
cd ic10lsp/data/game-sources/

# Replace with new versions
cp /path/to/new/Enums.json .
cp /path/to/new/Stationpedia.json .
cp /path/to/new/ProgrammableChip.cs .
```

### Step 3: Rebuild LSP

The auto-generation happens automatically during build:
```bash
cd ../../ic10lsp
cargo build --release
```

The build script will:
1. Parse the three source files
2. Extract all enum types and values
3. Generate `instructions_generated.rs`
4. Compile it into the LSP binary

### Step 4: Update Extension

Copy the new LSP binary to the VS Code extension:
```bash
# Windows
copy target\release\ic10lsp.exe "..\FlorpyDorp Language Support\bin\ic10lsp-win32.exe"

# Linux
cp target/release/ic10lsp "../FlorpyDorp Language Support/bin/ic10lsp-linux"

# macOS
cp target/release/ic10lsp "../FlorpyDorp Language Support/bin/ic10lsp-darwin"
```

### Step 5: Test

1. Reload VS Code window
2. Open any `.ic10` file
3. Type `s d0 ` and check that new logic types appear in completions
4. Verify tooltips show correct descriptions

## Implementation Details

### Parser Logic (build.rs)

**Enums.json Parser** (lines 242-320):
```rust
// Load and parse JSON
let enums_json: Value = serde_json::from_str(&enums_content)?;
let script_enums = enums_json["ScriptEnums"].as_object()?;

// Extract LogicType
if let Some(logic_type) = script_enums.get("LogicType") {
    if let Some(values) = logic_type["values"].as_object() {
        for (name, data) in values {
            let deprecated = data["deprecated"].as_bool().unwrap_or(false);
            if !deprecated {
                logic_types_builder.entry(name);
                let desc = data["description"].as_str().unwrap_or("");
                logic_type_docs_builder.entry(name, escape_str(desc));
            }
        }
    }
}
```

**ProgrammableChip.cs Parser** (lines 376-428):
```rust
// Find GetCommandExample method using regex
let method_regex = Regex::new(
    r"(?s)public static string GetCommandExample\s*\([^)]*\)\s*\{(.*?)\n\s*\}"
)?;

// Extract switch cases for each instruction
let case_regex = Regex::new(
    r#"case "(\w+)":\s*return "([^"]+)";"#
)?;

// Parse parameter types from helpstring
for cap in case_regex.captures_iter(method_body) {
    let instruction = cap[1];
    let helpstring = cap[2];
    // Parse parameters like "<color=yellow>add</color> <color=#0080FFFF>r?</color> ..."
}
```

**Generated Output Format**:
```rust
// Uses phf crate for compile-time perfect hash maps
pub const LOGIC_TYPES: phf::Set<&'static str> = phf_set! {
    "Power",
    "Open",
    // ... 255 more entries
    "VolumeOfLiquid",
};
```

### Code Integration (instructions.rs)

The generated file is included at the top of `instructions.rs`:

```rust
use std::fmt::Display;
use phf::{phf_map, phf_set};

// Include auto-generated constants
include!(concat!(env!("OUT_DIR"), "/instructions_generated.rs"));

// DataType enum and other manual code follows...
```

**Manual Definitions Commented Out**:
The old manual definitions (187 logic types) are preserved as comments:
```rust
/* MANUAL DEFINITIONS DISABLED - NOW AUTO-GENERATED FROM game-sources/Enums.json
   See include at top of file for auto-generated LOGIC_TYPES, etc.

#[allow(dead_code)]
pub const LOGIC_TYPES: phf::Set<&'static str> = phf_set! {
    "Power",
    "Open",
    // ... only 187 entries
};

END OF MANUAL SET DEFINITIONS */
```

## Verification

### Check Generated Output

After building, inspect the generated file:
```bash
# Find the generated file (path varies by build hash)
find target/debug/build/ic10lsp-*/out/instructions_generated.rs

# Check logic type count
grep -c "^\s*\"" target/debug/build/ic10lsp-*/out/instructions_generated.rs | head -1
# Should be 257+ for LOGIC_TYPES
```

### Test in Extension

Create a test file `test.ic10`:
```ic10
alias sensor d0

# Type "s d0 " then press Ctrl+Space
# Should show all 257 logic types including:
s d0 VolumeOfLiquid
s d0 VerticalRatio
s d0 MinimumWattsToContact
```

## Troubleshooting

### Build Fails - "File not found"
- **Cause**: Source files missing from `data/game-sources/`
- **Fix**: Verify all three files exist:
  ```bash
  ls data/game-sources/
  # Should show: Enums.json, Stationpedia.json, ProgrammableChip.cs
  ```

### Types Missing in Completions
- **Cause**: Old LSP binary still in use
- **Fix**: 
  1. Close all VS Code windows using the extension
  2. Copy new binary to extension bin folder
  3. Reopen VS Code and test

### Wrong Type Count
- **Cause**: Source files from wrong game version
- **Fix**: Re-extract from latest Stationeers installation

### Duplicate Definition Errors
- **Cause**: Manual definitions not properly commented out
- **Fix**: Check `instructions.rs` lines 189-930 are inside `/* ... */` comment blocks

## Benefits

✅ **Always Up-to-Date**: Game updates automatically reflected after file replacement  
✅ **Zero Manual Maintenance**: No need to track new logic types in code  
✅ **Authoritative Source**: Data comes directly from game, can't drift  
✅ **Type Safety**: Compile-time generation ensures correctness  
✅ **Performance**: phf crate provides O(1) lookups with zero runtime cost  

## History

**Before Auto-Generation**:
- 187 logic types manually maintained
- 93 types missing (36% incomplete)
- `VolumeOfLiquid` and other new types unavailable
- Manual updates required for each game patch

**After Auto-Generation**:
- 257 logic types from game data (100% coverage)
- All types include descriptions
- Update process: replace 3 files + rebuild
- Future-proof for game updates

## Related Files

- `build.rs` - Build script with parser logic
- `instructions.rs` - Main type definitions (includes generated code)
- `data/game-sources/` - Source files directory
- `Cargo.toml` - Dependencies (regex added to build-dependencies)
- `target/debug/build/ic10lsp-*/out/instructions_generated.rs` - Generated output

## Credits

Auto-generation system implemented November 2025 to solve missing logic type completions.
Based on game source extraction and Rust build.rs codegen patterns.
