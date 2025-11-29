# Game Source Files

This directory contains source files extracted from Stationeers that drive the auto-generation of type definitions and completions in the IC10 LSP.

## Files

### Enums.json
**Source**: `Stationeers/rocketstation_Data/StreamingAssets/Data/Enums.json`  
**Purpose**: Contains all game enum definitions with values, descriptions, and deprecation flags  
**Used For**:
- LogicType (257 entries) - All logic types for `l`/`s` instructions
- SlotLogicType (8 entries) - All slot logic types for `ls`/`ss` instructions  
- BatchMode (4 entries) - Batch operation modes (Average, Sum, Min, Max)
- ReagentMode (3 entries) - Reagent query modes (Contents, Required, Recipe)

**Current Version**: Respawn Update Beta (v0.2.6054.26551) - November 28, 2025

### Stationpedia.json
**Source**: `Stationeers/rocketstation_Data/StreamingAssets/Language/english/Stationpedia.json`  
**Purpose**: Game documentation and help text  
**Used For**:
- Device descriptions and display names
- Instruction descriptions (scriptCommands section)
- Logic type extended documentation

**Current Version**: Respawn Update Beta (v0.2.6054.26551) - November 28, 2025

### ProgrammableChip.cs
**Source**: Decompiled from `Stationeers/rocketstation_Data/Managed/Assembly-CSharp.dll`  
**Class**: `Assets.Scripts.Objects.Electrical.ProgrammableChip`  
**Purpose**: IC10 virtual machine implementation  
**Used For**:
- GetCommandExample() method contains instruction parameter signatures
- Instruction help strings with parameter types
- Validation logic

**Current Version**: Respawn Update Beta (v0.2.6054.26551) - November 28, 2025  
**Decompiler**: ILSpy 8.2

## Update Process

When Stationeers releases a new version with updated IC10 features:

### 1. Extract Files from Game

**Enums.json and Stationpedia.json**:
```bash
# Find Stationeers installation
STEAM_PATH="C:/Program Files (x86)/Steam/steamapps/common/Stationeers"

# Copy files
cp "$STEAM_PATH/rocketstation_Data/StreamingAssets/Data/Enums.json" .
cp "$STEAM_PATH/rocketstation_Data/StreamingAssets/Language/english/Stationpedia.json" .
```

**ProgrammableChip.cs**:
1. Download ILSpy: https://github.com/icsharpcode/ILSpy/releases
2. Open `Assembly-CSharp.dll` from `rocketstation_Data/Managed/`
3. Navigate to `Assets.Scripts.Objects.Electrical` → `ProgrammableChip`
4. Right-click class → "Save Code..." → Save as `ProgrammableChip.cs`
5. Copy to this directory

### 2. Verify File Contents

**Check Enums.json**:
```bash
# Should contain ScriptEnums with LogicType, SlotLogicType, etc.
jq '.ScriptEnums.LogicType.values | length' Enums.json
# Expected: 257+ (as of Nov 2025)
```

**Check Stationpedia.json**:
```bash
# Should contain scriptCommands
jq '.scriptCommands | length' Stationpedia.json
# Expected: 100+ instructions
```

**Check ProgrammableChip.cs**:
```bash
# Should contain GetCommandExample method
grep -c "GetCommandExample" ProgrammableChip.cs
# Expected: 1
```

### 3. Rebuild LSP

The auto-generation happens automatically:
```bash
cd ../
cargo build --release
```

See [AUTO-GENERATION.md](../AUTO-GENERATION.md) for complete update instructions.

## File Format Details

### Enums.json Structure
```json
{
  "ScriptEnums": {
    "LogicType": {
      "values": {
        "Power": {
          "value": 0,
          "description": "Can be read to return if the device is correctly powered...",
          "deprecated": false
        },
        "VolumeOfLiquid": {
          "value": 254,
          "description": "The total volume of all liquids in Liters in the atmosphere",
          "deprecated": false
        }
      }
    }
  }
}
```

### Stationpedia.json Structure  
```json
{
  "scriptCommands": {
    "add": {
      "desc": "Register = a + b.",
      "example": "<color=yellow>add</color> <color=#0080FFFF>r?</color> ..."
    }
  }
}
```

### ProgrammableChip.cs Key Section
```csharp
public static string GetCommandExample(string command) {
    switch (command.ToLower()) {
        case "add":
            return "<color=yellow>add</color> <color=#0080FFFF>r?</color> a(<color=#0080FFFF>r?</color><color=#585858FF>|</color><color=#20B2AA>num</color>) b(<color=#0080FFFF>r?</color><color=#585858FF>|</color><color=#20B2AA>num</color>)";
        // ... 100+ more cases
    }
}
```

## Version History

| Game Version | Date | Logic Types | Notable Changes |
|--------------|------|-------------|----------------|
| v0.2.6054.26551 (Respawn Beta) | Nov 28, 2025 | 257 | Added VolumeOfLiquid, trader pointing logic types |
| v0.2.5xxx (Previous) | Earlier | ~200 | Base set |

## Backup

Before updating files, backup the current versions:
```bash
# Create backup with date
DATE=$(date +%Y-%m-%d)
mkdir -p "backups/$DATE"
cp Enums.json Stationpedia.json ProgrammableChip.cs "backups/$DATE/"
```

## Troubleshooting

**"LogicType not found in Enums.json"**
- File may be from wrong location or corrupted
- Verify JSON is valid: `jq . Enums.json > /dev/null`

**"GetCommandExample not found"**
- Wrong class decompiled
- Ensure you exported `ProgrammableChip` not `ProgrammableChipEditor`

**"Old types still showing"**
- LSP binary not updated after rebuild
- Copy new binary to extension: `cp ../target/release/ic10lsp.exe "../../FlorpyDorp Language Support/bin/ic10lsp-win32.exe"`

## Credits

Source file extraction methodology established November 2025.  
Auto-generation system by GitHub Copilot based on game source analysis.
